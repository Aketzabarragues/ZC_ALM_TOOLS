# 📦 ESTRATEGIA DE BUILD - ZC ALM TOOLS (Ejecutable .exe)

**Versión:** 1.0  
**Fecha:** 2026-06-02  
**Autor:** CLINE (AI Software Engineer)  
**Objetivo:** Empaquetar ZC ALM TOOLS en un único `.exe` distribuible en planta.

---

## 1. INFORMACIÓN EXTRAÍDA DEL MANUAL TIA (MCP RAG)

### 1.1 Requisitos de Python (Crítico)

> ⚠️ **TIA Scripting Python requiere Python 3.12.X, 3.13.X o 3.14.X.**
> - Versiones 3.11.X o anteriores: **NO SOPORTADAS**
> - Versiones superiores a 3.14.X: **NO SOPORTADAS**

**Acción:** Verificar la versión de Python antes de compilar:
```bash
python --version
# Debe devolver 3.12.X, 3.13.X o 3.14.X
```

### 1.2 Tipo de Binario

El paquete `siemens_tia_scripting` se distribuye como un **módulo nativo C++**:
- **Extensión:** `.pyd` (Python Dynamic Module, equivalente Windows a `.so` en Linux)
- **Nombre del archivo:** `siemens_tia_scripting.pyd`
- **Nombre del wheel:** `siemens_tia_scripting-x.x.x-cp31x-cp31x-win_amd64.whl`
- **Arquitectura:** `win_amd64` (64-bit Windows)
- **Tag CPython:** `cp31x` (3.12, 3.13 o 3.14 según versión)

### 1.3 Instalación Local (Pre-Build)

```bash
# 1. Activar entorno con Python 3.12-3.14
# 2. Instalar wheel localmente
cd C:\Path\To\TIA_Scripting_Python\binaries\
pip install siemens_tia_scripting-1.0.0-cp312-cp312-win_amd64.whl
```

### 1.4 ⚠️ PROBLEMA CRÍTICO: Local vs Global Binaries

> 📖 **Cita literal del manual:**
> *"When building a Python executable, it's important to ensure that local binaries (such as siemens_tia_scripting.pyd) are used instead of any globally installed packages."*

PyInstaller tiende a usar el `siemens_tia_scripting` instalado en `site-packages` del sistema, lo cual puede causar:
- Incompatibilidad de versión CPython
- DLLs faltantes
- Crash al ejecutar el .exe

### 1.5 Solución Documentada por Siemens

El manual proporciona este código para forzar el uso del `.pyd` bundled:

```python
import importlib.util
import os
import sys

def get_file_path(file_name):
    if hasattr(sys, '_MEIPASS'):
        return os.path.join(sys._MEIPASS, file_name)
    return os.path.join(os.path.abspath("."), file_name)

# Importar el .pyd desde _MEIPASS (carpeta temporal de PyInstaller)
spec = importlib.util.spec_from_file_location(
    "siemens_tia_scripting",
    get_file_path("siemens_tia_scripting.pyd")
)
ts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ts)
```

### 1.6 Comando de Build Documentado

```bash
pyinstaller -F /mytiascript.py \
  --workpath /temp \
  -n MyTiaExecutable \
  --clean \
  --distpath ./output \
  --add-data "../dep/:."
```

**Flags importantes:**
- `-F` / `--onefile`: Empaqueta todo en un único `.exe`
- `--workpath`: Directorio temporal de build
- `-n`: Nombre del ejecutable
- `--clean`: Limpia builds anteriores
- `--distpath`: Carpeta de salida
- `--add-data`: Incluye archivos adicionales (separador `:` en Windows con `;`)

---

## 2. DEPENDENCIAS DEL PROYECTO (requirements.txt)

```
siemens_tia_scripting    # ⚠️ Wrapper nativo C++ (CRÍTICO)
psutil                    # Detección de procesos
questionary               # TUI interactiva
pandas                    # Lectura de Excel
openpyxl                  # Backend Excel
rich                      # Consola enriquecida
```

---

## 3. ESTRATEGIA DE EMPAQUETADO

