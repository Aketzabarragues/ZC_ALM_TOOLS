# 🚀 ZC_ALM_TOOLS

> **ZEUS CONTROL ALIMENTACION** — La navaja suiza de la automatización de TIA Portal.

---

## 🚀 ¿Qué es ZC_ALM_TOOLS?

**ZC_ALM_TOOLS** es una aplicación de consola (TUI) que automatiza la creación, sincronización y mantenimiento de software y hardware en proyectos de **Siemens TIA Portal**. En lugar de hacer clic manualmente durante horas en TIA Portal cuando tienes que generar un nuevo proceso industrial o sincronizar 300 entradas digitales, este programa lo hace por ti de forma **segura, transaccional y masiva**, leyendo los datos de un **único Excel maestro**.

El programa resuelve un problema muy real en la industria: cuando tienes una planta con 50 procesos similares y cada uno tiene 200 parámetros, 500 alarmas y 300 dispositivos, copiar y pegar a mano entre proyectos de TIA Portal es tedioso, propenso a errores y consume horas de ingeniería. ZC_ALM_TOOLS toma una carpeta-plantilla XML, la "muta" matemáticamente (cambiando UIDs, nombres de variables y constantes N_MAX) y la inyecta en TIA Portal con un solo comando, con **rollback atómico** si algo falla a mitad de camino.

---

## 🏗️ La Arquitectura "Para Dummies" (Cómo está organizado el código)

Imagina que ZC_ALM_TOOLS es un **restaurante de 3 plantas**. Cada carpeta del proyecto es una planta con un trabajo muy concreto, y casi nunca se mezclan entre sí.

### 🥘 `core/` — **La Receta (Dominio Puro)**
Esta es la planta donde está la **receta secreta** del restaurante. Aquí viven los ingredientes puros: las clases `Proceso`, `DispED`, `DispEA`, `PReal`, `Alarma`. Esta planta **no sabe nada de Excel, ni de TIA Portal, ni de Windows**. Solo conoce las formas puras de los datos (un proceso tiene un UID, un nombre, una cantidad de parámetros, etc.). Si mañana cambias TIA Portal por otro software, esta carpeta no se toca. **Es la capa más estable y protegida**.

### 🍽️ `application/` — **El Camarero (Casos de Uso y TUI)**
Aquí está el personal que atiende al cliente. Recibe el pedido del usuario (pulsar un botón, seleccionar un PLC, etc.), interpreta la intención, y la traduce en una secuencia de operaciones que cocina la `infrastructure/`. El camarero **no cocina** (no toca TIA Portal directamente): siempre llama a la cocina a través de un "contrato" (`ISoftwareRepository` en `core/ports.py`). Si el cliente pide algo raro, el camarero lo rechaza educadamente sin meter la pata en la cocina.

### 🔥 `infrastructure/` — **La Cocina (TIA Portal, Excel, XML)**
Aquí es donde nos manchamos las manos con el fogón: el wrapper de Siemens (`siemens_tia_scripting`), la lectura de Excel con `openpyxl`, la edición de XML con `minidom`, la gestión del ciclo de vida COM del wrapper, los archivos temporales `.build/`, los logs, etc. Esta es la única capa que importa librerías de terceros pesadas. Si cambias de versión de TIA Portal, este es el único lugar donde tendrás que tocar.

### 🗺️ Diagrama de dependencias (regla de oro)

```
core/  ←  application/  ←  infrastructure/
                  ↑
              (las flechas van de izquierda a derecha:
               el núcleo NO sabe nada del mundo exterior,
               la aplicación coordina, e infrastructure ejecuta)
```

---

## 🔄 Flujos de Trabajo: ¿Qué pasa cuando pulsas un botón?

Cuando seleccionas una opción del menú principal, esta es la cadena exacta de eventos que ocurre (ejemplo: "🎛️ Sincronizar Dispositivos"):

