"""
Infrastructure Layer - TIA Runtime Loader
==========================================
Loader para siemens_tia_scripting en ejecutables PyInstaller (--onefile).

Cuando la aplicacion se compila con PyInstaller, las dependencias se extraen
en una carpeta temporal (_MEIPASS). Este modulo:

  1. Inyecta _MEIPASS en sys.path y en el loader de DLLs de Windows.
  2. Hace un `import siemens_tia_scripting` regular.

CRITICO: NO usamos `importlib.util.spec_from_file_location` para cargar
el .pyd, porque ese mecanismo NO inicializa correctamente pythonnet/CLR.
El .pyd de Siemens es un wrapper de .NET y necesita el loader nativo de
extensiones de Python (_imp) para que pythonnet pueda:
  - Cargar el CLR.
  - Inicializar los tipos .NET (Portal, Project, Plc, Enums, etc.).
  - Registrar el modulo en sys.modules.

Con importlib, el shell del modulo existe pero `ts.Enums`, `ts.Portal`,
`ts.attach_portal`, etc. no se exponen (AttributeError al accederlos).
"""
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
    Carga el modulo de Siemens TIA Portal.

    - En modo Produccion (.exe): anyade _MEIPASS a sys.path (prioridad alta)
      y al loader de DLLs de Windows, luego hace un `import` estandar.
      Esto permite que el loader nativo de Python (_imp) cargue el .pyd
      y que pythonnet/CLR se inicialicen correctamente.

    - En modo Desarrollo: hace un `import` normal del paquete instalado.

    Returns:
        Modulo `siemens_tia_scripting` con todos los tipos .NET expuestos.
    """
    meipass = getattr(sys, '_MEIPASS', None)
    if meipass is not None:
        # 1. Inyectar _MEIPASS en sys.path con PRIORIDAD ALTA (insert, no append).
        #    Asi Python encuentra el .pyd ANTES que cualquier otra version
        #    que pudiera existir en site-packages.
        if meipass not in sys.path:
            sys.path.insert(0, meipass)

        # 2. Registrar _MEIPASS en el loader de DLLs de Windows.
        #    Esto permite que las .dll adyacentes al .pyd (necesarias
        #    para el CLR de .NET) se carguen al inicializar el wrapper.
        os.environ['PATH'] = meipass + os.pathsep + os.environ.get('PATH', '')
        if hasattr(os, 'add_dll_directory'):
            os.add_dll_directory(meipass)

        # 3. Import estandar: usa _imp (loader nativo de extensiones),
        #    registra el modulo en sys.modules, inicializa pythonnet/CLR
        #    y popula TODOS los enums (Enums.ExportOptions,
        #    Enums.PortalMode, Enums.CompilerResultState, etc.).
        import siemens_tia_scripting as ts  # type: ignore[import-not-found]
        return ts
    else:
        # Modo Desarrollo: el paquete viene de site-packages / venv.
        import siemens_tia_scripting as ts  # type: ignore[import-not-found]
        return ts