### 3.1 Arquitectura del .exe

```
dist/
└── ZC_ALM_TOOLS.exe  (~200-300 MB)
    └── _MEIPASS/  (carpeta temporal auto-extraída al ejecutar)
        ├── siemens_tia_scripting.pyd  ⚠️ CRÍTICO
        ├── python312.dll
        ├── pandas/...
        ├── openpyxl/...
        ├── rich/...
        └── questionary/...
```

### 3.2 Modificaciones al Código Fuente

#### 3.2.1 Hook para `siemens_tia_scripting`

Crear `infrastructure/tia_runtime_loader.py`:

```python
"""
Runtime loader para siemens_tia_scripting en ejecutables PyInstaller.
"""
import importlib.util
import os
import sys


def get_file_path(file_name: str) -> str:
    """Resuelve la ruta del archivo (bundled o dev)."""
    if hasattr(sys, '_MEIPASS'):
        return os.path.join(sys._MEIPASS, file_name)
    return os.path.join(os.path.abspath("."), file_name)


def load_siemens_tia():
    """Carga siemens_tia_scripting desde _MEIPASS o sistema."""
    spec = importlib.util.spec_from_file_location(
        "siemens_tia_scripting",
        get_file_path("siemens_tia_scripting.pyd")
    )
    if spec is None or spec.loader is None:
        # Fallback a import normal (modo desarrollo)
        import siemens_tia_scripting as ts
        return ts
    ts = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ts)
    return ts
```

#### 3.2.2 Modificar `tia_service.py`

```python
# ANTES
import siemens_tia_scripting as ts

# DESPUÉS
from infrastructure.tia_runtime_loader import load_siemens_tia
ts = load_siemens_tia()  # Auto-detecta PyInstaller vs desarrollo
```

### 3.3 Carpeta `.build/`

El código actual usa `Path(".build")` en varios puntos. **No necesita cambios** porque:
- Se crea automáticamente con `Path(".build").mkdir(exist_ok=True)`
- Solo afecta al directorio de trabajo actual (cwd)
- El usuario debe ejecutar el .exe desde una carpeta writable

**Recomendación:** Documentar en el README que el .exe necesita permisos de escritura en su carpeta.

---

## 4. ARCHIVO `zc_alm_tools.spec`

```python
# -*- mode: python ; coding: utf-8 -*-
"""
PyInstaller spec file para ZC ALM TOOLS.
Documentación: https://pyinstaller.org/en/stable/spec-files.html
"""

from PyInstaller.utils.hooks import collect_submodules, collect_data_files

block_cipher = None

# Análisis: dependencias detectadas automáticamente + hidden imports manuales
a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[
        # ⚠️ CRÍTICO: Incluir el .pyd nativo de Siemens explícitamente
        # Se busca en el directorio site-packages del venv actual
        ('siemens_tia_scripting.pyd', '.'),
    ],
    datas=[
        # Incluir config.json si lo necesitamos junto al exe
        # ('config.json', '.'),
    ],
    hiddenimports=[
        # --- SIEMENS TIA ---
        'siemens_tia_scripting',
        'pythonnet',
        'clr',
        
        # --- LIBRERÍAS DINÁMICAS ---
        'pandas',
        'openpyxl',
        'questionary',
        'rich',
        'rich.console',
        'rich.table',
        'rich.panel',
        'rich.text',
        'rich.live',
        'rich.spinner',
        'rich.status',
        'rich.progress',
        'rich.layout',
        'rich.columns',
        'rich.prompt',
        'psutil',
        
        # --- MÓDULOS PROPIOS ---
        'core',
        'core.models',
        'core.logger',
        'application',
        'application.automation_flow',
        'application.use_cases',
        'application.use_cases.generar_proceso',
        'application.use_cases.sincronizar_textos',
        'infrastructure',
        'infrastructure.excel_parser',
        'infrastructure.tia_service',
        'infrastructure.tia_scanner',
        'infrastructure.tia_importer',
        'infrastructure.tia_runtime_loader',
        'infrastructure.ui_dialogs',
        'infrastructure.config_manager',
        'infrastructure.xml_generator',
        'infrastructure.xml_modifier',
        'infrastructure.parsers',
        'infrastructure.parsers.base_parser',
        'infrastructure.parsers.procesos',
        'infrastructure.parsers.preal',
        'infrastructure.parsers.pint',
        'infrastructure.parsers.alarmas',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        # Excluir módulos innecesarios para reducir tamaño
        'tkinter',
        'matplotlib',
        'numpy.tests',
        'pandas.tests',
        'pytest',
        'unittest',
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='ZC_ALM_TOOLS',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,  # Comprimir con UPX (reduce ~30% tamaño)
    upx_exclude=[
        'siemens_tia_scripting.pyd',  # No comprimir el .pyd
        'python312.dll',
    ],
    runtime_tmpdir=None,
    console=True,  # Mostrar consola (necesario para TUI)
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=None,  # Añadir 'assets/icon.ico' si existe
)
```

