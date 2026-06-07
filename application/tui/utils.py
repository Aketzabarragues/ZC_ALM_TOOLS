"""
Application Layer - TUI: Utilidades
====================================
Helpers de la capa TUI que no dependen de ningun otro modulo del subpaquete.
Vivir aqui evita la dependencia circular entre main_flow.py y
software_flows.py.
"""

import os

__all__ = ["_clear_screen", "_pertenece_al_proceso"]


def _clear_screen() -> None:
    """Limpia la consola para una mejor experiencia TUI."""
    os.system('cls' if os.name == 'nt' else 'clear')


def _pertenece_al_proceso(
    nombre_proceso: str, nombre_destino: str, codigo_destino: str
) -> bool:
    """Helper para filtrar datos por proceso (case-insensitive)."""
    if not nombre_proceso:
        return False
    p_upper = nombre_proceso.upper()
    return p_upper == (nombre_destino.upper() if nombre_destino else "") or \
           p_upper == (codigo_destino.upper() if codigo_destino else "")