```
👤 Usuario marca "ED" en el checkbox de main_flow.py
    │
    ▼
📋 application/tui/main_flow.py
   (decide que la opción elegida es sincronizar_dispositivos y ED)
    │
    ▼
🎯 application/tui/hardware_flows.py::_flujo_sincronizar_dispositivos(session)
   (lee session.disp_ed_list y session.dimensiones, abre la transacción)
    │
    ▼
🧠 application/use_cases/hardware/sincronizar_dispositivos.py::ejecutar(hw_type="ed", ...)
   (orquesta las 4 fases: N_MAX → COM sync → XML add → DB comments)
    │
    ▼
📦 infrastructure/tia/software_repository.py
   (interfaz limpia; traduce Python a llamadas COM, abre transacciones reentrantes)
    │
    ▼
🔧 infrastructure/tia/gateway.py + infrastructure/tia/importer.py
   (el gateway abre start_transaction / end_transaction; el importer escribe XMLs y llama a TIA Portal)
    │
    ▼
🖥️ TIA Portal (vía siemens_tia_scripting .NET wrapper)
   (aplica los cambios sobre el proyecto .apXX abierto)
    │
    ▼
✅ Una sola entrada en el historial de TIA Portal
   (o ROLLBACK completo si algo falla, dejando el PLC como estaba)
```

---

## 🎛️ GUÍA PASO A PASO: Cómo añadir un nuevo tipo de Hardware (Ej: Entradas Analógicas - EA)

¡Buenas noticias! Gracias al polimorfismo por **Protocol** (`DispositivoHardware`), **no hay que tocar NADA de la lógica de negocio** del caso de uso. Solo hay que dar **4 pasos tontos** y todo funciona. Lo demostramos añadiendo un nuevo tipo de dispositivo: las **Entradas Analógicas (EA)**.

### 📋 Paso 1: Configuración (1 línea en JSON)

Edita `config.json` en la raíz y añade el bloque `"ea"` dentro de `"hardware"`. Solo tienes que saber cómo se llaman las cosas en TIA Portal:

```json
{
    "hardware": {
        "ed": { ... },
        "ea": {
            "db_name": "DB4000_EA",
            "db_array_name": "EA",
            "tag_table": "4000_Ana_Entradas",
            "config_table": "000_Config_Dispositivos",
            "config_constant": "N_MAX_DISP_EA"
        }
    }
}
```

¡Listo! No hay que tocar código Python para esto. El `get_hardware_tia_config("ea")` ya existe y sabrá devolver este DTO.

### 🥘 Paso 2: Modelo de Dominio (1 dataclass, 4 atributos)

Crea la dataclass `DispEA` en `core/models/hardware.py`. **Solo 4 campos obligatorios** (los del Protocol); puedes añadir todos los extras que necesites (rango, unidades, etc.) sin tocar nada más:

```python
@dataclass
class DispEA:
    """Dispositivo de Entrada Analogica (sensor 4-20mA, 0-10V, etc.)."""
    # --- 4 atributos del Protocol DispositivoHardware (obligatorios) ---
    numero: int = 0
    plc_tag: str = ""
    plc_comentario: str = ""
    descripcion: str = ""

    # --- Atributos extra especificos de EA (opcionales) ---
    rango_min: float = 0.0
    rango_max: float = 10.0
    unidades: str = "V"
    canal: int = 0
```

¡Eso es todo! `DispEA` automáticamente cumple el Protocol `DispositivoHardware` por **duck typing estático**: Pylance sabe que `DispEA` puede usarse donde se espera `DispositivoHardware`.

### 📊 Paso 3: Parser de Excel (1 archivo, 1 función)

Crea `infrastructure/parsers/hardware/disp_ea.py` con una función que lea la hoja del Excel:

```python
from core.models import DispEA
from openpyxl import load_workbook

def extraer_disp_ea(ruta_excel: str) -> list[DispEA]:
    wb = load_workbook(ruta_excel, data_only=True, read_only=True)
    ws = wb["DISP_EA"]  # nombre de la hoja en el Excel
    dispositivos: list[DispEA] = []
    for fila in ws.iter_rows(min_row=2, values_only=True):
        dispositivos.append(DispEA(
            numero=fila[0] or 0,
            plc_tag=fila[1] or "",
            plc_comentario=fila[2] or "",
            descripcion=fila[3] or "",
            rango_min=fila[4] or 0.0,
            rango_max=fila[5] or 10.0,
        ))
    wb.close()
    return dispositivos
```

Y añadelo a `infrastructure/excel_parser.py` con un método `extraer_disp_ea(...)`. Luego en `application/session.py` añade el campo:

```python
@dataclass
class AppSession:
    # ...campos existentes...
    disp_ea_list: list[DispEA] = field(default_factory=list)
```

Y en `application/tui/main_flow.py` (en `run()`, junto a la Carga Maestra):

```python
session = AppSession(
    # ...argumentos existentes...
    disp_ea_list=parser.extraer_disp_ea(ruta_excel),
)
```