---

## 5. SCRIPT DE BUILD (`build_exe.py`)

```python
"""
Script auxiliar para automatizar la compilación del .exe
Uso: python build_exe.py
"""
import os
import shutil
import subprocess
import sys
from pathlib import Path


def check_python_version() -> None:
    """Valida que la versión de Python sea compatible (3.12-3.14)."""
    major, minor = sys.version_info[:2]
    if (major, minor) not in [(3, 12), (3, 13), (3, 14)]:
        print(f"❌ ERROR: Python {major}.{minor} no soportado.")
        print("   TIA Scripting requiere Python 3.12, 3.13 o 3.14")
        sys.exit(1)
    print(f"✅ Python {major}.{minor} detectado (compatible)")


def find_siemens_pyd() -> Path:
    """Localiza el archivo siemens_tia_scripting.pyd."""
    import siemens_tia_scripting
    pyd_path = Path(siemens_tia_scripting.__file__)
    if not pyd_path.exists():
        print(f"❌ ERROR: No se encontró {pyd_path}")
        sys.exit(1)
    print(f"✅ siemens_tia_scripting.pyd en: {pyd_path}")
    return pyd_path


def clean_build_dirs() -> None:
    """Elimina carpetas de builds anteriores."""
    for folder in ['build', 'dist']:
        if Path(folder).exists():
            print(f"🧹 Limpiando {folder}/...")
            shutil.rmtree(folder, ignore_errors=True)


def run_pyinstaller() -> None:
    """Ejecuta PyInstaller con el archivo .spec."""
    spec_file = Path('zc_alm_tools.spec')
    if not spec_file.exists():
        print(f"❌ ERROR: No se encuentra {spec_file}")
        sys.exit(1)
    
    print("🔨 Ejecutando PyInstaller...")
    cmd = ['pyinstaller', '--clean', str(spec_file)]
    print(f"   Comando: {' '.join(cmd)}")
    
    result = subprocess.run(cmd, check=False)
    if result.returncode != 0:
        print("❌ ERROR durante la compilación")
        sys.exit(1)
    
    exe_path = Path('dist') / 'ZC_ALM_TOOLS.exe'
    if exe_path.exists():
        size_mb = exe_path.stat().st_size / (1024 * 1024)
        print(f"\n✅ COMPILACIÓN EXITOSA")
        print(f"   Ejecutable: {exe_path.absolute()}")
        print(f"   Tamaño: {size_mb:.1f} MB")
    else:
        print(f"❌ No se generó {exe_path}")


def main() -> None:
    print("=" * 60)
    print("ZC ALM TOOLS - Build Script")
    print("=" * 60)
    
    check_python_version()
    find_siemens_pyd()
    clean_build_dirs()
    run_pyinstaller()
    
    print("\n📋 Próximos pasos:")
    print("   1. Probar el .exe en una máquina con TIA Portal instalado")
    print("   2. Verificar que detecta siemens_tia_scripting.pyd")
    print("   3. Validar conexión con TIA Portal")


if __name__ == '__main__':
    main()
```

---

## 6. PROCEDIMIENTO DE BUILD

### 6.1 Pre-requisitos

