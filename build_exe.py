"""
Script auxiliar para automatizar la compilación del .exe
Uso: python build_exe.py
"""
import shutil
import subprocess
import sys
from pathlib import Path


def check_python_version() -> None:
    """Valida que la versión de Python sea compatible (3.12-3.14)."""
    major, minor = sys.version_info[:2]
    if (major, minor) not in [(3, 12), (3, 13), (3, 14)]:
        print(f"[ERROR] Python {major}.{minor} no soportado.")
        print("        TIA Scripting requiere Python 3.12, 3.13 o 3.14")
        sys.exit(1)
    print(f"[OK] Python {major}.{minor} detectado (compatible)")


def find_siemens_pyd() -> Path | None:
    """Localiza el archivo siemens_tia_scripting.pyd y lo copia a la raíz."""
    try:
        import siemens_tia_scripting

        # Extracción segura para Pylance
        file_path_str = getattr(siemens_tia_scripting, '__file__', None)
        if file_path_str is None:
            print("[ERROR] El modulo no tiene archivo fisico (__file__ es None).")
            return None

        pyd_path = Path(file_path_str)
        if not pyd_path.exists():
            print(f"[ERROR] No se encontro {pyd_path}")
            return None

        print(f"[OK] siemens_tia_scripting detectado en: {pyd_path}")

        # Copiar a la raíz para que el .spec lo encuentre
        dest_path = Path('siemens_tia_scripting.pyd')
        shutil.copy(pyd_path, dest_path)
        print(f"[OK] Archivo copiado a la raíz local para el empaquetado.")

        return dest_path
    except ImportError:
        print("[ERROR] siemens_tia_scripting no esta instalado.")
        print("        pip install siemens_tia_scripting-x.x.x-cp312-cp312-win_amd64.whl")
        return None


def clean_build_dirs() -> None:
    """Elimina carpetas de builds anteriores."""
    for folder in ['build', 'dist']:
        if Path(folder).exists():
            print(f"[CLEAN] Limpiando {folder}/...")
            shutil.rmtree(folder, ignore_errors=True)


def run_pyinstaller() -> bool:
    """Ejecuta PyInstaller con el archivo .spec."""
    spec_file = Path('zc_alm_tools.spec')
    if not spec_file.exists():
        print(f"[ERROR] No se encuentra {spec_file}")
        return False

    print("[BUILD] Ejecutando PyInstaller...")
    cmd = ['pyinstaller', '--clean', str(spec_file)]
    print(f"        Comando: {' '.join(cmd)}")

    result = subprocess.run(cmd, check=False)
    if result.returncode != 0:
        print("[ERROR] Fallo durante la compilacion")
        return False

    exe_path = Path('dist') / 'ZC_ALM_TOOLS.exe'
    if exe_path.exists():
        size_mb = exe_path.stat().st_size / (1024 * 1024)
        print(f"\n[SUCCESS] COMPILACION EXITOSA")
        print(f"          Ejecutable: {exe_path.absolute()}")
        print(f"          Tamano: {size_mb:.1f} MB")
        return True

    print(f"[ERROR] No se genero {exe_path}")
    return False


def main() -> None:
    print("=" * 60)
    print("ZC ALM TOOLS - Build Script")
    print("=" * 60)

    check_python_version()

    if find_siemens_pyd() is None:
        sys.exit(1)

    clean_build_dirs()

    if not run_pyinstaller():
        sys.exit(1)

    print("\n[NEXT STEPS]")
    print("  1. Probar el .exe en una maquina con TIA Portal instalado")
    print("  2. Verificar que detecta siemens_tia_scripting.pyd")
    print("  3. Validar conexion con TIA Portal")


if __name__ == '__main__':
    main()
