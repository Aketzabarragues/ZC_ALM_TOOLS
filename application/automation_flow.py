"""
Shim de compatibilidad para main.py.

TODO el codigo del orquestador vive ahora en application.tui.main_flow.
Este archivo solo re-exporta `run` para que:
    from application.automation_flow import run
siga funcionando intacto.
"""

from application.tui.main_flow import run

__all__ = ["run"]
