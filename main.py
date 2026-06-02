"""
ZC_ALM_TOOLS - ZEUS CONTROL APPLICATION LIFECYCLE MANAGEMENT TOOLS
===================================================================
Punto de entrada principal para la automatización con TIA Portal.
"""

import logging
import os
import sys

# Fix: Forzar consola Windows para prompt_toolkit/questionary
# Elimina la variable TERM heredada de terminals Unix-like
os.environ.pop("TERM", None)
os.environ["PROMPT_TOOLKIT_FORCE_WINDOWS_CONSOLE"] = "1"

from application.automation_flow import run as automation_run
from core.logger import setup_logging

# --- CONFIGURACIÓN DE DEBUG ---
ENABLE_FILE_LOGGING: bool = True
# ------------------------------


def main() -> None:
    """
    Punto de entrada de la aplicación.
    Verifica versión de Python y ejecuta el flujo de automatización.
    """
    logger: logging.Logger = logging.getLogger(__name__)

    # Verificación de versión de Python exigida por el wrapper [cite: 153]
    if sys.version_info < (3, 12) or sys.version_info >= (3, 15):
        logger.error("Versión de Python no soportada. Se requiere 3.12, 3.13 o 3.14.")
        sys.exit(1)

    # Ejecutar flujo de automatización
    # Pasar versión de TIA Portal como argumento si es necesario (ej: "18.0")
    automation_run(version=None)


if __name__ == "__main__":
    setup_logging(ENABLE_FILE_LOGGING)
    main()