"""
Application Layer - Use Case: Generar Proceso
=============================================
Orquesta el flujo de generación sin dependencias de UI.
"""
from dataclasses import dataclass
from pathlib import Path
import logging
import re

from core.models import BloquePLC, Proceso
from core.ports import ISoftwareRepository
from infrastructure.xml.generator import XMLGenerator


@dataclass
class ResultadoPreFlight:
    """Resultado del análisis de colisiones previo a la generación."""
    bloques_predichos: list[BloquePLC]
    colisiones_nombre: list[BloquePLC]
    colisiones_numero: list[tuple[BloquePLC, BloquePLC]]

    @property
    def tiene_colisiones(self) -> bool:
        return bool(self.colisiones_nombre or self.colisiones_numero)


@dataclass
class ResultadoGeneracion:
    """Resultado completo de una generación."""
    exito: bool
    ruta_build: str
    archivos_generados: int
    error: str | None = None


class ProcesoOrigenNoEncontradoError(Exception):
    """No se pudo deducir el proceso origen desde la plantilla."""
    pass


class PlantillaVaciaError(Exception):
    """La carpeta de plantillas no contiene archivos XML."""
    pass


class GenerarProcesoUseCase:
    """
    Caso de uso: Generar un proceso nuevo a partir de una plantilla.
    Orquesta: deducción de origen, escaneo de PLC, cálculo de
    colisiones, generación de XML e inyección en TIA Portal.
    No tiene dependencias de UI.
    """

    def __init__(self, tia: ISoftwareRepository) -> None:
        self._tia = tia
        self._generador = XMLGenerator()
        self._logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")

    def deducir_proceso_origen(self, ruta_plantilla: str) -> Proceso:
        """
        Deducc el Proceso origen PURAMENTE a partir del nombre del archivo
        de la plantilla. NO consulta el Excel del usuario.

        Por que NO se consulta el Excel:
          - La plantilla es un archivo fisico del usuario (un proyecto TIA
            Portal exportado a XML) que no debe contaminar la base de datos
            de produccion del Excel Maestro.
          - El Excel solo aporta el proceso destino (el NUEVO proceso a
            generar). El origen se infiere 100% del filesystem.

        Estrategia:
          1. Listar todos los XML de la plantilla.
          2. Aplicar la regex sobre el primer archivo encontrado.
          3. Si matchea: extraer uid_origen (group 2) y codigo_inferido
             (group 3) y construir el Proceso en memoria.
          4. Si NO matchea: lanzar ProcesoOrigenNoEncontradoError.

        Args:
            ruta_plantilla: ruta a la carpeta que contiene los XML de la
                plantilla del proceso origen.

        Returns:
            Proceso(uid=uid_origen, nombre=codigo_inferido, codigo=codigo_inferido)
            creado en memoria sin tocar la base de datos de produccion.

        Raises:
            PlantillaVaciaError: si la carpeta no tiene archivos XML.
            ProcesoOrigenNoEncontradoError: si el archivo no contiene el
                patron esperado (5\\d{4}_xxx).
        """
        archivos_xml = list(Path(ruta_plantilla).rglob("*.xml"))
        if not archivos_xml:
            raise PlantillaVaciaError(
                f"No se encontraron archivos XML en: {ruta_plantilla}"
            )

        primer_archivo = archivos_xml[0].stem

        # CRITICO: usamos re.search (NO re.match) porque 'primer_archivo'
        # es una RUTA COMPLETA de sistema (ej. C:\...\DB50100_Algo.xml) y
        # re.match con anclas ^...$ fallaria al no encontrar el patron al
        # inicio. re.search busca el patron en cualquier posicion del
        # string, asi que funciona con rutas absolutas.
        # El grupo 3 usa [^.]+ para capturar el nombre del bloque hasta
        # el primer punto de la extension .xml (evita capturar el ".xml").
        # IMPORTANTE: la regex captura el numero COMPLETO del bloque (5XXXX)
        # en el grupo 2. Antes era `(DB|FC|FB|OB)5(\d{4})` lo que daba
        # uid=100 en lugar de 50100 (bug historico compensado en
        # `calcular_diccionario_reemplazos` con `base_plantilla = 50000 +
        # origen.uid`). Como ahora el origen se deduce directamente, el
        # uid_origen DEBE ser el numero completo del bloque.
        match_num = re.search(
            r'(DB|FC|FB|OB)5(\d{4})_([^.]+)',
            str(primer_archivo),
            re.IGNORECASE
        )
        if not match_num:
            raise ProcesoOrigenNoEncontradoError(
                f"No se pudo deducir el proceso origen desde: '{primer_archivo}'. "
                f"Esperado patron: (DB|FC|FB|OB)5XXXX_xxx (ej. DB50100_CPR_PRINCIPAL)."
            )

        uid_origen = int(match_num.group(2))
        codigo_inferido = match_num.group(3).upper()

        # Construir el Proceso en memoria directamente. NO se consulta
        # ningun repositorio ni base de datos de produccion.
        proceso_origen = Proceso(
            uid=uid_origen,
            nombre=codigo_inferido,
            codigo=codigo_inferido,
        )

        self._logger.info(
            f"Proceso origen deducido desde archivo (sin consultar Excel): "
            f"uid={uid_origen}, codigo='{codigo_inferido}'."
        )
        return proceso_origen

    def ejecutar_preflight(
        self,
        ruta_plantilla: str,
        proceso_origen: Proceso,
        proceso_destino: Proceso,
        plc_nombre: str,
    ) -> ResultadoPreFlight:
        self._logger.info("Ejecutando comprobaciones previas...")
        bloques_en_plc = self._tia.get_existing_blocks(plc_nombre)

        reemplazos = self._generador.calcular_diccionario_reemplazos(
            ruta_plantilla, proceso_origen, proceso_destino
        )
        bloques_predichos = self._generador.predecir_bloques_generados(
            ruta_plantilla, reemplazos
        )

        colisiones_nombre: list[BloquePLC] = []
        colisiones_numero: list[tuple[BloquePLC, BloquePLC]] = []

        for predicho in bloques_predichos:
            # Normalizar para búsqueda case-insensitive (el caché guarda en minúsculas)
            clave_normalizada = predicho.nombre.replace('\xa0', '').replace(' ', '').strip().lower()
            if clave_normalizada in bloques_en_plc:
                colisiones_nombre.append(predicho)
            if predicho.numero > 0:
                for bloque_existente in bloques_en_plc.values():
                    if (bloque_existente.numero == predicho.numero and
                            bloque_existente.tipo == predicho.tipo):
                        colisiones_numero.append((predicho, bloque_existente))
                        break

        return ResultadoPreFlight(
            bloques_predichos=bloques_predichos,
            colisiones_nombre=colisiones_nombre,
            colisiones_numero=colisiones_numero,
        )

    def generar_y_exportar(
        self,
        ruta_plantilla: str,
        proceso_origen: Proceso,
        proceso_destino: Proceso,
    ) -> ResultadoGeneracion:
        try:
            reemplazos = self._generador.calcular_diccionario_reemplazos(
                ruta_plantilla, proceso_origen, proceso_destino
            )
            ruta_build = self._generador.generar_archivos(ruta_plantilla, reemplazos)
            archivos = len(list(Path(ruta_build).rglob("*.xml")))
            return ResultadoGeneracion(exito=True, ruta_build=ruta_build, archivos_generados=archivos)
        except Exception as e:
            self._logger.error(f"Error durante la generación: {e}", exc_info=True)
            return ResultadoGeneracion(exito=False, ruta_build="", archivos_generados=0, error=str(e))

    def inyectar_en_tia(self, plc_nombre: str, ruta_build: str, proceso_nombre: str = "desconocido") -> bool:
        self._logger.info("Iniciando Pre-Check de compilación...")
        
        # 1. PRE-CHECK: Compilación preventiva
        if not self._tia.compilar_software(plc_nombre):
            self._logger.error("❌ El PLC tiene errores de compilación previos. Se aborta la inyección para evitar corrupción.")
            return False

        # 2. INYECCIÓN
        self._logger.info("Inyectando XMLs en TIA Portal...")
        exito = self._tia.importar_bloques_generados(plc_nombre, ruta_build, proceso_nombre)

        # 3. POST-CHECK: Compilación de validación GLOBAL (no aislada).
        # La compilación global resuelve primero las constantes N_MAX y
        # luego compila todos los bloques. Esto evita el bug de Openness
        # con bloques recién importados que dependen de constantes.
        # Si la post-compilacion falla, retornamos False (no True) para
        # que el caller (orquestador superior) detecte el fallo y haga
        # ROLLBACK. El bug original era retornar exito=True aunque la
        # compilacion fallara (falso positivo).
        if exito:
            self._logger.info("Iniciando Post-Check de compilación...")
            if not self._tia.compilar_software(plc_nombre):
                self._logger.error(
                    "❌ ¡ALERTA! La inyección fue exitosa pero la "
                    "compilación global posterior falló. "
                    "Retornando False para forzar ROLLBACK."
                )
                return False  # ← FIX: era "return exito" (que era True)
        return exito
