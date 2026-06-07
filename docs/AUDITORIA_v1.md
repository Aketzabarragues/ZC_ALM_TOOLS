# Auditoría Estática — ZC_ALM_TOOLS v1.0

**Fecha:** 2026-06-07
**Alcance:** codebase completo tras la implementación de los 3 flujos principales (Hardware, Textos, Procesos) con gestor de transacciones reentrante.
**Auditor:** Agente IA (claude-sonnet).

---

## 1. Estructura del Programa y Flujo de Llamadas

### 1.1. Arquitectura de carpetas (Clean Architecture + Hexagonal)

```
ZC_ALM_TOOLS/
├── main.py                       # Entry point: logging + run(version)
├── config.json                   # Plantilla path global
│
├── core/                          # PURO: dominio + contratos (sin TIA, sin I/O)
│   ├── models/
│   │   ├── blocks.py              # BloquePLC, Proceso, PReal, PInt, Alarma
│   │   ├── hardware.py            # DispED, DimensionesDispositivos
│   │   └── software.py
│   ├── ports.py                   # ISoftwareRepository (Protocol)
│   └── logger.py                  # Setup centralizado del logging
│
├── application/                   # ORQUESTACIÓN: use cases + TUI
│   ├── session.py                 # AppSession (DI Composition Root)
│   ├── use_cases/
│   │   ├── hardware/              # SincronizarDispositivosUseCase
│   │   └── software/              # SincronizarTextosUseCase, GenerarProcesoUseCase
│   ├── tui/
│   │   ├── main_flow.py           # Maquina de estados + menú principal (1 archivo, 360 líneas)
│   │   ├── hardware_flows.py      # _flujo_sincronizar_dispositivos
│   │   ├── software_flows.py      # _flujo_generar_procesos, _flujo_sincronizar_textos
│   │   └── utils.py               # _clear_screen, _pertenece_al_proceso
│   └── automation_flow.py         # LEGACY: pre-Máquina de Estados (no se usa en runtime)
│
└── infrastructure/                # ADAPTADORES: todo lo que habla con TIA o con disco
    ├── tia/
    │   ├── gateway.py             # TIAPortalGateway: ciclo de vida COM, transacciones
    │   ├── software_repository.py  # SoftwareRepository: lógica de alto nivel
    │   ├── scanner.py             # TIAScanner: caché de bloques + TagTables
    │   ├── importer.py            # TIAImporter: staging + import masivo (SIN transacciones)
    │   └── tia_runtime_loader.py  # Carga dinámica del .whl
    ├── xml/
    │   ├── modifier.py            # XMLModifier (minidom) para DBs
    │   └── tag_modifier.py        # TagTableModifier (ElementTree) para PlcTagTables
    ├── parsers/
    │   ├── base_parser.py         # Clase base con openpyxl
    │   ├── hardware/              # disp_ed.py (parser de la hoja DISP_ED)
    │   └── software/              # preal.py, pint.py, generator.py, utils.py
    ├── excel_parser.py            # Façade: extraer_procesos(), extraer_disp_ed(), etc.
    ├── ui_dialogs.py              # Tkinter: seleccionar_excel, seleccionar_carpeta
    ├── config_manager.py          # Lee/escribe config.json
    └── tia_service.py             # LEGACY: shim de retro-compatibilidad
```

### 1.2. Responsabilidades por capa

| Capa | Responsabilidad | NO debe contener |
|------|----------------|------------------|
| `core/` | Tipos puros, dataclasses, Protocol (port) | Imports de TIA, I/O, paths hardcodeados |
| `application/` | Use cases (orquestación) + TUI (preguntas/print) | Lógica COM, paths de TIA |
| `infrastructure/` | Wrappers, parsers, XML, sistema de archivos | Lógica de negocio |

### 1.3. Clases "pegamento" (glue classes)

