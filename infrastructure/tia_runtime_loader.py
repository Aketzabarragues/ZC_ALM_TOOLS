"""
Infrastructure Layer - TIA Runtime Loader
=========================================
Loader dinamico para siemens_tia_scripting en ejecutables PyInstaller.

Cuando la aplicacion se compila con PyInstaller (--onefile), las dependencias
se extraen en una carpeta temporal (_MEIPASS). Este modulo fuerza a Python a
cargar el .pyd nativo desde ahi, evitando conflictos con versiones globales.

Documentacion oficial de Siemens:
https://support.industry.siemens.com (TIA Scripting Python)
"""
import importlib.util
import os
import sys


def get_file_path(file_name: str) -> str:
    """
    Resuelve la ruta de un archivo bundled en _MEIPASS o en el cwd.

    Args:
        file_name: Nombre del archivo (ej. 'siemens_tia_scripting.pyd')

    Returns:
        Ruta absoluta al archivo.
    """
    meipass = getattr(sys, '_MEIPASS', None)
    if meipass is not None:
        return os.path.join(meipass, file_name)
    return os.path.join(os.path.abspath("."), file_name)


def load_siemens_tia() -> object:
    """
    Carga el modulo siemens_tia_scripting de forma segura.

    Estrategia:
    1. Si estamos en un .exe (PyInstaller), carga el .pyd desde _MEIPASS
    2. Si no, intenta import normal (modo desarrollo)

    Returns:
        Modulo siemens_tia_scripting cargado
    """
    pyd_name = "siemens_tia_scripting.pyd"
    pyd_path = get_file_path(pyd_name)

    # En modo .exe, cargar desde _MEIPASS
    meipass = getattr(sys, '_MEIPASS', None)
    if meipass is not None and os.path.exists(pyd_path):
        spec = importlib.util.spec_from_file_location(
            "siemens_tia_scripting",
            pyd_path
        )
        if spec is not None and spec.loader is not None:
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            return module

    # Fallback: import normal (modo desarrollo o si el .pyd no esta en _MEIPASS)
    import siemens_tia_scripting  # type: ignore[import-not-found]
    return siemens_tia_scripting
