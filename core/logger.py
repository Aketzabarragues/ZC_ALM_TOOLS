"""
Core Layer - Logger Configuration
=================================
Configuracion centralizada de logging para toda la aplicacion.

Diseno:
  - Root logger = min(file_level, console_level) -> techo mas permisivo.
  - Cada handler recibe su nivel correspondiente segun su tipo:
      * FileHandler / RotatingFileHandler / NTEventLogHandler -> file_level
      * StreamHandler (consola, rich, etc.)                  -> console_level

  Esto permite tener DEBUG en archivo (diagnostico completo) y WARNING
  en consola (TUI limpia con Rich).
"""

import logging
import logging.handlers  # Para RotatingFileHandler
import sys


def setup_logging(enable_file_logging: bool = False) -> None:
    """Configura el root logger de la aplicacion."""
    logger = logging.getLogger()

    # Limpiamos handlers previos por seguridad
    if logger.hasHandlers():
        logger.handlers.clear()

    logger.setLevel(logging.DEBUG)  # Nivel base captura todo (sera re-ajustado)

    formatter = logging.Formatter(
        fmt="%(asctime)s [%(levelname)s] %(name)s - %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S"
    )

    # Handler para la Consola (por defecto silenciosa: WARNING+)
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(logging.WARNING)
    console_handler.setFormatter(formatter)
    logger.addHandler(console_handler)

    # Handler para el Archivo (guarda todo: DEBUG+)
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

    # Silenciadores estandar
    logging.getLogger("PIL").setLevel(logging.WARNING)
    logging.getLogger("openpyxl").setLevel(logging.WARNING)
    logging.getLogger("asyncio").setLevel(logging.WARNING)
    logging.getLogger("comtypes").setLevel(logging.WARNING)


def set_log_levels(file_level: str, console_level: str) -> None:
    """
    Aplica niveles diferenciados a archivo y consola.

    Args:
        file_level: Nivel para FileHandler (ej. "DEBUG").
        console_level: Nivel para StreamHandler (ej. "WARNING").

    Comportamiento:
      1. Convierte ambos strings a enteros con getattr.
      2. Root logger = min(file_int, console_int) (techo mas permisivo).
      3. Itera handlers y aplica segun su tipo concreto.
    """
    logger = logging.getLogger()

    # 1. Convertir a enteros (getattr con fallback seguro)
    file_int: int = getattr(logging, file_level.upper(), logging.DEBUG)
    console_int: int = getattr(logging, console_level.upper(), logging.WARNING)

    # 2. Techo mas permisivo: min() numerico. Asi el root no bloquea
    #    NADA: cada handler aplicara su propio filtro.
    root_level: int = min(file_int, console_int)
    logger.setLevel(root_level)

    # 3. Dispatch por tipo de handler
    file_handler_types = (logging.FileHandler, logging.handlers.RotatingFileHandler)

    for handler in logger.handlers:
        if isinstance(handler, file_handler_types):
            handler.setLevel(file_int)
        else:
            # StreamHandler (consola), NullHandler, etc.
            handler.setLevel(console_int)

    # 4. Log de confirmacion (no se muestra si console_level es mas alto que INFO)
    logging.getLogger(__name__).info(
        f"Niveles aplicados -> file={file_level.upper()}, "
        f"console={console_level.upper()}, root={logging.getLevelName(root_level)}"
    )
