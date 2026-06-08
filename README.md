🚀 ZC_ALM_TOOLS
ZEUS CONTROL Application Lifecycle Management Tools — La navaja suiza de la automatización de TIA Portal.

🚀 ¿Qué es ZC_ALM_TOOLS?
ZC_ALM_TOOLS es una aplicación de consola interactiva (TUI) que lee un Excel Maestro (el documento de ingeniería de tu proyecto) y automatiza la creación, configuración e inyección de código directamente en Siemens TIA Portal de forma segura y masiva.

En lugar de hacer clic manualmente durante horas para crear 50 procesos industriales o sincronizar 300 entradas digitales, este programa lo hace por ti en segundos. Funciona clonando plantillas XML, sustituyendo variables matemáticamente y enviándolo todo a TIA Portal en una sola transacción. Si algo falla, hace un rollback automático (deshace los cambios) para que tu PLC nunca quede corrupto o a medias.

El programa divide el mundo en dos:

Software: Lógica de procesos, parámetros (reales y enteros) y alarmas.

Hardware: Dispositivos físicos cableados (Entradas Digitales, Analógicas, etc.).

🏗️ La Arquitectura "Para Dummies"
(Cómo está organizado el código)

Imagina que el programa es un restaurante de alta cocina de 3 plantas. Las plantas están muy separadas y tienen reglas estrictas:

🥘 core/ — La Receta (Dominio Puro)
Esta es la planta donde está la receta secreta. Aquí viven los datos puros: qué es un Proceso, qué es un DispED (Entrada Digital). Esta planta NO sabe nada de TIA Portal, ni de Excel, ni de Windows.

Regla de oro: Si estás aquí y ves un import siemens o import pandas, ¡está mal! El dominio es independiente.

🤵 application/ — El Camarero (Casos de Uso y TUI)
Aquí está el personal que atiende al cliente (los menús interactivos en pantalla). El camarero toma tu pedido y coordina a la cocina, pero no cocina él mismo. Habla con la cocina a través de un "contrato" o interfaz.

El Ticket: Usa un objeto llamado AppSession que guarda todo lo que leyó del Excel al principio, para no tener que volver a leer el archivo cada vez que pulsas un botón.

👨‍🍳 infrastructure/ — La Cocina (TIA Portal, Excel, XML)
Aquí es donde nos manchamos las manos de grasa. Esta capa lee el Excel físico, edita los archivos XML y se pelea con la API de Siemens (siemens_tia_scripting).

Regla de oro: El archivo tia/gateway.py es el ÚNICO autorizado a importar el wrapper de Siemens.

El flujo de dependencias siempre es hacia la izquierda:
core/ ⬅️ application/ ⬅️ infrastructure/

🔄 Flujos de Trabajo: ¿Qué pasa bajo el capó?
Cuando pulsas un botón en el menú, esto es lo que ocurre internamente:

⚡ Botón: "Generar Procesos"
Plaintext
Usuario elige Generar Proceso
  ↳ TUI pregunta plantilla y destino
    ↳ Use Case lee la plantilla XML y busca colisiones en el PLC
      ↳ Generador clona el XML y cambia UIDs (100 -> 200)
        ↳ Repositorio abre transacción en TIA Portal
          ↳ Importer inyecta los bloques
            ↳ ✅ COMMIT (Guarda) o ❌ ROLLBACK (Deshace)
🎛️ Botón: "Sincronizar Dispositivos (ED)"
Plaintext
Usuario elige Sincronizar ED
  ↳ Use Case lee la lista de EDs en memoria (AppSession)
    ↳ 1. Actualiza el valor de N_MAX (Dimensión)
    ↳ 2. Borra/Renombra variables obsoletas vía COM
    ↳ 3. Exporta tabla XML, añade las nuevas ED y reimporta
    ↳ 4. Redimensiona y actualiza los comentarios del DataBlock
      ↳ ✅ Todo en 1 solo "clic" para el historial de TIA
🎛️ GUÍA: Cómo añadir un nuevo tipo de Hardware (Ej: Entradas Analógicas - EA)
Gracias a la magia del código (polimorfismo), no tienes que tocar la lógica compleja para añadir hardware nuevo. Solo sigue estos 4 pasos "para tontos":

Paso 1: Configurar los nombres (1 línea)
Abre config.json en la raíz y dile al programa cómo se llamarán los bloques en TIA Portal:

JSON
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
Paso 2: Crear el modelo de datos
En core/models/hardware.py, crea la clase DispEA. Solo debe tener estos 4 campos obligatorios (el resto pon los que quieras):

Python
@dataclass
class DispEA:
    numero: int = 0
    plc_tag: str = ""
    plc_comentario: str = ""
    descripcion: str = ""
    # Tus campos extra:
    rango_min: float = 0.0
    rango_max: float = 10.0
Paso 3: Leer el Excel
Crea un parser en infrastructure/parsers/hardware/disp_ea.py para leer tu nueva hoja del Excel. Añade la lista resultante (disp_ea_list) a AppSession en application/session.py y cárgala en main.py.

Paso 4: Añadir al menú
En application/tui/main_flow.py, añade la opción al menú de casillas:

Python
Choice(" Entradas Analógicas (EA)", value="EA"),
Y abajo, llámalo pasando tu nueva lista:

Python
if "EA" in tipos_seleccionados:
    _flujo_sincronizar_dispositivos(hw_type="ea", dispositivos=session.disp_ea_list, ...)
¡Y LISTO! 🎉 El sistema ya sabe cómo sincronizar tus EA sin tocar nada más.

🧩 GUÍA: Cómo añadir nuevos Procesos de Software
El sistema lee el Excel a través de una "fachada" llamada ExcelParser. Lee todo una vez al arrancar para que el programa vuele.

Si mañana añades un nuevo DataBlock a tus procesos (ejemplo: Recetas), solo tienes que hacer esto para que se sincronice:

Añade la propiedad del nombre del DB en core/models/software.py (ej: db_recetas_nombre).

Ve a application/tui/software_flows.py, busca la función _build_tareas() y añade una sola línea a la lista db_configs:

Python
def _build_tareas(...):
    db_configs = [
        (proceso.db_preal_nombre, "PReal", preal_list, True),
        # ...
        (proceso.db_recetas_nombre, "RECETA", recetas_list, False), # <-- NUEVO
    ]
El motor leerá esta "Tarea de Sincronización" y hará todo el trabajo (exportar, mutar XML, importar) él solito.

📦 Compilación y Ejecución
Prerrequisitos
Python 3.12+ instalado.

TIA Portal V15.1 o superior.

El archivo .whl de Siemens (siemens_tia_scripting).

Instalación para Desarrollo
Instala las dependencias: pip install -r requirements.txt.

Ejecuta el programa: python main.py.

🏗️ Crear el Ejecutable (.exe) para Producción
Para dárselo a un cliente o ingeniero que no tiene Python instalado, hemos creado un script mágico:

Bash
python build_exe.py
Esto empaquetará todo (incluyendo el wrapper de Siemens) en un único archivo ZC_ALM_TOOLS.exe dentro de la carpeta dist/. ¡Doble clic y a funcionar!

📜 Reglas de Oro para Desarrolladores
Tipado estricto: Usa anotaciones de tipo siempre (-> str, : int).

Cero diccionarios sueltos: No uses dict para pasar datos entre capas, usa @dataclass.

Cero strings mágicos: No escribas rutas como "003_Proceso" en el código. Ponlas en config.json y léelas con config_manager.

Si tocas código, compila: Ejecuta python -m compileall core application infrastructure main.py para asegurarte de que no has roto nada.
