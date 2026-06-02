"""
Core Layer - Logger Configuration
=================================
Configuración centralizada de logging para toda la aplicación.
"""

import logging
import sys


def setup_logging(enable_file_logging: bool = False) -> None:
    """Configura el root logger de la aplicación."""
    logger = logging.getLogger()

    # Limpiamos handlers previos por seguridad
    if logger.hasHandlers():
        logger.handlers.clear()

    logger.setLevel(logging.DEBUG)  # Nivel base captura todo

    formatter = logging.Formatter(
        fmt="%(asctime)s [%(levelname)s] %(name)s - %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S"
    )

    # Handler para la Consola (AHORA SOLO WARNING Y SUPERIOR)
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(logging.WARNING)
    console_handler.setFormatter(formatter)
    logger.addHandler(console_handler)

    # Handler para el Archivo (Guarda ABSOLUTAMENTE TODO)
    if enable_file_logging:
        file_handler = logging.FileHandler(
            "app_debug.log",
            mode="w",
            encoding="utf-8"
        )
        file_handler.setLevel(logging.DEBUG)
        file_handler.setFormatter(formatter)
        logger.addHandler(file_handler)
        logging.info("File logging habilitado. Guardando trazas en app_debug.log")

    # Silenciadores estándar
    logging.getLogger("PIL").setLevel(logging.WARNING)
    logging.getLogger("openpyxl").setLevel(logging.WARNING)
    logging.getLogger("asyncio").setLevel(logging.WARNING)
    logging.getLogger("comtypes").setLevel(logging.WARNING)