### 🎚️ Paso 4: Interfaz (1 línea en el checkbox)

En `application/tui/main_flow.py`, dentro del `elif opcion_principal == "sincronizar_dispositivos":`, añade `EA` a las opciones del checkbox:

```python
tipos_seleccionados = questionary.checkbox(
    "Selecciona los tipos de dispositivos a sincronizar "
    "(Espacio para marcar, Enter para confirmar):",
    choices=[
        Choice(" Entradas Digitales (ED)", value="ED"),
        Choice(" Entradas Analógicas (EA)", value="EA"),  # ← NUEVO
    ]
).ask()

# Y luego, en la sección de dispatch:
if "ED" in tipos_seleccionados:
    _flujo_sincronizar_dispositivos(session)  # (cuando generalices el helper, pasar hw_type)

if "EA" in tipos_seleccionados:
    _flujo_sincronizar_dispositivos_ea(session)  # o reusar el mismo con hw_type="ea"
```

**¡Y ya está!** 🎉 El caso de uso `SincronizarDispositivosUseCase` ya está generalizado; solo necesita que le pases `hw_type="ea"` y `dispositivos=session.disp_ea_list`. Cero código nuevo en la lógica.

---

## 🧩 GUÍA PASO A PASO: Cómo añadir nuevos Procesos de Software

### 📊 Cómo se lee el Excel (ExcelParser)

Toda la info del software vive en `infrastructure/excel_parser.py`. Esta clase tiene un método por cada entidad:

| Método                 | Hoja Excel            | Modelo devuelto |
|------------------------|----------------------|-----------------|
| `extraer_procesos()`   | `PROCESOS`           | `list[Proceso]` |
| `extraer_preal()`      | `PREAL` (PReal)      | `list[PReal]`   |
| `extraer_pint()`       | `PINT` (PInt)        | `list[PInt]`    |
| `extraer_alarmas()`    | `ALARMAS`            | `list[Alarma]`  |
| `extraer_disp_ed()`    | `DISP_ED`            | `list[DispED]`  |

Todos los parsers heredan de `infrastructure/parsers/base_parser.py` que abre el Excel con `openpyxl` en modo `read_only=True` (para no cargar 200 MB en RAM si el Excel es grande).

### 🔧 Cómo añadir la sincronización de un nuevo DataBlock

Si el día de mañana tu proyecto tiene un nuevo DataBlock (por ejemplo, `DB9000_RECETAS` con un array `RECETA[1..N]`), la gracia de la `@dataclass TareaSincronizacion` es que **solo tocas el helper `_build_tareas` en `application/tui/software_flows.py`**:

```python
def _build_tareas(proceso, preal_list, pint_list, alarmas_list, recetas_list) -> list[Any]:
    """Helper que construye la lista de TareaSincronizacion."""
    from application.use_cases.software.sincronizar_textos import TareaSincronizacion

    tareas: list[Any] = []
    db_configs = [
        (proceso.db_preal_nombre, "PReal", preal_list, True),
        (proceso.db_pint_nombre, "PInt", pint_list, True),
        (proceso.db_alm_nombre, "ALM", alarmas_list, False),
        (proceso.db_recetas_nombre, "RECETA", recetas_list, False),  # ← NUEVO
    ]
    for db_name, array_name, datos, es_parametro in db_configs:
        if not datos:
            continue
        tareas.append(
            TareaSincronizacion(
                db_name=db_name,
                array_name=array_name,
                items=list(datos),
                get_id_func=lambda x: getattr(x, "numero", 0),
                get_comment_func=lambda x: getattr(
                    x, "comentario_db", getattr(x, "texto", "")
                ),
                es_parametro=es_parametro,
            )
        )
    return tareas
```

Y ya está. El motor de `SincronizarTextosUseCase` (4 pasos: pre-check, compilación, sincronización, post-check) **lo recorre automáticamente** sin que tengas que tocarlo. Solo cambias una lista de tuplas en el helper. Esa es la magia del polimorfismo por dataclass.

### 📝 Tipado fuerte en `TareaSincronizacion`

Como `TareaSincronizacion` es una `@dataclass` con tipos explícitos, si mañana añades un campo `prioridad: int = 5`, todos los call sites se enteran en tiempo de edición (Pylance marca los que olvidan pasarlo). Cero código muerto, cero strings mágicos.

---

## 📦 Compilación y Ejecución

### 🖥️ Modo desarrollo (ejecutar desde el código fuente)

