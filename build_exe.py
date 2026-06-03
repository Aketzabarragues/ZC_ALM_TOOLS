"""
Script auxiliar para automatizar la compilación del .exe
Uso: python build_exe.py
"""
import glob
import os
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


def find_siemens_pyd() -> tuple[Path | None, list[Path]]:
    """
    Localiza el archivo .pyd de siemens_tia_scripting y sus DLLs/XMLs asociadas,
    y los copia a la raíz del proyecto para que PyInstaller los pueda encontrar.

    Returns:
        Tupla (ruta_pyd_destino, archivos_copiados):
        - ruta_pyd_destino: Path al .pyd en la raíz o None si no se encontró.
        - archivos_copiados: Lista exacta de archivos que hemos copiado a la raíz
          (NO contendrá archivos que ya estuvieran allí o que sean DLLs del sistema).
        Se devuelve explícitamente para que clean_vendor_copies() borre SOLO lo
        que nosotros hemos ensuciado, no archivos legítimos del usuario.
    """
    archivos_copiados: list[Path] = []
    try:
        import siemens_tia_scripting

        # Extracción segura para Pylance
        file_path_str = getattr(siemens_tia_scripting, '__file__', None)
        if file_path_str is None:
            print("[ERROR] El modulo no tiene archivo fisico (__file__ es None).")
            return None, archivos_copiados

        pyd_path = Path(file_path_str)
        if not pyd_path.exists():
            print(f"[ERROR] No se encontro {pyd_path}")
            return None, archivos_copiados

        print(f"[OK] siemens_tia_scripting detectado en: {pyd_path}")

        # 1. Copiar el .pyd
        dest_pyd = Path('siemens_tia_scripting.pyd').absolute()
        if pyd_path.resolve() != dest_pyd:
            shutil.copy(pyd_path, dest_pyd)
            archivos_copiados.append(dest_pyd)
            print(f"[OK] .pyd copiado a la raíz: {dest_pyd.name}")
        else:
            print("[OK] .pyd ya estaba en la raíz (no copiado de nuevo)")

        # 2. Copiar TODAS las .dll y .xml de esa misma carpeta
        base_dir = pyd_path.parent
        for ext in ['*.dll', '*.xml']:
            for asset_file in base_dir.glob(ext):
                dest_asset = Path(asset_file.name).absolute()
                if asset_file.resolve() != dest_asset:
                    shutil.copy(asset_file, dest_asset)
                    archivos_copiados.append(dest_asset)
                print(f"[OK] Dependencia detectada y copiada: {asset_file.name}")

        if not archivos_copiados:
            print("[WARN] No se encontraron assets para copiar (solo .pyd)")

        return dest_pyd, archivos_copiados
    except ImportError:
        print("[ERROR] siemens_tia_scripting no esta instalado.")
        print("        pip install siemens_tia_scripting-x.x.x-cp312-cp312-win_amd64.whl")
        return None, archivos_copiados


def clean_build_dirs() -> None:
    """Elimina carpetas de builds anteriores."""
    for folder in ['build', 'dist']:
        if Path(folder).exists():
            print(f"[CLEAN] Limpiando {folder}/...")
            shutil.rmtree(folder, ignore_errors=True)


def clean_vendor_copies(archivos_a_borrar: list[Path]) -> None:
    """
    Borra de la raíz EXCLUSIVAMENTE los archivos que find_siemens_pyd() ha copiado.

    Esto es seguro porque operamos sobre una lista explícita, no sobre globs
    que podrían匹配 archivos legítimos del usuario (.dll/.xml en otros contextos).

    Args:
        archivos_a_borrar: Lista de Path devuelta por find_siemens_pyd().
    """
    if not archivos_a_borrar:
        print("[CLEAN] Nada que limpiar (lista vacia)")
        return

    print(f"[CLEAN] Limpiando {len(archivos_a_borrar)} archivos vendor copiados a la raíz...")
    for file_path in archivos_a_borrar:
        try:
            if file_path.exists():
                os.remove(file_path)
                print(f"  - Eliminado: {file_path.name}")
            else:
                print(f"  - Ya no existe (omitido): {file_path.name}")
        except OSError as e:
            print(f"  - Error eliminando {file_path.name}: {e}")


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

    # Paso 1: copiar dependencias de Siemens a la raíz para que PyInstaller las encuentre
    pyd_path, archivos_copiados = find_siemens_pyd()
    if pyd_path is None:
        sys.exit(1)

    # Paso 2: limpiar builds anteriores
    clean_build_dirs()

    # Paso 3: ejecutar PyInstaller
    if not run_pyinstaller():
        sys.exit(1)

    # Paso 4: limpieza explícita y SEGURA de los archivos que copiamos
    clean_vendor_copies(archivos_copiados)

    print("\n[NEXT STEPS]")
    print("  1. Probar el .exe en una maquina con TIA Portal instalado")
    print("  2. Verificar que detecta siemens_tia_scripting.pyd")
    print("  3. Validar conexion con TIA Portal")


if __name__ == '__main__':
    main()
