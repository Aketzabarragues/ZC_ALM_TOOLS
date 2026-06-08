"""
Application Layer - Session
==========================
Estado encapsulado de la sesion de aplicacion.

Esta clase es el "Contexto" que se pasa a todos los menus, llevando
los repositorios inyectados y los datos del Excel Maestro (Carga Maestra)
para evitar releer el disco en cada flujo.
"""

from dataclasses import dataclass, field

from core.models import DimensionesDispositivos, DispEA, DispED, DispSA
from core.models.software import Alarma, PInt, PReal, Proceso
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
    # Software
    procesos: list[Proceso] = field(default_factory=list)
    preal_list: list[PReal] = field(default_factory=list)
    pint_list: list[PInt] = field(default_factory=list)
    alarmas_list: list[Alarma] = field(default_factory=list)
    # Hardware
    disp_ed_list: list[DispED] = field(default_factory=list)
    disp_ea_list: list[DispEA] = field(default_factory=list)
    disp_sa_list: list[DispSA] = field(default_factory=list)
    dimensiones: DimensionesDispositivos = field(
        default_factory=DimensionesDispositivos
    )