```bash
# 1. Instalar dependencias (solo la primera vez)
pip install -r requirements.txt

# 2. Asegurarse de tener la .whl de TIA Portal en lib/
# (lib/siemens_tia_scripting-1.2.1-cp312-cp312-win_amd64.whl, etc.)

# 3. Ejecutar
python main.py
```

### 🏗️ Modo producción (compilar a .exe con PyInstaller)

El proyecto incluye un script `build_exe.py` configurado con todas las opciones necesarias para empaquetar en un ejecutable standalone para Windows:

```bash
python build_exe.py
```

Esto generará un `.exe` en `dist/` que el usuario final puede ejecutar con doble clic **sin tener Python instalado**. La `.whl` de TIA Portal se incluye automáticamente dentro del bundle.

### 🔍 Validar el tipado y la sintaxis antes de commit

```bash
python -m compileall -q core application infrastructure main.py
```

Si este comando devuelve `OK`, el código compila. Para validar el tipado, abre el proyecto en VS Code con la extensión **Pylance** activada y comprueba que no haya subrayados rojos en los archivos que tocaste.

---

## 📂 Estructura completa del proyecto

```
ZC_ALM_TOOLS/
├── main.py                                # Entry point
├── config.json                            # Config del usuario (rutas, TIA folders, hardware)
├── requirements.txt
├── build_exe.py                           # Script de PyInstaller
│
├── core/                                  # 🥘 Receta (Dominio puro)
│   ├── models/
│   │   ├── software.py                    # Proceso, PReal, PInt, Alarma
│   │   └── hardware.py                    # DispED, DispEA, Protocol DispositivoHardware
│   └── ports.py                           # ISoftwareRepository (Protocol del repo)
│
├── application/                            # 🍽️ Camarero (Use cases + TUI)
│   ├── session.py                          # AppSession (contexto global)
│   ├── tui/
│   │   ├── main_flow.py                   # Bucle principal del menú
│   │   ├── hardware_flows.py              # Subrutina de dispositivos
│   │   ├── software_flows.py              # Subrutinas de procesos
│   │   └── utils.py
│   └── use_cases/
│       ├── hardware/                       # SincronizarDispositivosUseCase
│       └── software/                       # GenerarProcesoUseCase, SincronizarTextosUseCase
│
├── infrastructure/                        # 🔥 Cocina (TIA, Excel, XML)
│   ├── tia/
│   │   ├── gateway.py                      # Ciclo de vida COM + transacciones
│   │   ├── software_repository.py          # Fachada sobre TIA Portal
│   │   ├── importer.py                     # Staging + import masivo
│   │   └── scanner.py                      # Caché de bloques
│   ├── xml/
│   │   ├── generator.py                    # Genera XMLs desde plantilla
│   │   ├── modifier.py                     # Edita comentarios de DBs
│   │   └── tag_modifier.py                 # Edita tablas de tags
│   ├── parsers/
│   │   ├── base_parser.py
│   │   ├── software/                       # preal.py, pint.py, alarmas.py
│   │   └── hardware/                       # disp_ed.py, disp_ea.py
│   ├── excel_parser.py
│   ├── config_manager.py
│   └── ui_dialogs.py                       # Tkinter: seleccionar_excel, etc.
│
├── docs/                                   # Auditorías y documentos
│   └── AUDITORIA_v1.md
│
└── lib/                                    # Wheels offline de TIA Portal
    └── siemens_tia_scripting-*.whl
```

---

## 🤝 Convenciones del proyecto

- **Tipado fuerte obligatorio:** todas las funciones públicas tienen anotaciones de tipo (PEP 484 + `from __future__ import annotations` cuando conviene).
- **Dataclasses para DTOs:** nunca uses `dict[str, Any]` para pasar datos entre capas; usa `@dataclass` o `Protocol`.
- **Cero singletons globales:** todas las dependencias se inyectan vía `AppSession` o vía el `__init__` del caso de uso.
- **Transacciones reentrantes:** nunca abras una transacción de TIA Portal sin envolverla en `with repo.transaccion(...):`.
- **Magic strings al `config.json`:** nada de "2000_Dispositivos" o "003_Proceso" hardcodeado en el código; todo se externaliza vía `config_manager.get_*()`.
- **Logging con `logging` (nunca `print`):** cada clase tiene `self._logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")`.

---

## 📜 Licencia

Proyecto interno de **Zeus Control** — Todos los derechos reservados.