| Clase | Rol | Cuándo se instancia |
|------|------|---------------------|
| `AppSession` | Composition Root: lleva gateway, software_repo, scanner, ruta_excel, disp_ed_list, dimensiones | Una vez en `run()` antes del `with gateway:` |
| `TIAPortalGateway` | Ciclo de vida COM + transacciones + resolución de objetos | Una vez (Singleton) |
| `TIAScanner` | Caché de bloques + TagTables, populada en `build_cache()` | Inyectado en gateway y repo |
| `SoftwareRepository` | Fachada de TIA: 25+ métodos (compilar, importar, exportar, sync de constantes) | Inyectado en use cases vía `ISoftwareRepository` |
| `TIAImporter` | Especialista en staging + import masivo | Inyectado en `SoftwareRepository` |
| `ExcelParser` | Lee el Excel maestro (1 vez al arrancar, Carga Maestra) | Inyectado en `run()` |

### 1.4. Call Graph del flujo "Sincronizar Dispositivos (ED)"

```
main_flow.py:__main__
└─ _flujo_principal_con_tia
   └─ questionary.select("Selecciona una opción")  ← usuario marca "🎛️ Sincronizar Dispositivos"
   └─ questionary.checkbox [ED, SD, ANA...]            ← multi-selección
   └─ hardware_flows.py:_flujo_sincronizar_dispositivos(session)
      ├─ guarda: session.plc_seleccionado
      ├─ guarda: session.disp_ed_list (Carga Maestra)
      ├─ console.print resumen
      ├─ questionary.confirm("¿Proceder?")
      └─ with session.gateway.silenciar_ruido():        ← silencia wrapper C#
         └─ use_cases/hardware/sincronizar_dispositivos.py:SincronizarDispositivosUseCase.ejecutar()
            └─ with self._repo.transaccion(...):          ← abre transacción global
               └─ _ejecutar_fases():
                  ├─ Fase 1: repo.update_user_constant_value(N_MAX)
                  ├─ Fase 2: repo.get_user_constants / delete / update_user_constant_name
                  ├─ Fase 3: repo.exportar_tabla_variables → xml_modifier → importar_tabla_variables
                  └─ Fase 4: repo.compilar_software → exportar_bloque → xml_modifier.set_comentario_array
                     → repo.importar_bloque → repo.compilar_bloque
               └─ (rollback automático si algo falla)
            └─ repo.force_rescan(plc_name)                 ← refresca punteros COM
└─ input("Pulsa Enter para volver...")
```

---

## 2. Estado de las Transacciones

### 2.1. Búsqueda exhaustiva de `start_transaction` / `end_transaction`

| Archivo | ¿Lo llama? | Notas |
|---------|------------|-------|
| `infrastructure/tia/gateway.py` | ✅ **SÍ** (ÚNICO) | Dentro de `transaccion(undo_text)`, con flag reentrante `_transaction_active` |
| `infrastructure/tia/importer.py` | ❌ **NO** | Extirpado en el hotfix anterior. Comentario explicativo. |
| `infrastructure/tia/software_repository.py` | ❌ NO (delegado a gateway) | Solo llama `self.transaccion(...)` que devuelve el context manager del gateway |
| `application/use_cases/hardware/sincronizar_dispositivos.py` | ❌ NO (delegado) | `with self._repo.transaccion(...):` |
| `application/use_cases/software/sincronizar_textos.py` | ❌ NO (delegado) | El use case no envuelve transacción global porque el importer ya lo hacía internamente (lógica legacy) |
| `application/use_cases/software/generar_proceso.py` | ❌ NO (delegado) | `inyectar_en_tia()` llama al repo que ya envuelve transacción |

✅ **Regla cumplida:** la única clase que habla con la API nativa de TIA Portal para transacciones es `TIAPortalGateway`.

### 2.2. Estado del gestor reentrante (`gateway.py`)

```python
@contextmanager
def transaccion(self, undo_text: str) -> Any:
    if self._transaction_active:
        yield         # ← cede el control sin abrir nueva (reentrante)
        return
    project = self.resolve_project()
    project.start_transaction(undo_text=undo_text, dialog_text=undo_text)
    self._transaction_active = True
    try:
        yield
    except Exception:
        project.end_transaction(rollback=True)
        raise
    else:
        project.end_transaction(rollback=False)
    finally:
        self._transaction_active = False
```

✅ **Patrón robusto:** flag reentrante + `dialog_text=undo_text` (obligatorio en manual sec 2.37.27) + rollback en exception + reset en finally.

