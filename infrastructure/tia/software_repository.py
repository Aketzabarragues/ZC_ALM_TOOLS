"""
Infrastructure Layer - Software Repository
============================================
Logica de SOFTWARE sobre TIA Portal: bloques, tags, compilación, constantes.

Recibe un Gateway (para resolver Project/Plc) y delega TODO el acceso COM.
Implementa el contrato ISoftwareRepository (ver core/ports.py).
"""

import logging
from contextlib import AbstractContextManager
from pathlib import Path
from typing import Any

from core.models import BloquePLC
from infrastructure.tia.gateway import TIAPortalGateway, TIAServiceError
from infrastructure.tia.importer import TIAImporter
from infrastructure.tia.scanner import TIAScanner
from infrastructure.tia_runtime_loader import load_siemens_tia

# Cargamos el wrapper una sola vez al importar el modulo.
# 'ts.Enums.ExportOptions.WithDefaults' es la sintaxis confirmada en
# el manual oficial de TIA Scripting Python (seccion 2.28.3).
_ts: Any = load_siemens_tia()

__all__ = ["SoftwareRepository"]


class SoftwareRepository:
    """
    Repositorio de software: agrupa todas las operaciones de alto nivel
    sobre bloques/PLCs/tags de TIA Portal.

    NO maneja el ciclo de vida COM: eso es responsabilidad del Gateway.
    """

    def __init__(
        self,
        gateway: TIAPortalGateway,
        scanner: TIAScanner,
        importer: TIAImporter,
    ) -> None:
        self._gateway = gateway
        self._scanner = scanner
        self._importer = importer
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )

    # ------------------------------------------------------------------ #
    #  Cache de bloques
    # ------------------------------------------------------------------ #

    def get_existing_blocks(self, plc_name: str) -> dict[str, BloquePLC]:
        return self._scanner.get_cached_blocks()

    def build_cache(self, plc_name: str, force: bool = False) -> dict[str, BloquePLC]:
        plc = self._gateway.resolve_plc(plc_name)
        if not plc:
            raise TIAServiceError(f"No se pudo acceder al PLC: {plc_name}")
        return self._scanner.build_cache(plc_name, plc, force)

    def clear_cache(self) -> None:
        self._scanner.clear_cache()

    def force_rescan(self, plc_name: str) -> dict[str, BloquePLC]:
        return self.build_cache(plc_name, force=True)

    # ------------------------------------------------------------------ #
    #  Consistencia y compilación
    # ------------------------------------------------------------------ #

    def is_bloque_consistente(self, plc_name: str, block_name: str) -> bool:
        """
        Verifica si un bloque esta compilado y consistente (con Compilacion
        Defensiva: si no, intenta compilar al vuelo antes de retornar False).
        """
        try:
            bloque_dto = self._scanner.find_block_case_insensitive(block_name)
            if not bloque_dto:
                return False

            plc = self._gateway.resolve_plc(plc_name)
            if not plc:
                return False

            program_blocks = plc.get_program_blocks()
            com_block = self._importer.find_block_in_group(program_blocks, bloque_dto.nombre)

            if not com_block:
                return False

            return self._importer.asegurar_consistencia(com_block)
        except Exception as e:
            self._logger.warning(
                f"No se pudo verificar consistencia de '{block_name}' "
                f"(bloque protegido o inaccesible): {e}"
            )
            return True  # Por seguridad, asumimos TRUE

    def compilar_software(self, plc_name: str) -> bool:
        """
        Compila el software del PLC.
        Retorna True si la compilación fue EXITOSA (0 errores).
        Nota: compile_software() retorna True si hay errores, False si OK.
        """
        plc = self._gateway.resolve_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False

        self._logger.info(f"⏳ Iniciando compilación de software para '{plc_name}'...")
        try:
            has_errors: bool = plc.compile_software()
            if has_errors:
                self._logger.error(f"❌ Fallo de compilación en el PLC '{plc_name}'. Revisa TIA Portal.")
                return False
            self._logger.info(f"✅ Compilación exitosa para '{plc_name}'.")
            return True
        except Exception as e:
            self._logger.error(f"Fallo al invocar la compilación: {e}")
            return False

    # ------------------------------------------------------------------ #
    #  Import / Export de bloques
    # ------------------------------------------------------------------ #

    def importar_bloques_generados(
        self,
        plc_name: str,
        ruta_build: str,
        proceso_nombre: str = "desconocido",
    ) -> bool:
        self._logger.info(f"Delegando importación al TIAImporter para PLC '{plc_name}'...")
        plc = self._gateway.resolve_plc(plc_name)
        if not plc:
            raise TIAServiceError(f"No se pudo acceder al PLC: {plc_name}")

        project = self._gateway.resolve_project()
        # El TIAImporter ya NO abre transaccion propia. El control
        # centralizado vive en el Gateway (reentrante). Asi, si este
        # metodo se invoca dentro de una transaccion global del UC,
        # el flag reentrante cede el control sin anidar.
        with self.transaccion(f"Importando proceso '{proceso_nombre}' en PLC '{plc_name}'"):
            return self._importer.importar_proyecto(
                project, plc, ruta_build, proceso_nombre
            )

    def exportar_bloque(self, plc_name: str, block_name: str, target_dir: str) -> bool:
        plc = self._gateway.resolve_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False
        return self._importer.exportar_bloque(plc, block_name, target_dir)

    def importar_bloque_single(
        self,
        plc_name: str,
        xml_file_path: str,
        target_relative_path: str = "",
    ) -> bool:
        project = self._gateway.resolve_project()
        if not project:
            self._logger.error("No hay proyecto abierto para importar bloque.")
            return False
        plc = self._gateway.resolve_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False
        # Gestion de transaccion centralizada en el Gateway.
        with self.transaccion(
            f"Importando bloque '{Path(xml_file_path).name}' en PLC '{plc_name}'"
        ):
            return self._importer.import_single_block(
                project, plc, xml_file_path, target_relative_path
            )

    def importar_bloque_override(
        self,
        plc_name: str,
        xml_path: str,
        target_folder: str | None = None,
    ) -> bool:
        project = self._gateway.resolve_project()
        if not project:
            self._logger.error("No hay proyecto abierto para importar bloque.")
            return False
        plc = self._gateway.resolve_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False
        # Gestion de transaccion centralizada en el Gateway.
        with self.transaccion(
            f"Override-importando bloque '{Path(xml_path).name}' en PLC '{plc_name}'"
        ):
            return self._importer.importar_bloque_override(
                project, plc, xml_path, target_folder
            )

    # ------------------------------------------------------------------ #
    #  Compilacion / importacion a nivel de bloque (Fase 4 DB)
    # ------------------------------------------------------------------ #

    def compilar_bloque(self, plc_name: str, block_name: str) -> bool:
        """
        Compila un bloque de programa especifico con Compilacion Defensiva.
        Retorna True si el bloque esta consistente (o si se logro compilar).
        """
        bloque_dto = self._scanner.find_block_case_insensitive(block_name)
        if bloque_dto is None:
            self._logger.warning(
                f"Bloque '{block_name}' no encontrado en PLC '{plc_name}' para compilar."
            )
            return False

        plc = self._gateway.resolve_plc(plc_name)
        if plc is None:
            self._logger.error(f"PLC '{plc_name}' no encontrado para compilar bloque.")
            return False

        try:
            program_blocks = plc.get_program_blocks()
            com_block = self._importer.find_block_in_group(
                program_blocks, bloque_dto.nombre
            )
            if com_block is None:
                self._logger.warning(
                    f"No se pudo localizar el objeto COM del bloque '{block_name}'."
                )
                return False
            return self._importer.asegurar_consistencia(com_block)
        except Exception as e:
            self._logger.error(f"Fallo compilando bloque '{block_name}': {e}")
            return False

    def importar_bloque(
        self, plc_name: str, xml_path: str, folder_path: str
    ) -> None:
        """
        Importa un bloque desde un XML. Equivalente a importar_bloque_single
        pero con la firma del port (sin retorno, folder_path obligatorio).

        folder_path="" significa root.
        """
        project = self._gateway.resolve_project()
        if project is None:
            self._logger.error("No hay proyecto abierto para importar bloque.")
            return
        plc = self._gateway.resolve_plc(plc_name)
        if plc is None:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return
        if not Path(xml_path).exists():
            self._logger.error(f"Archivo XML no encontrado: {xml_path}")
            return
        # Gestion de transaccion centralizada en el Gateway.
        with self.transaccion(
            f"Importando bloque '{Path(xml_path).name}' en PLC '{plc_name}'"
        ):
            self._importer.import_single_block(
                project, plc, xml_path, folder_path or ""
            )

    def sanitize_import_path(self, full_path: str) -> str:
        return self._importer.sanitize_import_path(full_path)

    # ------------------------------------------------------------------ #
    #  Transacciones COM (reentrante via Gateway)
    # ------------------------------------------------------------------ #

    def transaccion(self, undo_text: str) -> AbstractContextManager[None]:
        """
        Delega en el context manager del Gateway. Es reentrante:
        si ya hay una transacción abierta en el Gateway, cede el
        control sin abrir una nueva (TIA no soporta anidamiento).
        """
        return self._gateway.transaccion(undo_text)

    # ------------------------------------------------------------------ #
    #  Búsqueda de bloques
    # ------------------------------------------------------------------ #

    def bloque_existe(self, plc_name: str, block_name: str) -> bool:
        try:
            return self._scanner.find_block_case_insensitive(block_name) is not None
        except Exception as e:
            self._logger.error(f"Error comprobando existencia del bloque {block_name}: {e}")
            return False

    def obtener_ruta_bloque(self, plc_name: str, block_name: str) -> str | None:
        try:
            bloque_dto = self._scanner.find_block_case_insensitive(block_name)
            if bloque_dto:
                return bloque_dto.ruta
            return None
        except Exception as e:
            self._logger.error(f"Error obteniendo ruta del bloque {block_name}: {e}")
            return None

    # ------------------------------------------------------------------ #
    #  Constantes de proceso (N_MAX)
    # ------------------------------------------------------------------ #

    def actualizar_constantes_proceso(
        self,
        plc_name: str,
        nombre_tabla: str,
        constantes_dict: dict[str, Any],
    ) -> bool:
        """
        Modifica en vivo (RAM) los valores de las constantes de usuario en
        una tabla ubicada en la carpeta '003_Proceso' de las variables del PLC.
        """
        plc = self._gateway.resolve_plc(plc_name)
        if not plc:
            return False

        try:
            self._logger.info(f"Buscando tabla de variables '{nombre_tabla}' en '003_Proceso'...")
            tablas = plc.get_plc_tag_tables(folder_path="003_Proceso")

            tabla = next(
                (t for t in tablas if t.get_property(name="Name") == nombre_tabla),
                None,
            )
            if not tabla:
                self._logger.error(f"Tabla '{nombre_tabla}' no encontrada en '003_Proceso'.")
                return False

            user_constants = tabla.get_user_constants()

            cambios = 0
            for constante in user_constants:
                nombre_const = constante.get_property(name="Name")
                if nombre_const in constantes_dict:
                    nuevo_valor = str(constantes_dict[nombre_const])
                    constante.set_property(name="Value", value=nuevo_valor)
                    self._logger.debug(f"Constante {nombre_const} actualizada a {nuevo_valor}")
                    cambios += 1

            if cambios > 0:
                self._logger.info(f"✅ {cambios} constantes actualizadas en '{nombre_tabla}'")
                return True
            self._logger.warning(
                f"No se modificó ninguna constante. Verifica los nombres en el diccionario."
            )
            return False

        except Exception as e:
            self._logger.error(f"Fallo actualizando constantes en {nombre_tabla}: {e}")
            return False

    # ------------------------------------------------------------------ #
    #  User Constants: lectura y mutacion via COM (motor hibrido Fase 1)
    # ------------------------------------------------------------------ #

    def _find_tag_table(self, plc_name: str, table_name: str) -> Any:
        """
        Busca una PlcTagTable consultando la caché del TIAScanner.

        La caché se puebla en build_cache() (en el arranque) con TODAS
        las tablas del PLC, así que esta búsqueda es O(1) y no toca
        el COM wrapper directamente. La firma conserva `plc_name` por
        simetría con el resto de metodos del repo, aunque el scanner
        ya tiene la instancia resuelta.
        """
        table = self._scanner.find_tag_table_case_insensitive(table_name)
        if table is None:
            self._logger.warning(
                f"Tabla '{table_name}' no encontrada en la caché del scanner "
                f"(¿se ha ejecutado build_cache sobre '{plc_name}'?)."
            )
        return table

    def _find_user_constant(self, table: Any, constant_name: str) -> Any | None:
        """
        Busca una UserConstant por nombre iterando la lista nativa
        del wrapper (`table.get_user_constants()`). Manual sec 2.34:
        cada UserConstant expone `get_name()` o `.Name`.
        """
        try:
            for c in table.get_user_constants():
                name = (
                    c.get_property(name="Name")
                    if hasattr(c, "get_property")
                    else getattr(c, "Name", None)
                )
                if name == constant_name:
                    return c
        except Exception as e:
            self._logger.error(f"Error iterando UserConstants buscando '{constant_name}': {e}")
        return None

    def get_user_constants(
        self, plc_name: str, table_name: str
    ) -> dict[int, str]:
        """
        Devuelve {Value: Name} de las constantes de usuario de una tabla.

        NOTA: NO inicia transaccion ni ExclusiveAccess. El use case
        superior (o el caller) decide si necesita bloquear el PLC.
        """
        table = self._find_tag_table(plc_name, table_name)
        if table is None:
            self._logger.warning(
                f"Tabla '{table_name}' no encontrada en PLC '{plc_name}'."
            )
            return {}

        resultado: dict[int, str] = {}
        try:
            for constant in table.get_user_constants():  # manual sec 2.28.5
                raw_value = (
                    constant.get_property(name="Value")
                    if hasattr(constant, "get_property")
                    else getattr(constant, "Value", None)
                )
                try:
                    int_value = int(str(raw_value).strip())
                except (TypeError, ValueError):
                    self._logger.warning(
                        f"Constante con Value no numerico: {raw_value!r} (se omite)"
                    )
                    continue
                name = (
                    constant.get_property(name="Name")
                    if hasattr(constant, "get_property")
                    else getattr(constant, "Name", "")
                )
                resultado[int_value] = str(name)
        except Exception as e:
            self._logger.error(f"Error iterando UserConstants de '{table_name}': {e}")

        return resultado

    def update_user_constant_name(
        self, plc_name: str, table_name: str, current_name: str, new_name: str
    ) -> None:
        """Renombra una constante de usuario en vivo. No-op si no existe."""
        table = self._find_tag_table(plc_name, table_name)
        if table is None:
            self._logger.warning(
                f"Tabla '{table_name}' no encontrada en PLC '{plc_name}'."
            )
            return

        constant = self._find_user_constant(table, current_name)
        if constant is None:
            self._logger.warning(
                f"Constante '{current_name}' no encontrada en tabla '{table_name}'."
            )
            return

        try:
            if hasattr(constant, "set_property"):
                constant.set_property(name="Name", value=new_name)
            else:
                constant.Name = new_name
            self._logger.info(
                f"Constante renombrada: '{current_name}' -> '{new_name}' en '{table_name}'."
            )
        except Exception as e:
            self._logger.error(
                f"Fallo renombrando constante '{current_name}' a '{new_name}': {e}"
            )

    def delete_user_constant(
        self, plc_name: str, table_name: str, name: str
    ) -> None:
        """Borra una constante de usuario en vivo. No-op si no existe."""
        table = self._find_tag_table(plc_name, table_name)
        if table is None:
            self._logger.warning(
                f"Tabla '{table_name}' no encontrada en PLC '{plc_name}'."
            )
            return

        constant = self._find_user_constant(table, name)
        if constant is None:
            self._logger.warning(
                f"Constante '{name}' no encontrada en tabla '{table_name}'."
            )
            return

        try:
            # Manual sec 2.34.4: UserConstant.delete() (snake_case).
            constant.delete()
            self._logger.info(
                f"Constante '{name}' borrada de tabla '{table_name}'."
            )
        except Exception as e:
            self._logger.error(f"Fallo borrando constante '{name}': {e}")

    def update_user_constant_value(
        self,
        plc_name: str,
        table_name: str,
        constant_name: str,
        new_value: int,
    ) -> None:
        """
        Actualiza el valor de una constante de usuario en vivo.
        El API COM de Siemens espera un string numerico para Value,
        por eso se serializa con str().
        """
        table = self._find_tag_table(plc_name, table_name)
        if table is None:
            self._logger.warning(
                f"Tabla '{table_name}' no encontrada en PLC '{plc_name}'."
            )
            return

        constant = self._find_user_constant(table, constant_name)
        if constant is None:
            self._logger.warning(
                f"Constante '{constant_name}' no encontrada en tabla '{table_name}'."
            )
            return

        try:
            if hasattr(constant, "set_property"):
                constant.set_property(name="Value", value=str(new_value))
            else:
                constant.Value = str(new_value)
            self._logger.info(
                f"Constante '{constant_name}' actualizada a valor={new_value} "
                f"en tabla '{table_name}'."
            )
        except Exception as e:
            self._logger.error(
                f"Fallo actualizando valor de '{constant_name}' a {new_value}: {e}"
            )

    def exportar_tabla_variables(
        self, plc_name: str, table_name: str, export_dir: str
    ) -> str:
        """
        Exporta una tabla de variables PLC a un archivo .xml.
        Retorna la ruta absoluta del XML generado.

        Sintaxis confirmada en el manual (sec 2.28.3):
            PlcTagTable.export(target_directory_path, export_options)
        donde export_options es el ENUM `Enums.ExportOptions.WithDefaults`.
        """
        table = self._find_tag_table(plc_name, table_name)
        if table is None:
            self._logger.warning(
                f"Tabla '{table_name}' no encontrada en PLC '{plc_name}'."
            )
            return ""

        export_dir_path = Path(export_dir)
        export_dir_path.mkdir(parents=True, exist_ok=True)

        try:
            # getattr defensivo: si WithDefaults no existe, caemos al int 1
            # (que es el ordinal del enum en la mayoria de versiones).
            export_opts = getattr(
                _ts.Enums.ExportOptions, "WithDefaults", 1
            )
        except AttributeError:
            self._logger.error(
                "No se encontró Enums.ExportOptions.WithDefaults en el wrapper."
            )
            return ""

        try:
            # 1. Pasamos el DIRECTORIO, como debe ser.
            # TIA Portal anida el XML replicando la estructura de
            # grupos del PLC (ej. hardware/2000_Dispositivos/2000_Disp_ED.xml).
            table.export(
                target_directory_path=str(export_dir_path.absolute()),
                export_options=export_opts,
            )
        except Exception as e:
            self._logger.error(
                f"Excepción exportando tabla '{table_name}': {e}"
            )
            return ""

        # 2. Búsqueda RECURSIVA (rglob) para encontrar el XML
        # anidado en subcarpetas.
        xml_files = list(export_dir_path.rglob("*.xml"))
        if xml_files:
            # Tomamos el modificado más recientemente por si hay varios
            xml_files.sort(key=lambda f: f.stat().st_mtime, reverse=True)
            generado = xml_files[0]
            self._logger.debug(
                f"XML encontrado tras exportación: {generado}"
            )
            self._logger.info(
                f"Tabla '{table_name}' exportada a {generado}."
            )
            return str(generado.absolute())

        self._logger.error(
            f"TIA reportó éxito, pero no se encontró ningún XML "
            f"(ni en subcarpetas) en {export_dir_path}"
        )
        return ""

    def importar_tabla_variables(
        self, plc_name: str, xml_path: str, folder_path: str
    ) -> None:
        """
        Importa una tabla de variables PLC desde XML.

        Sintaxis confirmada en el manual (sec 2.2.24):
            Plc.import_plc_tags(
                import_root_directory,
                target_folder_path=None
            )
        Si folder_path esta vacio, el wrapper importa en el root.
        """
        plc = self._gateway.resolve_plc(plc_name)
        if plc is None:
            self._logger.warning(
                f"PLC '{plc_name}' no encontrado para importar tabla."
            )
            return

        xml_file_path = Path(xml_path)
        if not xml_file_path.exists():
            self._logger.error(
                f"Archivo XML no encontrado: {xml_path}"
            )
            return

        # El wrapper CreateTagGroupAndImportFiles espera recibir el
        # DIRECTORIO PADRE que contiene el XML (no el XML en sí).
        # Ej: si el XML está en .build/hardware/2000_Dispositivos/2000_Disp_ED.xml,
        # pasamos .build/hardware/2000_Dispositivos/.
        directory_path = str(xml_file_path.parent.absolute())

        try:
            target = folder_path if folder_path else None
            plc.import_plc_tags(
                import_root_directory=directory_path,
                target_folder_path=target,
            )
            self._logger.info(
                f"Tabla importada desde directorio {directory_path} "
                f"(folder_path='{folder_path or '<root>'}')."
            )
        except Exception as e:
            self._logger.error(
                f"Fallo importando tabla desde {directory_path} en "
                f"'{folder_path}': {e}"
            )
