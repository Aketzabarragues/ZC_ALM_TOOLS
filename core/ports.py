"""
Core Layer - Domain Ports (Interfaces)
=======================================
Contratos de los repositorios que la capa de infraestructura debe implementar.

Estos Protocols usan duck typing estático: los use cases importan SOLO
de core.ports, garantizando la inversión de dependencias (Clean Architecture).
"""

from contextlib import AbstractContextManager
from typing import Any, Protocol
from collections.abc import Generator


from core.models import BloquePLC

__all__ = ["ISoftwareRepository"]


class ISoftwareRepository(Protocol):
    """
    Puerto (interface) del repositorio de software.

    Declara las firmas de los metodos que los Use Cases y el
    Automation Flow necesitan para manipular bloques/PLC en TIA Portal.

    Cualquier implementacion concreta (sea pythonnet, sea el wrapper actual)
    debe satisfacer este contrato.
    """

    def build_cache(self, plc_name: str, force: bool = False) -> dict[str, BloquePLC]: ...
    def get_existing_blocks(self, plc_name: str) -> dict[str, BloquePLC]: ...
    def clear_cache(self) -> None: ...
    def force_rescan(self, plc_name: str) -> dict[str, BloquePLC]: ...

    def is_bloque_consistente(self, plc_name: str, block_name: str) -> bool: ...
    def compilar_software(self, plc_name: str) -> bool: ...
    def compilar_bloque(self, plc_name: str, block_name: str) -> bool: ...
    def importar_bloque(
        self, plc_name: str, xml_path: str, folder_path: str
    ) -> bool: ...

    def importar_bloques_generados(
        self,
        plc_name: str,
        ruta_build: str,
        proceso_nombre: str = "desconocido",
    ) -> bool: ...
    def exportar_bloque(self, plc_name: str, block_name: str, target_dir: str) -> bool: ...
    def importar_bloque_single(
        self,
        plc_name: str,
        xml_file_path: str,
        target_relative_path: str = "",
    ) -> bool: ...
    def importar_bloque_override(
        self,
        plc_name: str,
        xml_path: str,
        target_folder: str | None = None,
    ) -> bool: ...
    def sanitize_import_path(self, full_path: str) -> str: ...
    def bloque_existe(self, plc_name: str, block_name: str) -> bool: ...
    def obtener_ruta_bloque(self, plc_name: str, block_name: str) -> str | None: ...
    def actualizar_constantes_proceso(
        self,
        plc_name: str,
        nombre_tabla: str,
        constantes_dict: dict[str, Any],
    ) -> bool: ...

    def get_user_constants(
        self, plc_name: str, table_name: str
    ) -> dict[int, str]: ...
    def update_user_constant_name(
        self, plc_name: str, table_name: str, current_name: str, new_name: str
    ) -> None: ...
    def delete_user_constant(
        self, plc_name: str, table_name: str, name: str
    ) -> None: ...
    def update_user_constant_value(
        self, plc_name: str, table_name: str, constant_name: str, new_value: int
    ) -> None: ...
    def exportar_tabla_variables(
        self, plc_name: str, table_name: str, export_dir: str
    ) -> str: ...
    def importar_tabla_variables(
        self, plc_name: str, xml_path: str, folder_path: str
    ) -> bool: ...

    # --- Transacciones COM (reentrante via Gateway) ---
    def transaccion(
        self, undo_text: str
    ) -> AbstractContextManager[None]: ...