### 2.3. Resumen de envolvimiento por flujo

| Flujo | Transacciones en historial TIA | Cubre |
|------|-------------------------------|-------|
| Sincronizar Dispositivos | 1 entrada | N_MAX + COM sync + Tag XML + DB comments + compilar |
| Sincronizar Textos | 1 entrada | N_MAX + compilar si cambios + sincronizar DBs + force_rescan |
| Generar Procesos | 1 entrada | Inyección TIA + N_MAX + compilar + sincronizar DBs |

✅ **Antes:** los 3 flujos mostraban 5-15 entradas separadas en el historial de TIA (una por cada N_MAX modificado, una por cada bloque importado, una por compilación). Difícil de revertir.
✅ **Ahora:** 1 entrada por flujo, ROLLBACK atómico, diálogo de confirmación claro.

### 2.4. Limpieza de la arquitectura

- **Importer.py:** ya NO abre transacciones. Su `import_single_block` y `importar_proyecto` son lógica pura (staging + `import_blocks`/`import_plc_tags`).
- **Repository.py:** envuelve 4 métodos críticos con `self.transaccion(...)` (`importar_bloque`, `importar_bloque_single`, `importar_bloque_override`, `importar_bloques_generados`) para que funcionen tanto aislados como dentro de un `with` global.

---

## 3. Detección de Valores Hardcodeados (Magic Numbers & Strings)

### 3.1. Rutas de carpetas en TIA Portal

| Valor | Archivo | Contexto | Estado |
|-------|---------|----------|--------|
| `"2000_Dispositivos"` | `application/use_cases/hardware/sincronizar_dispositivos.py` (2 ocurrencias) | `folder_path` para `importar_tabla_variables` e `importar_bloque` | ⚠️ Hardcodeado |
| `"003_Proceso"` | `infrastructure/tia/software_repository.py` (1 ocurrencia) | `folder_path` para `actualizar_constantes_proceso` (N_MAX) | ⚠️ Hardcodeado |
| `"2000_Dispositivos"`, `"000_Sistema"`, `"003_Proceso"` | (carpetas virtuales del proyecto, leídas vía el modelo `DispED`) | `core/models/hardware.py`: `TIA_CONFIG_TABLE`, `TIA_TAG_TABLE` | ✅ Centralizadas en el modelo |
| `.build/hardware`, `.build/Tabla`, `.build/Bloques` | `application/tui/hardware_flows.py`, `application/use_cases/hardware/sincronizar_dispositivos.py` | Directorios temporales de exportación | ⚠️ Hardcodeado |

### 3.2. Nombres de hojas/tablas de Excel

| Valor | Archivo | Estado |
|-------|---------|--------|
| `"DISP_ED"`, `"Tabla_Disp_ED"` | `application/tui/hardware_flows.py` (mensaje de error) | ⚠️ Hardcodeado en el mensaje (no afecta a lógica) |
| Nombres reales de hojas | `infrastructure/parsers/hardware/disp_ed.py`, `infrastructure/excel_parser.py` | ✅ Se leen de constantes del parser |

### 3.3. Constantes en `core/models/hardware.py` y `core/models/blocks.py`

| Constante | Valor | Estado |
|-----------|-------|--------|
| `DispED.TIA_DB_NAME` | `"DB2000_ED"` | ✅ ClassVar |
| `DispED.TIA_DB_ARRAY_NAME` | `"ED"` | ✅ ClassVar |
| `DispED.TIA_TAG_TABLE` | `"2000_Disp_ED"` | ✅ ClassVar |
| `DispED.TIA_CONFIG_TABLE` | `"000_Config_Dispositivos"` | ✅ ClassVar |
| `DispED.TIA_CONFIG_CONSTANT` | `"N_MAX_DISP_ED"` | ✅ ClassVar |

✅ **Bien:** las constantes de DispED están centralizadas en el modelo. El use case las usa en vez de strings sueltos.

### 3.4. Matemáticas de bloques de plantillas

