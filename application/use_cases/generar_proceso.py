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
from infrastructure.tia_service import TIAService
from infrastructure.xml_generator import XMLGenerator


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

    def __init__(self, tia: TIAService) -> None:
        self._tia = tia
        self._generador = XMLGenerator()
        self._logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")

    def deducir_proceso_origen(
        self,
        ruta_plantilla: str,
        procesos: list[Proceso]
    ) -> Proceso:
        archivos_xml = list(Path(ruta_plantilla).rglob("*.xml"))
        if not archivos_xml:
            raise PlantillaVaciaError(
                f"No se encontraron archivos XML en: {ruta_plantilla}"
            )

        primer_archivo = archivos_xml[0].stem

        # Intento 1: Extraer UID por patrón numérico
        match_num = re.search(r'(?:FC|FB|DB|OB)?5\d(\d{3})', primer_archivo, re.IGNORECASE)
        if match_num:
            uid_origen = int(match_num.group(1))
            proceso = next((p for p in procesos if p.uid == uid_origen), None)
            if proceso:
                return proceso

        # Intento 2: Buscar coincidencia por nombre o código
        for p in procesos:
            nombre_upper = p.nombre.upper()
            codigo_upper = p.codigo.upper()
            archivo_upper = primer_archivo.upper()
            if (f"_{nombre_upper}_" in f"_{archivo_upper}_" or
                    f"_{codigo_upper}_" in f"_{archivo_upper}_"):
                return p

        raise ProcesoOrigenNoEncontradoError(
            f"No se pudo deducir el proceso origen desde: '{primer_archivo}'"
        )

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

        # 3. POST-CHECK: Compilación de validación
        if exito:
            self._logger.info("Iniciando Post-Check de compilación...")
            if not self._tia.compilar_software(plc_nombre):
                self._logger.error("❌ ¡ALERTA! La inyección fue exitosa pero la compilación posterior falló. Revisa TIA Portal.")
                # Retornamos True porque la inyección física sí ocurrió, pero el log dejará constancia del error.
        
        return exito
