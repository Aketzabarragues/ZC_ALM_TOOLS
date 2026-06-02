"""
Infrastructure Layer - XML Generator
===================================
Generador de archivos XML a partir de plantillas.
Implementa el enfoque de "Diccionario Quirúrgico" basado en la lógica C# heredada.
"""

import logging
import re
import shutil
from pathlib import Path

from core.models import BloquePLC, Proceso

__all__ = ["XMLGenerator", "XMLGeneratorError"]


class XMLGeneratorError(Exception):
    """Base exception for XML Generator errors."""
    pass


class XMLGenerator:
    """
    Genera archivos XML mutados a partir de plantillas.

    Usage:
        generador = XMLGenerator()
        diccionario = generador.calcular_diccionario_reemplazos(ruta_plantilla, origen, destino)
        bloques = generador.predecir_bloques_generados(ruta_plantillas, diccionario)
        generador.generar_archivos(ruta_plantillas, diccionario)
    """

    def __init__(self) -> None:
        """Initialize the generator with a logger."""
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
        Construye un diccionario quirúrgico extrayendo nombres de bloques y variables
        exactas de la plantilla, basándose en la lógica heredada de C#.

        Args:
            ruta_plantilla: Ruta a la carpeta con plantillas XML.
            origen: Proceso PLANTILLA (origen).
            destino: Proceso A GENERAR (destino).

        Returns:
            Dict con pares old_str -> new_str ordenados de más largo a más corto.
        """
        self._logger.info(
            f"Calculando Diccionario Quirúrgico: {origen.nombre} ({origen.codigo}) -> "
            f"{destino.nombre} ({destino.codigo})"
        )

        diccionario: dict[str, str] = {}
        ruta_origen = Path(ruta_plantilla)

        if not ruta_origen.exists():
            self._logger.warning("Ruta de plantilla no existe para calcular diccionario.")
            return diccionario

        base_plantilla = 50000 + origen.uid
        delta = destino.uid - base_plantilla

        # 1. ENFOQUE BISTURÍ: Solo extraemos del XML, sin reemplazos genéricos
        # Los reemplazos vendrán exclusivamente de los nombres de archivo y variables

        # 2. Extraer Nombres de Bloques (FC, FB, DB)
        for filepath in ruta_origen.rglob("*.xml"):
            nombre_original = filepath.stem
            match = re.match(r"^(FC|FB|DB)(\d+)(.*)", nombre_original, re.IGNORECASE)
            if match:
                tipo = match.group(1).upper()
                numero_original = int(match.group(2))
                resto_nombre = match.group(3)

                # Aplicar delta solo a los de la serie 50000 (o la base de la plantilla)
                numero_nuevo = numero_original + delta if numero_original >= 50000 else numero_original
                nombre_proyectado = f"{tipo}{numero_nuevo}{resto_nombre}"

                # Reemplazar código de proceso en el nombre si existe (Ej: _CPR_ por _PINT_)
                nombre_proyectado = nombre_proyectado.replace(f"_{origen.codigo}_", f"_{destino.codigo}_")

                diccionario[nombre_original] = nombre_proyectado

        # 3. Extraer Variables de la Tabla de Tags
        rutas_tablas = [
            f for f in ruta_origen.rglob("*.xml")
            if "TABLA" in str(f.parent).upper() or "TAG" in str(f.parent).upper()
        ]
        for tabla_path in rutas_tablas:
            # 3A. Añadir el nombre del archivo de la tabla al diccionario
            nombre_tabla = tabla_path.stem

            match_tabla = re.match(r"(5\d{4})_(.*)", nombre_tabla)
            if match_tabla:
                num_tabla = int(match_tabla.group(1))
                cod_dest = destino.codigo.strip() if destino.codigo else ""
                nombre_tabla_mutado = f"{num_tabla + delta}_{cod_dest}"

                if nombre_tabla != nombre_tabla_mutado:
                    diccionario[nombre_tabla] = nombre_tabla_mutado

            try:
                xml_content = tabla_path.read_text(encoding="utf-8")
                # Regex basada en C#: Buscar <Name>([^<]+)</Name>
                nombres_variables = re.findall(r"<Name>([^<]+)</Name>", xml_content)

                for nombre_var in nombres_variables:
                    # a) Aplicamos el Delta
                    def reemplazar_num(m: re.Match) -> str:
                        return str(int(m.group(0)) + delta)
                    nombre_proyectado_var = re.sub(r"5\d{4}", reemplazar_num, nombre_var)

                    # b) Reemplazamos el código del proceso de forma robusta
                    cod_ori = origen.codigo.strip() if origen.codigo else ""
                    cod_dest = destino.codigo.strip() if destino.codigo else ""

                    if cod_ori and cod_dest:
                        # Busca _CODIGO seguido de un guion bajo o el final de la línea
                        patron = rf"_{cod_ori}(?=_|$)"
                        nombre_proyectado_var = re.sub(patron, f"_{cod_dest}", nombre_proyectado_var, flags=re.IGNORECASE)

                    if nombre_var != nombre_proyectado_var:
                        diccionario[nombre_var] = nombre_proyectado_var
            except Exception as e:
                self._logger.error(f"Error parseando tabla de variables {tabla_path.name}: {e}")

        # 4. Guardamos metadatos para las rutas (con prefijo __ para excluirlos del XML)
        diccionario["__delta__"] = str(delta)
        diccionario["__nombre_origen__"] = origen.nombre
        diccionario["__nombre_destino__"] = destino.nombre

        # 5. ORDENAMIENTO CRÍTICO: De cadenas más largas a más cortas
        reemplazos_ordenados = dict(
            sorted(diccionario.items(), key=lambda item: len(item[0]), reverse=True)
        )

        self._logger.info(f"Base plantilla calculada: {base_plantilla} (origen.uid={origen.uid})")
        self._logger.info(f"Delta matemático: destino.uid({destino.uid}) - base_plantilla({base_plantilla}) = {delta}")

        # DEBUG: Loggear TODO el diccionario quirúrgico
        self._logger.debug("=== CONTENIDO DEL DICCIONARIO QUIRÚRGICO ===")
        self._logger.debug(f"Total de entradas: {len(reemplazos_ordenados)}")
        for key, value in reemplazos_ordenados.items():
            if key == "__delta__":
                self._logger.debug(f"  [META] __delta__ = {value}")
            else:
                self._logger.debug(f"  REEMPLAZAR: '{key}' -> '{value}'")
        self._logger.debug("============================================")

        return reemplazos_ordenados

    def predecir_bloques_generados(
        self,
        ruta_plantilla: str,
        reemplazos: dict[str, str]
    ) -> list[BloquePLC]:
        """
        Predice los objetos BloquePLC que se generarán tras la mutación.

        Args:
            ruta_plantilla: Ruta a la carpeta con plantillas XML.
            reemplazos: Dict con pares old_str -> new_str para mutar nombres.

        Returns:
            Lista de BloquePLC predichos que se generarán.
        """
        self._logger.info(f"Prediciendo bloques a generar desde: {ruta_plantilla}")
        ruta_origen = Path(ruta_plantilla)
        bloques_predichos: list[BloquePLC] = []

        if not ruta_origen.exists():
            self._logger.error(f"Ruta de plantilla no encontrada: {ruta_plantilla}")
            return bloques_predichos

        for filepath in ruta_origen.rglob("*.xml"):
            # 1. Mutar la ruta (o 2. en el caso de predecir)
            delta = int(reemplazos.get("__delta__", "0"))
            nombre_origen = reemplazos.get("__nombre_origen__", "")
            nombre_destino = reemplazos.get("__nombre_destino__", "")

            ruta_relativa = str(filepath.relative_to(ruta_origen))
            self._logger.debug(f"[Carpeta] Ruta original: {ruta_relativa}")

            # A. Reemplazo de palabras exactas del diccionario
            for old_str, new_str in reemplazos.items():
                if not old_str.startswith("__"):
                    ruta_relativa = ruta_relativa.replace(old_str, new_str)

            # B. Reemplazo de números de carpeta (serie 50000)
            def reemplazar_num(m: re.Match) -> str:
                return str(int(m.group(0)) + delta)
            ruta_mutada = re.sub(r"5\d{4}", reemplazar_num, ruta_relativa)

            # C. Reemplazo del nombre del proceso en la carpeta (Ej: Compacto -> CIP_1)
            if nombre_origen and nombre_destino:
                ruta_mutada = re.sub(re.escape(nombre_origen), nombre_destino, ruta_mutada, flags=re.IGNORECASE)

            self._logger.debug(f"[Carpeta] Ruta mutada: {ruta_mutada}")

            # 3. Separar el nuevo nombre de archivo de su nueva ruta de carpetas
            nuevo_nombre_archivo = Path(ruta_mutada).stem
            nueva_ruta_carpetas = str(Path(ruta_mutada).parent)

            # 4. Extraer prefijo y número del NUEVO nombre de archivo
            if "TABLA" in nueva_ruta_carpetas.upper() or "TAG" in nueva_ruta_carpetas.upper():
                tipo = "Tabla"
                match_num = re.search(r"(\d+)", nuevo_nombre_archivo)
                numero = int(match_num.group(1)) if match_num else 0
            else:
                match = re.match(r"^([A-Za-z]+)(\d+)", nuevo_nombre_archivo, re.IGNORECASE)
                tipo = match.group(1).upper() if match else "Desconocido"
                numero = int(match.group(2)) if match else 0

            self._logger.debug(f"[Predicción] {tipo} -> {numero}")

            bloques_predichos.append(
                BloquePLC(
                    nombre=nuevo_nombre_archivo,
                    numero=numero,
                    tipo=tipo,
                    ruta=nueva_ruta_carpetas
                )
            )
            self._logger.debug(f"[Predicción] Mutado: {ruta_relativa} -> {ruta_mutada}")

        self._logger.info(f"Predicción completada: {len(bloques_predichos)} bloques a generar.")
        return bloques_predichos

    def generar_archivos(
        self,
        ruta_plantilla: str,
        reemplazos: dict[str, str],
        build_dir: str = ".build"
    ) -> str:
        """
        Genera físicamente los archivos XML mutados en el directorio destino.
        Aplica el diccionario de reemplazos tanto a las rutas como al contenido del XML.
        Maneja explícitamente el <Number> interno de cada bloque.

        Args:
            ruta_plantilla: Ruta a la carpeta con plantillas XML.
            reemplazos: Dict con pares old_str -> new_str para mutar nombres.
            build_dir: Directorio de salida (por defecto .build/).

        Returns:
            Ruta absoluta del directorio donde se generaron los archivos.
        """
        ruta_origen = Path(ruta_plantilla)
        directorio_salida = Path(build_dir)

        # Limpiamos el directorio de salida si ya existe de una ejecución anterior
        if directorio_salida.exists():
            shutil.rmtree(directorio_salida)
        directorio_salida.mkdir(parents=True, exist_ok=True)

        self._logger.info(f"Iniciando generación física en: {directorio_salida.absolute()}")
        archivos_generados = 0

        for filepath in ruta_origen.rglob("*.xml"):
            # 1. Mutar la ruta
            delta = int(reemplazos.get("__delta__", "0"))
            nombre_origen = reemplazos.get("__nombre_origen__", "")
            nombre_destino = reemplazos.get("__nombre_destino__", "")

            ruta_relativa = str(filepath.relative_to(ruta_origen))
            self._logger.debug(f"[Carpeta] Ruta original: {ruta_relativa}")

            # A. Reemplazo de palabras exactas del diccionario
            for old_str, new_str in reemplazos.items():
                if not old_str.startswith("__"):
                    ruta_relativa = ruta_relativa.replace(old_str, new_str)

            # B. Reemplazo de números de carpeta (serie 50000)
            def reemplazar_num_ruta(m: re.Match) -> str:
                return str(int(m.group(0)) + delta)
            ruta_mutada = re.sub(r"5\d{4}", reemplazar_num_ruta, ruta_relativa)

            # C. Reemplazo del nombre del proceso en la carpeta (Ej: Compacto -> CIP_1)
            if nombre_origen and nombre_destino:
                ruta_mutada = re.sub(re.escape(nombre_origen), nombre_destino, ruta_mutada, flags=re.IGNORECASE)

            self._logger.debug(f"[Carpeta] Ruta mutada: {ruta_mutada}")
            ruta_destino = directorio_salida / ruta_mutada
            ruta_destino.parent.mkdir(parents=True, exist_ok=True)

            # 2. Leer contenido original
            try:
                contenido = filepath.read_text(encoding="utf-8")
            except Exception as e:
                self._logger.error(f"Error leyendo {filepath}: {e}")
                continue

            # 3A. Reemplazo del número interno estructural del bloque (<Number>)
            match_nombre = re.match(r"^(FC|FB|DB)(\d+)", filepath.stem, re.IGNORECASE)
            if match_nombre:
                numero_original_str = match_nombre.group(2)
                numero_original = int(numero_original_str)
                if numero_original >= 50000:
                    nuevo_nombre_archivo = reemplazos.get(filepath.stem, filepath.stem)
                    match_nuevo = re.match(r"^(FC|FB|DB)(\d+)", nuevo_nombre_archivo, re.IGNORECASE)
                    if match_nuevo:
                        numero_nuevo_str = match_nuevo.group(2)
                        contenido = contenido.replace(
                            f"<Number>{numero_original_str}</Number>",
                            f"<Number>{numero_nuevo_str}</Number>"
                        )

            # 3B. Mutar el contenido interno con el diccionario quirúrgico
            for old_str, new_str in reemplazos.items():
                if not old_str.startswith("__"):
                    contenido = contenido.replace(old_str, new_str)

            # 4. Escribir el nuevo archivo
            try:
                ruta_destino.write_text(contenido, encoding="utf-8")
                archivos_generados += 1
            except Exception as e:
                self._logger.error(f"Error escribiendo {ruta_destino}: {e}")

        self._logger.info(f"Completado: {archivos_generados} archivos creados.")
        return str(directorio_salida.absolute())