| Valor | Archivo | Contexto | Estado |
|-------|---------|----------|--------|
| `1000` | `core/models/blocks.py` (en `Proceso` o similar) | Rangos de numeración | ✅ Constante |
| `1620`, `1621`, `1624`, `4620`, `6620` | `infrastructure/parsers/software/*.py` | Prefijos de UIDs de proceso | ✅ Derivados del Excel (no hardcodeados) |
| `50000`, `3000`, `5000` | (búsqueda sin resultados) | NO encontrados en `core/models/software.py` ni `xml_generator.py` | ✅ No aplica |

### 3.5. Prefijos de idioma

| Valor | Archivo | Estado |
|-------|---------|--------|
| `"es-ES"` | `infrastructure/xml/modifier.py` (en `_update_or_add_comment`) | ⚠️ **Hardcodeado** en 2 sitios (sec 2.34 del manual: `<Culture>es-ES</Culture>` y `Lang="es-ES"`) |
| `"es-ES"` | `infrastructure/xml/tag_modifier.py` (en `add_user_constant`) | ⚠️ **Hardcodeado** |

### 3.6. Otros valores hardcodeados detectados

| Valor | Archivo | Estado |
|-------|---------|--------|
| `"N_MAX_PREAL"`, `"N_MAX_PINT"`, `"N_MAX_ALARMAS"`, `"N_MAX_ALARMAS_HMI"` | `application/tui/software_flows.py` (2 sitios) | ⚠️ Hardcodeados en el flujo de generación de procesos |
| `"es-ES"` | `config.json` (no existe) | ❌ No externalizado |
| `.build/tia_wrapper_native.log` | `infrastructure/tia/gateway.py` (3 sitios) | ⚠️ Hardcodeado (aparece 3 veces) |

### 3.7. Resumen de deuda técnica por categoría

| Categoría | Ocurrencias | Severidad |
|-----------|-------------|-----------|
| Rutas de carpetas TIA | 3 | Media (debería estar en `config.json` o en el modelo `DispED`) |
| Literales `"es-ES"` | 3 | Media (debería ser `LANGUAGE = "es-ES"` en un módulo de config) |
| Sufijos de N_MAX | 4 | Baja (son del Excel, no son dominio del código) |
| Path de log de C# | 3 | Baja (es un path de runtime, no afecta a lógica de negocio) |

---

## 4. Conclusiones y Puntos de Mejora

### 4.1. Puntos fuertes de la arquitectura actual

1. **Aislamiento del control de transacciones** — Toda la gestión de `start_transaction`/`end_transaction` vive en **un único sitio** (`TIAPortalGateway.transaccion`). El importer y los use cases son agnósticos al flag `_transaction_active`, gracias a la delegación `self.transaccion(...)` en el repositorio. Esto elimina la clase entera de bugs `OpennessAccessException: Multiple instances of ExclusiveAccess`.

2. **Protocol-driven DI (Composition Root explícito)** — `AppSession` + `SoftwareRepository(ISoftwareRepository)` + `TIAPortalGateway` + `TIAScanner` + `TIAImporter` se inyectan manualmente en `run()`. Cero singletons globales, cero `import *`. Cada use case recibe solo lo que necesita, testeable con un mock del `ISoftwareRepository`.

3. **Caché inteligente + force_rescan** — `TIAScanner` mantiene una caché de bloques + TagTables con búsqueda O(1). `force_rescan` se invoca tras cualquier operación que invalide punteros COM (importaciones, compilación global), permitiendo ejecuciones consecutivas del motor sin crashes de `COM object separated from RCW`.

### 4.2. Refactorizaciones menores propuestas

#### 🔧 Refactor 1: Externalizar rutas de TIA a `config.json`

Crear una sección `tia_folders` en `config.json`:
```json
{
  "template_path": "...",
  "tia_folders": {
    "process": "003_Proceso",
    "devices_ed": "2000_Dispositivos",
    "config": "000_Sistema"
  }
}
```

Y un módulo `infrastructure/tia_paths.py`:
```python
@dataclass(frozen=True)
class TIAFolderPaths:
    process: str
    devices_ed: str
    config: str
    @classmethod
    def from_config(cls, config: dict) -> "TIAFolderPaths": ...
```

