"""
Infrastructure Layer - TIA Runtime Loader
=========================================
Loader dinamico para siemens_tia_scripting en ejecutables PyInstaller.

Cuando la aplicacion se compila con PyInstaller (--onefile), las dependencias
se extraen en una carpeta temporal (_MEIPASS). Este modulo fuerza a Python a
cargar el .pyd nativo desde ahi, evitando conflictos con versiones globales.

Implementa la solucion oficial documentada por Siemens (Manual v1.2.1, seccion 1.7.1)
para resolver las rutas de los binarios .pyd y .dll en entornos PyInstaller.
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
    Carga el módulo de Siemens TIA Portal de forma dinámica.
    Implementa la solución oficial documentada por Siemens (Manual v1.2.1)
    para resolver las rutas de los binarios .pyd y .dll en entornos PyInstaller.
    """
    meipass = getattr(sys, '_MEIPASS', None)
    if meipass is not None:
        # Modo Producción (.exe): Cargar usando importlib apuntando a _MEIPASS
        # Esto permite que el CLR de .NET encuentre las .dll adyacentes al .pyd

        # Inyectar _MEIPASS en todas las rutas de búsqueda posibles de Windows y Python
        sys.path.append(meipass)
        os.environ['PATH'] = meipass + os.pathsep + os.environ.get('PATH', '')
        if hasattr(os, 'add_dll_directory'):
            os.add_dll_directory(meipass)

        pyd_path = os.path.join(meipass, "siemens_tia_scripting.pyd")

        spec = importlib.util.spec_from_file_location("siemens_tia_scripting", pyd_path)
        if spec is None or spec.loader is None:
            raise ImportError(f"No se pudo crear el spec para el módulo en {pyd_path}")

        ts = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(ts)
        return ts
    else:
        # Modo Desarrollo: Usar el paquete instalado global/virtual via pip
        import siemens_tia_scripting as ts  # type: ignore[import-not-found]
        return ts
