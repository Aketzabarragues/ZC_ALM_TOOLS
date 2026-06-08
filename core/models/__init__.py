"""
Core Layer - Domain Models (Paquete)
=====================================
Re-export publico de los DTOs del dominio.

Mantener estos imports en __init__.py garantiza la retrocompatibilidad:
el resto de la aplicacion puede seguir haciendo
    from core.models import Proceso, BloquePLC, DispED
como antes del refactor.
"""

from core.models.blocks import BloquePLC
from core.models.hardware import (
    DimensionesDispositivos,
    DispEA,
    DispED,
    DispSA,
    DispositivoHardware,
)
from core.models.software import Alarma, PInt, PReal, Proceso

__all__ = [
    "Proceso",
    "PReal",
    "PInt",
    "Alarma",
    "BloquePLC",
    "DispED",
    "DispEA",
    "DispSA",
    "DimensionesDispositivos",
    "DispositivoHardware",
]
