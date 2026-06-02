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
        """Initialize the importer with a logger."""
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )
        self._export_with_defaults = export_with_defaults_enum
        self._import_override = import_override_enum

    def sanitize_import_path(self, full_path: str) -> str:
        """
        Sana la ruta absoluta para extraer solo la parte relativa al grupo de usuario.
        Detecta dinámicamente si hay carpetas de sistema y las elimina.
        
        Args:
            full_path: Ruta absoluta o relativa del bloque 
                      (ej: "PLC_1/Program blocks/110_Expedicion/3110_Parametros")
            
        Returns:
            Ruta relativa para import_blocks (ej: "110_Expedicion/3110_Parametros")
            o cadena vacía si no hay subcarpetas.
        """
        if not full_path:
            return ""
        
        partes = full_path.replace("\\", "/").split("/")
        
        # Carpetas de sistema conocidas
        system_folders = [
            "Program blocks", 
            "Bloques de programa",
            "PLC tags",
            "Etiquetas PLC",
            "System blocks",
            "Bloques de sistema"
        ]
        
        # Buscar la carpeta de sistema y devolver todo lo que viene después
        for i, parte in enumerate(partes):
            if parte in system_folders:
                sub_ruta = "/".join(partes[i+1:])
                self._logger.debug(f"sanitize_import_path: '{full_path}' -> '{sub_ruta}'")
                return sub_ruta
        
        # Si no tiene carpetas de sistema, devolver la ruta tal cual (ya es relativa)
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
        
        Args:
            project_object: Objeto Project de TIA Portal (para transacciones).
            plc_object: Objeto Plc donde importar.
            xml_file_path: Ruta al archivo XML a importar.
            target_relative_path: Ruta relativa del grupo destino (ej: "110_Expedicion/3110_Parametros")
            
        Returns:
            True si la importación fue exitosa.
        """
        staging_dir = Path(".build/import_stage").absolute()  # CRÍTICO: Ruta absoluta para Siemens
        
        try:
            xml_file = Path(xml_file_path)
            if not xml_file.exists():
                self._logger.error(f"Archivo XML no encontrado: {xml_file_path}")
                return False

            # Limpiar y crear directorio staging
            if staging_dir.exists():
                shutil.rmtree(staging_dir, ignore_errors=True)
            staging_dir.mkdir(parents=True, exist_ok=True)

            # Copiar solo el XML a importar
            shutil.copy(str(xml_file), str(staging_dir / xml_file.name))

            # Iniciar transacción
            project_object.start_transaction(
                undo_text="Sincronizar DB con comentarios",
                dialog_text=f"Importando {xml_file.name}..."
            )

            # Construir los argumentos dinámicamente para evitar pasar None a la API C#
            kwargs: dict[str, Any] = {"import_root_directory": str(staging_dir.absolute())}
            if target_relative_path:
                kwargs["target_folder_path"] = target_relative_path

            self._logger.debug(f"Llamando import_blocks con kwargs: {kwargs}")
            plc_object.import_blocks(**kwargs)

            # Consolidar
            project_object.end_transaction(rollback=False)
            self._logger.info(f"Bloque importado: {xml_file.name} -> {target_relative_path or 'raiz'}")
            return True

        except Exception as e:
            self._logger.error(f"Error importando bloque: {e}", exc_info=True)
            try:
                project_object.end_transaction(rollback=True)
            except Exception:
                pass
            return False

        finally:
            # Limpieza obligatoria del staging
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
        Orquesta la importación completa usando las funciones de directorio
        nativas de Siemens dentro de un entorno transaccional seguro.
        """
        ruta_build = Path(ruta_build_str)
        self._logger.info("Iniciando secuencia de importación nativa...")

        try:
            project_object.start_transaction(
                undo_text="Generacion de procesos",
                dialog_text=f"Creando proceso {proceso_nombre} automaticamente..."
            )
        except Exception as e:
            self._logger.error(f"No se pudo iniciar la transacción: {e}")
            return False

        try:
            # Fase de Tablas de Variables
            for ruta_tabla in ruta_build.iterdir():
                if ruta_tabla.is_dir() and ("TABLA" in ruta_tabla.name.upper() or "TAG" in ruta_tabla.name.upper()):
                    plc_object.import_plc_tags(import_root_directory=str(ruta_tabla.absolute()))

            # Fase de Bloques de Programa
            for ruta_bloque in ruta_build.iterdir():
                if ruta_bloque.is_dir() and "BLOQUE" in ruta_bloque.name.upper():
                    plc_object.import_blocks(import_root_directory=str(ruta_bloque.absolute()))

            project_object.end_transaction(rollback=False)
            self._logger.info("✅ Importación completada.")
            return True

        except Exception as e:
            self._logger.error(f"❌ FALLO CRÍTICO en importación masiva: {e}", exc_info=True)
            try:
                project_object.end_transaction(rollback=True)
            except Exception as rollback_err:
                msg = f"Fallo crítico durante el rollback: {rollback_err}. TIA Portal podría estar bloqueado."
                self._logger.critical(msg)
                raise TIAImporterError(msg) from rollback_err
            return False

    def exportar_bloque(self, plc_object: Any, block_name: str, target_dir: str) -> bool:
        """
        Exporta un bloque PLC a un directorio de forma plana.
        
        Args:
            plc_object: Objeto Plc de TIA Portal.
            block_name: Nombre del bloque a exportar.
            target_dir: Directorio destino para la exportación.
            
        Returns:
            True si la exportación fue exitosa.
        """
        try:
            program_blocks = plc_object.get_program_blocks()
            bloque = self._find_block_in_group(program_blocks, block_name)
            
            if not bloque:
                self._logger.error(f"Bloque '{block_name}' no encontrado.")
                return False

            destino = Path(target_dir).absolute()  # CRÍTICO: Ruta absoluta para Siemens
            destino.mkdir(parents=True, exist_ok=True)

            # Exportar de forma plana
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

    def _find_block_in_group(self, group_or_blocks: Any, block_name: str) -> Any | None:
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
        
        # Buscar en subgrupos
        groups: list[Any] = []
        if hasattr(group_or_blocks, 'get_groups'):
            groups = group_or_blocks.get_groups()
        elif hasattr(group_or_blocks, 'Groups'):
            groups = group_or_blocks.Groups
        
        for sub_group in groups:
            resultado = self._find_block_in_group(sub_group, block_name)
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