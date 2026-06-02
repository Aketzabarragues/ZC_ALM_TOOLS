# ZC ALM TOOLS (Python Edition)

**Automatización de Ingeniería en TIA Portal mediante scripting Openness y COM Interop**

---

## Descripción General

**ZC ALM TOOLS** es una herramienta de automatización avanzada diseñada para ingenieros de automatización industrial que trabajan con Siemens TIA Portal. La aplicación permite:

- **Lectura de datos desde Excel**: Extrae procesos, parámetros (PReal, PInt) y alarmas de un libro de cálculo maestro.
- **Inyección masiva de bloques**: Genera y移植 bloques PLC (DBs, FCs, FBs) desde plantillas XML mutadas con datos específicos del proceso.
- **Manipulación en vivo (COM)**: Modifica directamente las constantes de dimensionamiento (`N_MAX_PREAL`, `N_MAX_PINT`, etc.) en la memoria RAM del PLC sin necesidad de exportar/importar.

---

## Arquitectura del Proyecto (Clean Architecture)

```
ZC_ALM_TOOLS/
├── core/                       # 🧠 DOMINIO PURO
│   ├── models.py              # Dataclasses: Proceso, PReal, PInt, Alarma, BloquePLC
│   └── logger.py              # Logging estructurado (logging estándar)
│
├── infrastructure/            # 🔌 ADAPTADORES Y SERVICIOS EXTERNOS
│   ├── tia_service.py         # UNICO módulo que importa siemens_tia_scripting
│   ├── tia_scanner.py        # Escaneo y caché de bloques del PLC
│   ├── tia_importer.py       # Importación XML y análisis de transacciones
│   ├── excel_parser.py       # Extracción de datos desde Excel (.xlsx)
│   ├── xml_generator.py      # Generación de bloques XML desde plantillas
│   ├── xml_modifier.py       # Mutación de XML (textos, comentarios)
│   ├── ui_dialogs.py         # diálogos nativos (Tkinter file dialogs)
│   └── config_manager.py     # Persistencia de configuración
│
├── application/               # 🎯 CASOS DE USO Y FLUJOS
│   ├── automation_flow.py    # Máquina de estados con main loop interactivo
│   └── use_cases/
│       ├── generar_proceso.py    # Radar anti-colisiones + preflight
│       └── sincronizar_textos.py # Motor inteligente de textos
│
└── main.py                    # 🎬 Entry point
```

### Propósito de cada capa

| Capa | Responsabilidad | Dependencias |
|------|----------------|--------------|
| **core** | Modelos de dominio puros. Sin dependencias externas ni de frameworks. | Ninguna |
| **infrastructure** | Adaptadores para servicios externos (TIA Portal, Excel, archivos). **Aquí se importa siemens_tia_scripting**. | core |
| **application** | Orquestación de flujos y casos de uso. Coordina UI ↔ Infraestructura. | core, infrastructure |
| **ui (main.py)** | Punto de entrada. Delega todo al application layer. | application |

---

## Los Tres Motores

### 1. 🚀 Motor de Interop COM (Edición en Vivo)

**Propósito:** Modificar constantes de dimensionamiento (`N_MAX_*`) directamente en la RAM del PLC sin exportar/importar.

**Implementación:**
```python
# infrastructure/tia_service.py
def actualizar_constantes_proceso(self, plc_name, nombre_tabla, constantes_dict):
    plc = self._get_plc(plc_name)
    tablas = plc.get_plc_tag_tables(folder_path="003_Proceso")
    tabla = next(t for t in tablas if t.get_property(name="Name") == nombre_tabla)
    user_constants = tabla.get_user_constants()
    
    for constante in user_constants:
        nombre_const = constante.get_property(name="Name")
        if nombre_const in constantes_dict:
            constante.set_property(name="Value", value=str(constantes_dict[nombre_const]))
```

**Ventajas:**
- ⚡ **Velocidad**: Modificación instantánea vs. exportar/editar/importar (minutos → segundos)
- 🔒 **Seguridad**: Cambios en vivo sin alterar la estructura del proyecto
- ♻️ **Reutilizable**: Aplicable a cualquier proceso sin regenerar código

---

### 2. 🔧 Motor de Compilación Inteligente

**Propósito:** Redimensionar los arrays de los DataBlocks después de cambiar las constantes N_MAX.

**Flujo crítico:**
```
1. COM: Actualizar constantes N_MAX
   ↓
2. COMPILAR: Forzar a TIA Portal a recalcular tamaños de arrays
   ↓
3. CACHE: Reconstruir mapa de memoria (build_cache) para reflejar cambios
```

**Prevención de crasheos:**
```python
# El script reconstruye el caché tras cada compilación
if cambios_constantes:
    tia.compilar_software(plc_name)
    tia.clear_cache()
    tia.build_cache(plc_name)  # ← Crítico: evita inconsistencias
```

