"""
Application Layer - Session
==========================
Estado encapsulado de la sesion de aplicacion.

Esta clase es el "Contexto" que se pasa a todos los menus, llevando
los repositorios inyectados y los datos del Excel Maestro (Carga Maestra)
para evitar releer el disco en cada flujo.
"""

from dataclasses import dataclass, field

from core.models import DimensionesDispositivos, DispED
from core.ports import ISoftwareRepository
from infrastructure.tia.gateway import TIAPortalGateway
from infrastructure.tia.scanner import TIAScanner

__all__ = ["AppSession"]


@dataclass
class AppSession:
    """Sesion de aplicacion - estado encapsulado para evitar globales."""
    # --- Repositorios (inyectados por el Composition Root) ---
    gateway: TIAPortalGateway
    software_repo: ISoftwareRepository
    scanner: TIAScanner

    # --- Estado de usuario ---
    plc_seleccionado: str | None = None
    ruta_excel: str | None = None

    # --- Carga Maestra del Excel (precargada al inicio) ---
    # Hardware
    disp_ed_list: list[DispED] = field(default_factory=list)
    dimensiones: DimensionesDispositivos = field(
        default_factory=DimensionesDispositivos
    )
