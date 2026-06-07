"""
Infrastructure Layer - TIA Service (Shim de Compatibilidad)
=============================================================
Fachada temporal que mantiene la API historica TIAService tras la
division en Gateway + Repository.

TODA la logica de ciclo de vida vive ahora en TIAPortalGateway.
TODA la logica de software vive ahora en SoftwareRepository.
TIAService solo:
  1. Hereda de TIAPortalGateway (mantiene __enter__/__exit__, attach, etc.).
  2. Crea un SoftwareRepository interno en su __init__.
  3. Redefine (con firma explicita) los metodos de software delegando.

Este shim sera eliminado en una version futura, cuando todos los
callers importen directamente TIAPortalGateway / SoftwareRepository.
"""

import logging
from pathlib import Path
from typing import Any, cast

from core.models import BloquePLC
from infrastructure.tia.gateway import (
    TIAPortalGateway,
    TIAServiceError,
)
from infrastructure.tia.importer import TIAImporter
from infrastructure.tia.software_repository import SoftwareRepository

__all__ = ["TIAService", "TIAServiceError"]


class TIAService(TIAPortalGateway):
    """
    Fachada temporal de compatibilidad (Shim).
    Mantiene la API historica: TIAService(version, scanner) sigue funcionando.
    """

    def __init__(self, version: str | None = None, scanner=None) -> None:
        # 1. Inicializar el Gateway (ciclo de vida COM)
        super().__init__(version=version, scanner=scanner)

        # 2. Recuperar el scanner del Gateway.
        #    Como la validacion es runtime (no de tipo), hacemos cast
        #    explicito: si llega None, lanzamos error.
        if self._scanner is None:
            # El scanner es obligatorio para SoftwareRepository; en la practica
            # siempre se inyecta desde el Composition Root, pero el gateway
            # lo declara opcional. Validamos explicitamente.
            raise TIAServiceError(
                "TIAScanner no ha sido inyectado. El shim TIAService requiere scanner."
            )
        scanner_instance = cast(Any, self._scanner)  # Narrow para mypy

        # 3. Reutilizar el importer que el Gateway ya creo (compat con la API original)
        importer = self._importer

        # 4. Crear el Repository de software
        self._sw_repo = SoftwareRepository(
            gateway=self,
            scanner=scanner_instance,
            importer=importer,
        )

    # ============================================================== #
    #  Delegacion explicita de metodos de software
    #  (firmas completas para que mypy/pylance esten contentos)
    # ============================================================== #

    def get_existing_blocks(self, plc_name: str) -> dict[str, BloquePLC]:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.get_existing_blocks(plc_name)

    def build_cache(self, plc_name: str, force: bool = False) -> dict[str, BloquePLC]:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.build_cache(plc_name, force)

    def clear_cache(self) -> None:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.clear_cache()

    def force_rescan(self, plc_name: str) -> dict[str, BloquePLC]:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.force_rescan(plc_name)

    def is_bloque_consistente(self, plc_name: str, block_name: str) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.is_bloque_consistente(plc_name, block_name)

    def compilar_software(self, plc_name: str) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.compilar_software(plc_name)

    def importar_bloques_generados(
        self,
        plc_name: str,
        ruta_build: str,
        proceso_nombre: str = "desconocido",
    ) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.importar_bloques_generados(plc_name, ruta_build, proceso_nombre)

    def exportar_bloque(self, plc_name: str, block_name: str, target_dir: str) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.exportar_bloque(plc_name, block_name, target_dir)

    def importar_bloque_single(
        self,
        plc_name: str,
        xml_file_path: str,
        target_relative_path: str = "",
    ) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.importar_bloque_single(
            plc_name, xml_file_path, target_relative_path
        )

    def importar_bloque_override(
        self,
        plc_name: str,
        xml_path: str,
        target_folder: str | None = None,
    ) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.importar_bloque_override(plc_name, xml_path, target_folder)

    def sanitize_import_path(self, full_path: str) -> str:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.sanitize_import_path(full_path)

    def bloque_existe(self, plc_name: str, block_name: str) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.bloque_existe(plc_name, block_name)

    def obtener_ruta_bloque(self, plc_name: str, block_name: str) -> str | None:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.obtener_ruta_bloque(plc_name, block_name)

    def actualizar_constantes_proceso(
        self,
        plc_name: str,
        nombre_tabla: str,
        constantes_dict: dict[str, Any],
    ) -> bool:
        """[Shim] Delega en SoftwareRepository."""
        return self._sw_repo.actualizar_constantes_proceso(
            plc_name, nombre_tabla, constantes_dict
        )