**Lógica del Radar Anti-Colisiones:**
- Calcula bloques predichos: `{tipo}{nuevo_numero}_{codigo}`
- Compara con caché existente (case-insensitive)
- Detecta colisiones por nombre o número antes de inyectar

---

### 3. 📄 Motor de Inyección XML

**Propósito:** Exportar → Mutear → Reimportar bloques PLC preservando la estructura.

**Flujo detallado:**
```
┌─────────────────────────────────────────────────────────────┐
│ EXPORTAR                                                     │
│   tia.exportar_bloque(plc, db_name, .build/temp/)           │
│   → Archivo XML con formato Siemens                          │
├─────────────────────────────────────────────────────────────┤
│ MUTAR                                                        │
│   xml_modifier.inject_textos(xml_path, datos_excel)          │
│   → Inyectar comentarios/descripciones sin tocar estructura │
├─────────────────────────────────────────────────────────────┤
│ IMPORTAR                                                     │
│   tia.importar_bloque_override(plc, xml_mutado, carpeta)     │
│   → Preserva rutas originales en el árbol de TIA Portal     │
└─────────────────────────────────────────────────────────────┘
```

**Compilación Post-Inyección:**
- Pre-Check: Verifica si el bloque necesita compilación (`is_consistent()`)
- Importar
- Post-Check: Compila solo si hay errores pendientes

---

## UX / UI - Interfaz de Terminal Interactiva (TUI)

### Tecnologías

| Librería | Uso |
|----------|-----|
| **questionary** | Menús interactivos, confirmaciones, inputs |
| **rich** | Tablas, reglas, emojis, colores (console.print) |
| **tkinter** | Diálogos nativos de selección de archivos/carpetas |
| **logging** | Logging estructurado a archivo (nunca a stdout) |

### Context Manager: `silenciar_ruido()`

**Problema:** El wrapper nativo de Siemens (`Siemens.TiaPortal.OpennessApi18...`) escribe logs crusados a stdout durante transacciones pesadas, rompiendo la experiencia TUI.

**Solución:** Administrador de contexto que desvía la salida a un archivo `.log` durante operaciones críticas:

```python
# infrastructure/tia_service.py
@contextmanager
def silenciar_ruido(self):
    log_path = str(Path(".build/tia_wrapper_native.log").absolute())
    Path(".build").mkdir(exist_ok=True)
    
    ts.set_logging(path=log_path, console=False)  # Apagar consola
    try:
        yield
    finally:
        ts.set_logging(path=log_path, console=True)  # Restaurar
```

**Uso en automation_flow.py:**
```python
# Conexión (Attach)
with tia.silenciar_ruido():
    tia.build_cache(plc_name)

# Compilación
with tia.silenciar_ruido():
    tia.compilar_software(plc_name)
    tia.clear_cache()
    tia.build_cache(plc_name)

# Sincronización masiva
with tia.silenciar_ruido():
    resultados = uc_sync.sincronizar_multiple_db(plc_name, tareas)

# Desconexión (Detach)
ts.set_logging(path=log_path, console=False)  # En _detach()
    self._portal.detach()
ts.set_logging(path=log_path, console=True)
```

**Resultado:** Terminal 100% limpia durante toda la sesión, logs guardados en `.build/tia_wrapper_native.log`.

---

## Flujos Principales

### 🔄 Flujo 1: Generación de Procesos

