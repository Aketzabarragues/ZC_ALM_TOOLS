"""
Infrastructure Layer - XML Generator
===================================
Generador de archivos XML a partir de plantillas.

IMPORTANTE: la sustitucion del CONTENIDO de los XMLs se hace de forma
QUIRURGICA via ProcesoXMLModifier, que parsea el XML con xml.etree.ElementTree
y modifica SOLO las etiquetas de la whitelist (Name, Number, Comment, Title).
Esto evita corromper IDs internos y cross-references de Siemens (que era el
bug del str.replace global).
"""

import logging
import re
import shutil
from pathlib import Path

from core.models import BloquePLC, Proceso
from infrastructure import config_manager
from infrastructure.xml.proceso_modifier import ProcesoXMLModifier

__all__ = ["XMLGenerator", "XMLGeneratorError"]


class XMLGeneratorError(Exception):
    """Base exception for XML Generator."""
    pass


class XMLGenerator:
    """
    Genera archivos XML mutados a partir de plantillas.
    """

    def __init__(self) -> None:
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )

    def calcular_diccionario_reemplazos(
        self,
        ruta_plantilla: str,
        origen: Proceso,
        destino: Proceso
    ) -> dict[str, str]:
        """
        Construye un diccionario quirurgico extrayendo nombres de bloques y variables.

        IMPORTANTE: este metodo SOLO trabaja con los NOMBRES de archivos
        (paths del sistema operativo), NO con el contenido de los XMLs.
        Por tanto, es seguro usar regex y str.replace aqui.

        La sustitucion del CONTENIDO de los XMLs se hace luego en
        `generar_archivos()` con el ProcesoXMLModifier (parseo XML seguro).
        """
        self._logger.info(
            f"Calculando Diccionario Quirurgico: {origen.nombre} ({origen.codigo}) -> "
            f"{destino.nombre} ({destino.codigo})"
        )

        diccionario: dict[str, str] = {}
        ruta_origen = Path(ruta_plantilla)

        if not ruta_origen.exists():
            self._logger.warning("Ruta de plantilla no existe para calcular diccionario.")
            return diccionario

        base_plantilla = 50000 + origen.uid
        delta = destino.uid - base_plantilla

        for filepath in ruta_origen.rglob("*.xml"):
            nombre_original = filepath.stem
            match = re.match(r"^(FC|FB|DB)(\d+)(.*)", nombre_original, re.IGNORECASE)
            if match:
                tipo = match.group(1).upper()
                numero_original = int(match.group(2))
                resto_nombre = match.group(3)

                numero_nuevo = numero_original + delta if numero_original >= 50000 else numero_original
                nombre_proyectado = f"{tipo}{numero_nuevo}{resto_nombre}"

                nombre_proyectado = nombre_proyectado.replace(
                    f"_{origen.codigo}_", f"_{destino.codigo}_"
                )

                diccionario[nombre_original] = nombre_proyectado

        # Tambien los nombres de las tablas de variables
        rutas_tablas = [
            f for f in ruta_origen.rglob("*.xml")
            if "TABLA" in str(f.parent).upper() or "TAG" in str(f.parent).upper()
        ]
        for tabla_path in rutas_tablas:
            nombre_tabla = tabla_path.stem

            match_tabla = re.match(r"(5\d{4})_(.*)", nombre_tabla)
            if match_tabla:
                num_tabla = int(match_tabla.group(1))
                cod_dest = destino.codigo.strip() if destino.codigo else ""
                nombre_tabla_mutado = f"{num_tabla + delta}_{cod_dest}"

                if nombre_tabla != nombre_tabla_mutado:
                    diccionario[nombre_tabla] = nombre_tabla_mutado

            # ------------------------------------------------------------------ #
            #  INYECCION DE CONSTANTES DE LA TABLA DE VARIABLES
            #  Leemos el contenido del XML de la tabla como texto plano
            #  y extraemos todos los <Name>5\d{4}_[^<]+</Name> (constantes del
            #  proceso, ej. 50100_N_MAX_PREAL, 50100_CPR_PRINCIPAL_DEST_1).
            #
            #  Para cada constante calculamos el nuevo nombre aplicando:
            #    1. Suma del delta al prefijo numerico 5\d{4} (50100 -> 1600)
            #    2. Reemplazo del codigo de proceso si aparece (CPR_PRINCIPAL
            #       -> CPR_2)
            #
            #  Asi la FASE 1 (str.replace global) del modifier captura
            #  cada constante como una sola unidad atomica. Sin esto, la
            #  sola Fase 2 (delta quirurgico) dejaria el sufijo textual con
            #  el codigo antiguo.
            # ------------------------------------------------------------------ #
            try:
                contenido_tabla = tabla_path.read_text(encoding="utf-8")
            except Exception as e:
                self._logger.warning(
                    f"No se pudo leer la tabla {tabla_path.name}: {e}"
                )
                continue

            # Regex simple: <Name>50100_xxx</Name> o <Name>50100</Name>.
            # Capturamos cualquier nombre que empiece con 5\d{4}_ para
            # asegurarnos de que es una constante del proceso (no un
            # bloque generico).
            constantes_encontradas = re.findall(
                r"<Name>(5\d{4}_[^<]+)</Name>",
                contenido_tabla,
            )
            # Tambien capturamos constantes que son SOLO el numero
            # (ej. <Name>50100</Name> en tablas simples).
            constantes_solo_numero = re.findall(
                r"<Name>(5\d{4})</Name>",
                contenido_tabla,
            )
            constantes_encontradas.extend(constantes_solo_numero)

            cod_origen = origen.codigo.strip() if origen.codigo else ""
            cod_destino = destino.codigo.strip() if destino.codigo else ""

            for constante in constantes_encontradas:
                # 1) Suma del delta al prefijo 5\d{4}
                nuevo_nombre = re.sub(
                    r"5\d{4}",
                    lambda m: str(int(m.group(0)) + delta),
                    constante,
                )
                # 2) Reemplazo del codigo de proceso (si aparece)
                if cod_origen and cod_destino and cod_origen != cod_destino:
                    nuevo_nombre = nuevo_nombre.replace(cod_origen, cod_destino)

                if nuevo_nombre != constante:
                    diccionario[constante] = nuevo_nombre

        # Metadatos para uso interno (replacements de nombre/rutas, no para XML)
        diccionario["__delta__"] = str(delta)
        diccionario["__nombre_origen__"] = origen.nombre
        diccionario["__nombre_destino__"] = destino.nombre

        # Ordenar por longitud descendente para evitar colisiones en str.replace
        reemplazos_ordenados = dict(
            sorted(diccionario.items(), key=lambda item: len(item[0]), reverse=True)
        )

        self._logger.info(f"Base plantilla calculada: {base_plantilla} (origen.uid={origen.uid})")
        self._logger.info(
            f"Delta matematico: destino.uid({destino.uid}) - base_plantilla({base_plantilla}) = {delta}"
        )

        # ------------------------------------------------------------------ #
        #  LOG DEL DICCIONARIO DE REEMPLAZOS GLOBALES
        #  VITAL para auditoria: imprime cada cadena que el modifier
        #  buscara y reemplazara en TODO el archivo (Fase 1) y cada
        #  cadena numerica a la que sumara el delta (Fase 2).
        #  Si ves aqui una cadena que no quieres reemplazar, ese es
        #  el momento de corregir el Excel o la logica de negocio.
        # ------------------------------------------------------------------ #
        self._logger.info("--- DICCIONARIO DE REEMPLAZOS GLOBALES ---")
        for k, v in reemplazos_ordenados.items():
            if not k.startswith("__"):
                self._logger.info(f"Reemplazar: '{k}' -> '{v}'")
        self._logger.info(
            f"Total entradas literales: {len([k for k in reemplazos_ordenados if not k.startswith('__')])}"
        )
        self._logger.info(
            f"Delta (suelo a `5\\d{{4}}` en whitelist): {delta:+d}"
        )
        self._logger.info("------------------------------------------")

        return reemplazos_ordenados

    def predecir_bloques_generados(
        self,
        ruta_plantilla: str,
        reemplazos: dict[str, str]
    ) -> list[BloquePLC]:
        """Predice los objetos BloquePLC que se generaran tras la mutacion."""
        self._logger.info(f"Prediciendo bloques a generar desde: {ruta_plantilla}")
        ruta_origen = Path(ruta_plantilla)
        bloques_predichos: list[BloquePLC] = []

        if not ruta_origen.exists():
            self._logger.error(f"Ruta de plantilla no encontrada: {ruta_plantilla}")
            return bloques_predichos

        for filepath in ruta_origen.rglob("*.xml"):
            delta = int(reemplazos.get("__delta__", "0"))
            nombre_origen = reemplazos.get("__nombre_origen__", "")
            nombre_destino = reemplazos.get("__nombre_destino__", "")

            ruta_relativa = str(filepath.relative_to(ruta_origen))
            self._logger.debug(f"[Carpeta] Ruta original: {ruta_relativa}")

            for old_str, new_str in reemplazos.items():
                if not old_str.startswith("__"):
                    ruta_relativa = ruta_relativa.replace(old_str, new_str)

            def reemplazar_num(m: re.Match) -> str:
                return str(int(m.group(0)) + delta)
            ruta_mutada = re.sub(r"5\d{4}", reemplazar_num, ruta_relativa)

            if nombre_origen and nombre_destino:
                ruta_mutada = re.sub(
                    re.escape(nombre_origen), nombre_destino,
                    ruta_mutada, flags=re.IGNORECASE
                )

            self._logger.debug(f"[Carpeta] Ruta mutada: {ruta_mutada}")

            nuevo_nombre_archivo = Path(ruta_mutada).stem
            nueva_ruta_carpetas = str(Path(ruta_mutada).parent)

            if "TABLA" in nueva_ruta_carpetas.upper() or "TAG" in nueva_ruta_carpetas.upper():
                tipo = "Tabla"
                match_num = re.search(r"(\d+)", nuevo_nombre_archivo)
                numero = int(match_num.group(1)) if match_num else 0
            else:
                match = re.match(r"^([A-Za-z]+)(\d+)", nuevo_nombre_archivo, re.IGNORECASE)
                tipo = match.group(1).upper() if match else "Desconocido"
                numero = int(match.group(2)) if match else 0

            self._logger.debug(f"[Prediccion] {tipo} -> {numero}")

            bloques_predichos.append(
                BloquePLC(
                    nombre=nuevo_nombre_archivo,
                    numero=numero,
                    tipo=tipo,
                    ruta=nueva_ruta_carpetas
                )
            )

        self._logger.info(f"Prediccion completada: {len(bloques_predichos)} bloques a generar.")
        return bloques_predichos

    def generar_archivos(
        self,
        ruta_plantilla: str,
        reemplazos: dict[str, str],
        build_dir: str | None = None,
    ) -> str:
        """
        Genera fisicamente los archivos XML mutados en el directorio destino.

        ESTRATEGIA QUIRURGICA (post-refactor):
          1. Calcula la RUTA mutada del archivo (nombre en disco) con
             str.replace + re.sub. Esto es SEGURO porque opera sobre el
             path del sistema de archivos, no sobre el contenido XML.
          2. Copia el XML original al destino SIN MODIFICAR.
          3. Parsea el XML destino con xml.etree.ElementTree.
          4. Recorre el arbol y reemplaza SOLO en las etiquetas de la
             whitelist (Name, Number, Comment, Title).
          5. Atributos prohibidos (ID, Handle, SystemId, RefId, Link,
             CompositionName) NO SE TOCAN, aunque contengan el numero.
          6. Guarda el XML preservando namespaces y encoding originales.
        """
        if build_dir is None:
            build_dir = config_manager.get_build_root()
        ruta_origen = Path(ruta_plantilla)
        directorio_salida = Path(build_dir)

        if directorio_salida.exists():
            shutil.rmtree(directorio_salida)
        directorio_salida.mkdir(parents=True, exist_ok=True)

        self._logger.info(f"Iniciando generacion fisica en: {directorio_salida.absolute()}")
        archivos_generados = 0

        for filepath in ruta_origen.rglob("*.xml"):
            delta = int(reemplazos.get("__delta__", "0"))
            nombre_origen = reemplazos.get("__nombre_origen__", "")
            nombre_destino = reemplazos.get("__nombre_destino__", "")

            # ------------------------------------------------------------------ #
            #  PASO 1: Calcular la RUTA mutada (nombre en disco, NO contenido).
            #  str.replace + re.sub son SEGUROS aqui: operan sobre el path
            #  del sistema de archivos, no sobre el contenido XML.
            # ------------------------------------------------------------------ #
            ruta_relativa = str(filepath.relative_to(ruta_origen))
            self._logger.debug(f"[Carpeta] Ruta original: {ruta_relativa}")

            for old_str, new_str in reemplazos.items():
                if not old_str.startswith("__"):
                    ruta_relativa = ruta_relativa.replace(old_str, new_str)

            def reemplazar_num_ruta(m: re.Match) -> str:
                return str(int(m.group(0)) + delta)
            ruta_mutada = re.sub(r"5\d{4}", reemplazar_num_ruta, ruta_relativa)

            if nombre_origen and nombre_destino:
                ruta_mutada = re.sub(
                    re.escape(nombre_origen), nombre_destino,
                    ruta_mutada, flags=re.IGNORECASE
                )

            self._logger.debug(f"[Carpeta] Ruta mutada: {ruta_mutada}")
            ruta_destino = directorio_salida / ruta_mutada
            ruta_destino.parent.mkdir(parents=True, exist_ok=True)

            # ------------------------------------------------------------------ #
            #  PASO 2: Copiar el XML original al destino.
            # ------------------------------------------------------------------ #
            try:
                shutil.copy(str(filepath), str(ruta_destino))
            except Exception as e:
                self._logger.error(f"Error copiando {filepath} -> {ruta_destino}: {e}")
                continue

            # ------------------------------------------------------------------ #
            #  PASO 3: Mutacion QUIRURGICA del contenido XML.
            #  Parsea el XML y modifica SOLO las etiquetas de la whitelist.
            #  NO toca <ID>, <Handle>, <SystemId>, <RefId>, <Link>, etc.
            # ------------------------------------------------------------------ #
            try:
                modifier = ProcesoXMLModifier(str(ruta_destino))
                modificado = modifier.modificar(reemplazos=reemplazos, delta=delta)
                modifier.save()
                if modificado:
                    self._logger.debug(
                        f"[Modif] {ruta_destino.name}: XML mutado quirurgicamente."
                    )
                else:
                    self._logger.debug(
                        f"[Modif] {ruta_destino.name}: sin cambios en el contenido."
                    )
                archivos_generados += 1
            except Exception as e:
                self._logger.error(
                    f"Error modificando XML {ruta_destino.name}: {e}",
                    exc_info=True
                )
                # Continuamos con el siguiente archivo (no abortamos todo)

        self._logger.info(f"Completado: {archivos_generados} archivos creados.")
        return str(directorio_salida.absolute())