```bash
# 1. Instalar Python 3.12.X (3.12.7 o superior)
# 2. Crear venv
python -m venv .venv
.venv\Scripts\activate

# 3. Instalar dependencias
pip install -r requirements.txt
pip install pyinstaller

# 4. Instalar siemens_tia_scripting desde wheel local
pip install path/to/siemens_tia_scripting-1.0.0-cp312-cp312-win_amd64.whl
```

### 6.2 Build

```bash
# Opción A: Script automático
python build_exe.py

# Opción B: Comando directo
pyinstaller --clean zc_alm_tools.spec
```

### 6.3 Verificación

```bash
# 1. Verificar que el .exe existe
dir dist\ZC_ALM_TOOLS.exe

# 2. Probar en CMD
cd dist
ZC_ALM_TOOLS.exe

# 3. Verificar que detecta TIA Portal
# (El .exe debe abrir la TUI sin errores de DLL)
```

---

## 7. RIESGOS Y MITIGACIONES

| # | Riesgo | Mitigación |
|---|--------|------------|
| 1 | `.pyd` no se incluye en el .exe | Hook runtime + `binaries=[('siemens_tia_scripting.pyd', '.')]` en .spec |
| 2 | Carpeta `.build/` no se crea | `Path.mkdir(exist_ok=True)` en cada uso (ya implementado) |
| 3 | Tamaño del .exe >300MB | UPX compression + excluir tests/módulos innecesarios |
| 4 | Antivirus bloquea el .exe | Firmar digitalmente (futuro) |
| 5 | TIA Portal no instalado | Mensaje claro: "PortalNotRunningError" |
| 6 | Usuario sin permisos en carpeta | Documentar que necesita permisos de escritura |
| 7 | Versión de Python incorrecta | `check_python_version()` en build script |

---

## 8. PRUEBAS POST-BUILD

### 8.1 Checklist de Validación

- [ ] El .exe arranca sin errores
- [ ] La TUI (questionary) se muestra correctamente
- [ ] El selector de Excel abre el diálogo tkinter
- [ ] La conexión a TIA Portal funciona
- [ ] El build_cache escanea bloques correctamente
- [ ] La importación de XML funciona
- [ ] La compilación se ejecuta
- [ ] Los logs se generan en `.build/`

### 8.2 Debug en Caso de Error

Si el .exe falla al iniciar:

```bash
# 1. Ejecutar desde CMD para ver errores
cd dist
ZC_ALM_TOOLS.exe

# 2. Verificar que el .pyd está presente
# (PyInstaller lo extrae a %TEMP%/_MEIxxxxx/)
# En código, sys._MEIPASS apunta a esa carpeta
```

---

## 9. DISTRIBUCIÓN EN PLANTA

### 9.1 Archivos a Distribuir

```
ZC_ALM_TOOLS_v1.0.zip
├── ZC_ALM_TOOLS.exe       # Ejecutable principal
├── README.md              # Instrucciones
├── INSTALL.md             # Pasos de instalación
└── config.example.json    # Configuración inicial
```

### 9.2 Requisitos en la Máquina Destino

- ✅ Windows 10/11 64-bit
- ✅ TIA Portal V18+ instalado
- ✅ .NET Framework 4.8+ (viene con TIA Portal)
- ❌ **NO requiere Python instalado** (gracias a PyInstaller)
- ❌ **NO requiere pip install** (todo bundled)

### 9.3 Tamaño Estimado

- **Sin compresión UPX:** ~280 MB
- **Con compresión UPX:** ~190 MB
- **Tiempo de extracción al iniciar:** 3-5 segundos (onefile overhead)

---

## 10. COMANDO RÁPIDO (TL;DR)

Si ya tienes el entorno configurado, el build completo es:

```bash
# Activar venv
.venv\Scripts\activate

# Build
pyinstaller --clean zc_alm_tools.spec

# Resultado
dir dist\ZC_ALM_TOOLS.exe
```

---

**Firmado:** CLINE (AI Software Engineer)  
**Revisión:** 1.0 - Estrategia de empaquetado inicial