```
┌──────────────────────────────────────────────────────────────────────────┐
│ GENERAR PROCESO                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  [1] Seleccionar Proceso Destino                                         │
│      └── UID + Nombre del proceso a generar                              │
│                                                                          │
│  [2] Seleccionar Plantilla XML                                          │
│      └── Plantilla base (carpeta en ruta configurada)                    │
│                                                                          │
│  [3] Pre-flight (Radar Anti-Colisiones)                                 │
│      └── Calcular bloques predichos → Detectar colisiones                │
│      └── Mostrar resumen: origen, destino, PLC, bloques                  │
│      └── Si hay colisiones → ABORTAR                                     │
│                                                                          │
│  [4] Confirmar → Generar y Exportar                                     │
│      └── XMLs mutados en .build/                                        │
│                                                                          │
│  [5] ¿Inyectar en PLC? (Confirmación)                                    │
│      └── NO → Puedes importar .build/ manualmente                       │
│      └── SÍ → Continuar                                                 │
│                                                                          │
│  [6] Inyectar código base + Compilar (Pre/Post-Check)                   │
│      └── silenciar_ruido(): importar_proyecto()                         │
│      └── build_cache(force=True)                                         │
│                                                                          │
│  [7] COM: Actualizar Constantes N_MAX                                    │
│      └── {uid}_N_MAX_PREAL, _PINT, _ALARMAS, _ALARMAS_HMI               │
│      └── silenciar_ruido(): actualizar_constantes_proceso()              │
│                                                                          │
│  [8] Compilar para Redimensionar                                         │
│      └── silenciar_ruido(): compilar_software()                          │
│      └── clear_cache() + build_cache()                                   │
│                                                                          │
│  [9] COM: Sincronizar Textos (Export/Mutate/Import)                       │
│      └── DB3100_{codigo}_PREAL                                           │
│      └── DB3101_{codigo}_PINT                                            │
│      └── DB5000_{codigo}_ALM                                             │
│      └── silenciar_ruido(): sincronizar_multiple_db()                    │
│                                                                          │
│  [10] Resumen Final                                                      │
│      └── tabla con resultados: ✅/❌ por DB                              │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

### 🔧 Flujo 2: Sincronización de Parámetros y Alarmas

```
┌──────────────────────────────────────────────────────────────────────────┐
│ SINCRONIZAR PARÁMETROS Y ALARMAS                                         │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  [1] Seleccionar Proceso                                                 │
│      └── Filtrar datos en memoria: PReal, PInt, Alarmas                 │
│                                                                          │
│  [2] Batching: Armar lista de tareas                                      │
│      └── DB3100_{codigo}_PREAL: items + getter functions                 │
│      └── DB3101_{codigo}_PINT: items + getter functions                 │
│      └── DB5000_{codigo}_ALM: items + getter functions                   │
│                                                                          │
│  [3] COM: Actualizar Constantes N_MAX (1/4)                              │
│      └── silenciar_ruido(): actualizar_constantes_proceso()              │
│                                                                          │
│  [4] Compilar Redimensionamiento (2/4)                                   │
│      └── silenciar_ruido(): compilar_software()                          │
│      └── clear_cache() + build_cache()                                   │
│                                                                          │
│  [5] Inyección Masiva de Textos (3/4)                                    │
│      └── Export → Mutate → Import para cada DB                          │
│      └── silenciar_ruido(): sincronizar_multiple_db()                   │
│                                                                          │
│  [6] Reconstrucción Caché (4/4)                                          │
│      └── silenciar_ruido(): build_cache(force=True)                     │
│                                                                          │
│  [7] Resumen Final                                                      │
│      └── X exitosos, Y fallidos                                          │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Requisitos y Uso

### Librerías Clave

| Librería | Versión | Propósito |
|----------|---------|-----------|
| `siemens_tia_scripting` | 18.x | Wrapper Python para Openness API |
| `pandas` / `openpyxl` | latest | Lectura de archivos Excel |
| `questionary` | latest | Interfaz TUI interactiva |
| `rich` | latest | Console coloring y tablas |
| `psutil` | latest | Detección de procesos TIA Portal |

### Requisitos del Sistema

- **TIA Portal** instalado y ejecutándose con un proyecto abierto
- **Permisos de Openness** habilitados en TIA Portal
- **Python 3.10+** con las dependencias instaladas

### Iniciar la Aplicación

```bash
# Desde el directorio raíz del proyecto
python main.py
```

### Estructura del Excel Maestro

El archivo Excel debe contener las siguientes hojas:

| Hoja | Contenido |
|------|-----------|
| `Procesos` | UID, Nombre, Código, Etapas, PReal, PInt, Alarmas |
| `Tabla_PReal` | UID, Número, Proceso, DB, Producto, Tipo, Descripción, Comentario_DB, Visibilidad |
| `Tabla_PInt` | UID, Número, Proceso, DB, Producto, Tipo, Descripción, Comentario_DB, Visibilidad |
| `Tabla_Alarmas` | UID, Número, Proceso, DB, Descripción, Comentario_DB |

---

## Menú Principal (TUI)

```
═══════════════════════════════════════════════════════════════════════
              MENÚ PRINCIPAL | Proyecto: {name} | PLC: {plc}
═══════════════════════════════════════════════════════════════════════

  ⚡ Generar Procesos
  🔄 Sincronizar Parámetros y Alarmas

  ───────────────────────────────────────────────────────────────────

  🔌 Cambiar PLC objetivo
  📡 Forzar escaneo completo del PLC
  📊 Recargar datos del Excel Maestro
  📂 Configurar Ruta de Plantillas

  ───────────────────────────────────────────────────────────────────

  ❌ Salir

═══════════════════════════════════════════════════════════════════════
```

---

## Configuración

La ruta de plantillas se almacena en `config.json` (persistente entre sesiones):

```json
{
  "template_path": "C:/Zeus Control/Plantillas"
}
```

---

## Estructura de Archivos Generados

```
.build/
├── tia_wrapper_native.log     # Logs crusados del wrapper de Siemens
├── temp/                      # Exportaciones temporales XML
├── sincronizado/              # XMLs mutados listos para importar
└── {proceso}_{uid}/           # Bloques generados para un proceso
    ├── DB3100_CPR_PREAL.xml
    ├── DB3101_CPR_PINT.xml
    └── DB5000_CPR_ALM.xml
```

---

## Licencia y Autores

**ZC ALM TOOLS** - Python Edition  
Desarrollado con Siemens TIA Portal Openness API

> ⚠️ **Nota:** Esta herramienta manipula directamente la configuración del PLC. Úsela con precaución y siempre realice backup de sus proyectos antes de ejecutar operaciones masivas.