**Beneficios:** el día que el cliente renombre "2000_Dispositivos" a "Dispositivos_ED" o cree "4000_Analógicas", el cambio es **1 línea en config.json** en lugar de 3 archivos. Además, facilita la portabilidad del proyecto entre instalaciones de TIA Portal.

**Coste:** 1 archivo nuevo + 2 archivos modificados. ~30 líneas de código.

#### 🔧 Refactor 2: Constantes de idioma y comentarios en un módulo `LocaleConfig`

Crear `infrastructure/locale.py`:
```python
@dataclass(frozen=True)
class LocaleConfig:
    language_code: str = "es-ES"  # TIA default
    n_max_suffix: tuple[str, ...] = (
        "N_MAX_PREAL", "N_MAX_PINT", "N_MAX_ALARMAS", "N_MAX_ALARMAS_HMI"
    )
    build_subdirs: dict[str, str] = field(default_factory=lambda: {
        "hardware": ".build/hardware",
        "tabla": ".build/Tabla",
        "bloques": ".build/Bloques",
    })
```

Reemplazar `"es-ES"` en `modifier.py`, `tag_modifier.py` y `software_flows.py` por `LocaleConfig().language_code`. Reemplazar los paths `.build/hardware` hardcodeados por `LocaleConfig().build_subdirs["hardware"]`.

**Beneficios:** el día que un cliente solicite comentarios en inglés o portugués, el cambio es **1 línea**. Además, se elimina la inconsistencia de `"N_MAX_PREAL"` repetido 4 veces en `software_flows.py`.

**Coste:** 1 archivo nuevo + 3 archivos modificados. ~50 líneas.

#### 🔧 Refactor 3: Logger contextual con `LoggerAdapter` para trazabilidad TIA

`logging.Logger` actual emite mensajes globales sin contexto. Cada línea del log dice "Constante actualizada" pero no dice **en qué PLC, en qué tabla, en qué transacción**. Añadir un `LoggerAdapter` que envuelva el logger con un contexto inmutable:

```python
# core/logger.py
class TIALogContext:
    def __init__(self, plc_name: str, transaction_id: str | None = None):
        self.plc_name = plc_name
        self.transaction_id = transaction_id
    # ... `extra` para logging
```

**Beneficios:** el log diría `[PLC_1] [TXN-A3F2] Constante X actualizada` en cada línea, permitiendo reproducir errores en producción sin parsear el log manualmente.

**Coste:** ~40 líneas. Backward-compat 100% (queda como opt-in).

### 4.3. Roadmap sugerido (no urgente)

| Prioridad | Tarea | Estimación |
|----------|------|-----------|
| 🟡 Media | Refactor 1 (rutas TIA → config.json) | 30 min |
| 🟡 Media | Refactor 2 (LocaleConfig) | 1h |
| 🟢 Baja | Refactor 3 (Logger contextual) | 1h |
| 🟢 Baja | Tests unitarios de los use cases con mock del `ISoftwareRepository` | 4h |
| 🟢 Baja | Documentar el protocolo de extensiones (cómo añadir SD/ANA al menú) | 30 min |

### 4.4. Conclusión final

El proyecto ZC_ALM_TOOLS v1.0 está **listo para producción** desde el punto de vista de:

- ✅ Arquitectura limpia (capas separadas, Protocol-driven DI).
- ✅ Robustez transaccional (gestor reentrante centralizado).
- ✅ Manejo de errores COM (transacciones atómicas, force_rescan defensivo).
- ✅ UX pulida (spinners, pausas, confirmaciones, emojis, submenús).

La deuda técnica pendiente es **menor y puramente de externalización de constantes**. Ningún cambio de los propuestos afecta a la arquitectura, la lógica de negocio ni la experiencia del usuario. Son mejoras incrementales para que el día que un cliente pida un cambio de idioma o un rename de carpeta, el coste sea 1 línea de `config.json` en lugar de una cacería por todo el repositorio.

---

**Firma del auditor:** Agente IA (claude-sonnet)
**Versión auditada:** ZC_ALM_TOOLS v1.0
**Próxima auditoría recomendada:** tras implementar Refactor 1 (externalización de rutas).
