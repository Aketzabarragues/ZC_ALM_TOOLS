"""
Infrastructure Layer - TIA Portal Importer
==========================================
Especialista encargado de importar archivos XML a TIA Portal
utilizando las funciones nativas masivas del wrapper.
"""

import logging
import shutil
from pathlib import Path
from typing import Any

from infrastructure import config_manager


class TIAImporterError(Exception):
    """Base exception for TIA Importer errors."""
    pass


class TIAImporter:
    """
    Importador especializado para injectar bloques XML en TIA Portal
    usando las funciones nativas masivas del wrapper de Siemens.
    """

    def __init__(
        self,
        export_with_defaults_enum: Any = None,
        import_override_enum: Any = None
    ) -> None:
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )
        self._export_with_defaults = export_with_defaults_enum
        self._import_override = import_override_enum

    @property
    def export_opts(self) -> Any:
        """
        Expone el enum de opciones de exportacion al SoftwareRepository,
        evitando que el repo tenga que recargar el wrapper de TIA Portal
        (Clean Architecture: el wrapper se carga UNA sola vez en el
        Composition Root / TIAPortalGateway).
        """
        return self._export_with_defaults

    def asegurar_consistencia(self, objeto: Any) -> bool:
        """
        Garantiza que un objeto TIA Portal este compilado antes de manipularlo.
        """
        if not hasattr(objeto, "is_consistent"):
            return True

        obj_name: str = "<sin_nombre>"
        try:
            if hasattr(objeto, "Name"):
                obj_name = str(objeto.Name)
            elif hasattr(objeto, "get_name"):
                obj_name = str(objeto.get_name())
        except Exception:
            obj_name = "<inaccesible>"

        try:
            if bool(objeto.is_consistent()):
                return True
        except Exception as e:
            self._logger.warning(
                f"No se pudo verificar is_consistent() de '{obj_name}': {e}. "
                f"Tratando como no consistente."
            )

        if hasattr(objeto, "compile"):
            self._logger.debug(f"Compilando objeto '{obj_name}' al vuelo...")
            try:
                compile_result = objeto.compile()
                if compile_result is True:
                    self._logger.warning(
                        f"compile() de '{obj_name}' retorno True (=errores de compilacion)."
                    )
            except Exception as e:
                self._logger.warning(f"compile() de '{obj_name}' lanzo excepcion: {e}")
                return False

        try:
            if bool(objeto.is_consistent()):
                self._logger.info(f"Objeto '{obj_name}' compilado con exito al vuelo.")
                return True
        except Exception as e:
            self._logger.warning(
                f"No se pudo re-verificar is_consistent() de '{obj_name}': {e}"
            )

        self._logger.warning(
            f"El objeto '{obj_name}' tiene errores de codigo que impiden su compilacion. "
            f"Se omitira su procesamiento para evitar un crash de Openness."
        )
        return False

    def sanitize_import_path(self, full_path: str) -> str:
        """Sana la ruta absoluta para extraer solo la parte relativa al grupo de usuario."""
        if not full_path:
            return ""

        partes = full_path.replace("\\", "/").split("/")

        system_folders = [
            "Program blocks",
            "Bloques de programa",
            "PLC tags",
            "Etiquetas PLC",
            "System blocks",
            "Bloques de sistema"
        ]

        for i, parte in enumerate(partes):
            if parte in system_folders:
                sub_ruta = "/".join(partes[i+1:])
                self._logger.debug(f"sanitize_import_path: '{full_path}' -> '{sub_ruta}'")
                return sub_ruta

        return "/".join(partes)

    def import_single_block(
        self,
        project_object: Any,
        plc_object: Any,
        xml_file_path: str,
        target_relative_path: str = ""
    ) -> bool:
        """
        Importa un único bloque XML usando carpeta staging efímera.

        IMPORTANTE: NO se capturan excepciones. Cualquier fallo (incluidas
        las excepciones COM de `plc_object.import_blocks(...)`) se propaga
        al caller. Esto es CRITICO para que el `with self._repo.transaccion(...)`
        del SoftwareRepository detecte el fallo, aborte la transacción y
        haga ROLLBACK ANTES de que el Gateway intente el COMMIT.

        Antes este método tenia un `try/except Exception as e: return False`
        que silenciaba las COM exceptions. Eso envenenaba la transacción:
        el `with` pensaba que todo OK, hacia COMMIT, y TIA Portal rechazaba
        el commit con `OpennessAccessException: Commit of a Transaction is
        not allowed after an exception is thrown due to potential project
        data corruption`.

        Solo se valida la existencia del archivo XML localmente con un
        TIAImporterError (no es una excepción COM, es una precondición de
        programación).
        """
        staging_dir = (Path(config_manager.get_build_root()) / "import_stage").absolute()

        try:
            xml_file = Path(xml_file_path)
            if not xml_file.exists():
                raise TIAImporterError(
                    f"Archivo XML no encontrado: {xml_file_path}"
                )

            if staging_dir.exists():
                shutil.rmtree(staging_dir, ignore_errors=True)
            staging_dir.mkdir(parents=True, exist_ok=True)

            shutil.copy(str(xml_file), str(staging_dir / xml_file.name))

            # NOTA: NO abrimos transaccion aqui. El control de transacciones
            # es responsabilidad EXCLUSIVA del TIAPortalGateway (gestor
            # reentrante). Abrir start_transaction() localmente provocaria
            # "Multiple instances of ExclusiveAccess is not supported"
            # si el caller ya tiene una transaccion global abierta.

            kwargs: dict[str, Any] = {"import_root_directory": str(staging_dir.absolute())}
            if target_relative_path:
                kwargs["target_folder_path"] = target_relative_path

            self._logger.debug(f"Llamando import_blocks con kwargs: {kwargs}")
            plc_object.import_blocks(**kwargs)  # <-- Excepciones COM se propagan

            self._logger.info(
                f"Bloque importado: {xml_file.name} -> {target_relative_path or 'raiz'}"
            )
            return True

        finally:
            if staging_dir.exists():
                shutil.rmtree(staging_dir, ignore_errors=True)

    def importar_proyecto(
        self,
        project_object: Any,
        plc_object: Any,
        ruta_build_str: str,
        proceso_nombre: str = "desconocido"
    ) -> bool:
        """
        Orquesta la importación completa usando las funciones de directorio nativas.

        IMPORTANTE: NO se capturan excepciones. Las COM exceptions de
        `plc_object.import_plc_tags(...)` y `plc_object.import_blocks(...)`
        se propagan al caller para que el `with self._repo.transaccion(...)`
        detecte el fallo y haga ROLLBACK antes del COMMIT.
        """
        ruta_build = Path(ruta_build_str)
        self._logger.info("Iniciando secuencia de importación nativa...")

        # NOTA: NO abrimos transaccion aqui. El control de transacciones
        # es responsabilidad EXCLUSIVA del TIAPortalGateway (gestor
        # reentrante). El caller (SoftwareRepository.importar_bloques_generados)
        # ya envuelve esta llamada con self.transaccion(...).

        for ruta_tabla in ruta_build.iterdir():
            if ruta_tabla.is_dir() and ("TABLA" in ruta_tabla.name.upper() or "TAG" in ruta_tabla.name.upper()):
                plc_object.import_plc_tags(import_root_directory=str(ruta_tabla.absolute()))

        for ruta_bloque in ruta_build.iterdir():
            if ruta_bloque.is_dir() and "BLOQUE" in ruta_bloque.name.upper():
                plc_object.import_blocks(import_root_directory=str(ruta_bloque.absolute()))

        self._logger.info("✅ Importación completada.")
        return True

    def exportar_bloque(self, plc_object: Any, block_name: str, target_dir: str) -> bool:
        """Exporta un bloque PLC a un directorio de forma plana (con compilación defensiva)."""
        try:
            program_blocks = plc_object.get_program_blocks()
            bloque = self.find_block_in_group(program_blocks, block_name)

            if not bloque:
                self._logger.error(f"Bloque '{block_name}' no encontrado.")
                return False

            if not self.asegurar_consistencia(bloque):
                self._logger.error(
                    f"Saltando exportacion de '{block_name}': no se logro compilar."
                )
                return False

            destino = Path(target_dir).absolute()
            destino.mkdir(parents=True, exist_ok=True)

            bloque.export(
                target_directory_path=str(destino),
                export_options=self._export_with_defaults,
                keep_folder_structure=False
            )

            self._logger.info(f"Bloque '{block_name}' exportado a: {target_dir}")
            return True

        except Exception as e:
            self._logger.error(f"Error exportando bloque {block_name}: {e}", exc_info=True)
            return False

    def find_block_in_group(self, group_or_blocks: Any, block_name: str) -> Any | None:
        """Busca un bloque por nombre en un grupo de bloques."""
        items: list[Any] = []

        if hasattr(group_or_blocks, 'get_blocks'):
            items = group_or_blocks.get_blocks()
        elif hasattr(group_or_blocks, 'Blocks'):
            items = group_or_blocks.Blocks
        elif hasattr(group_or_blocks, '__iter__'):
            items = list(group_or_blocks)

        for item in items:
            try:
                if hasattr(item, 'get_name') and item.get_name() == block_name:
                    return item
                elif hasattr(item, 'Name') and item.Name == block_name:
                    return item
            except Exception:
                pass

        groups: list[Any] = []
        if hasattr(group_or_blocks, 'get_groups'):
            groups = group_or_blocks.get_groups()
        elif hasattr(group_or_blocks, 'Groups'):
            groups = group_or_blocks.Groups

        for sub_group in groups:
            resultado = self.find_block_in_group(sub_group, block_name)
            if resultado:
                return resultado

        return None

    def importar_bloque_override(
        self,
        project_object: Any,
        plc_object: Any,
        xml_path: str,
        target_folder_path: str | None = None
    ) -> bool:
        """Método legacy - usar import_single_block para importaciones por bloque."""
        return self.import_single_block(project_object, plc_object, xml_path, target_folder_path or "")
