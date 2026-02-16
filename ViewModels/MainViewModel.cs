using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{
    /// <summary>
    /// ViewModel principal de la aplicación.
    /// Gestiona la conexión con TIA Portal, la orquestación de la extracción de datos desde Excel 
    /// mediante Python y la carga dinámica de modelos de dispositivos.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        // =================================================================================================================
        // 1. PROPIEDADES Y SERVICIOS

        /// <summary>Servicio de abstracción para operaciones con la API de Siemens Openness.</summary>
        private TiaService _tiaService;

        /// <summary>Instancia del proceso TIA Portal abierto.</summary>
        private TiaPortal _tiaPortal;

        /// <summary>Referencia al software del PLC seleccionado en el proyecto.</summary>
        private PlcSoftware _plcSoftware;

        /// <summary>Diccionario que actúa como caché de ingeniería (Key: Nombre de categoría, Value: Lista de objetos cargados).</summary>
        private Dictionary<string, List<object>> _cacheIngenieria = new Dictionary<string, List<object>>();

        /// <summary>Caché para los parámetros de configuración global (ej. N_MAX_V).</summary>
        private Dictionary<string, int> _cacheDispConfig = new Dictionary<string, int>();

        /// <summary>ViewModel de la vista de detalle de dispositivos.</summary>
        public DispositivosViewModel DispositivosViewModel { get; set; }

        /// <summary>Lista de categorías de dispositivos configuradas en el archivo disp_settings.xml.</summary>
        public List<DeviceCategory> Categorias { get; set; }





        /// <summary>Colección de mensajes de log vinculada a la pestaña de Log en la UI.</summary>
        public LogViewModel LogViewModel { get; set; }






        private bool _isBusy;
        /// <summary>Indica si la aplicación está realizando una tarea pesada para bloquear/desbloquear la UI.</summary>
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        private string _archivoExcelSeleccionado;
        /// <summary>Ruta completa del archivo Excel seleccionado por el usuario.</summary>
        public string ArchivoExcelSeleccionado { get => _archivoExcelSeleccionado; set { _archivoExcelSeleccionado = value; OnPropertyChanged(); } }

        private string _mensajeEstado;
        /// <summary>Mensaje informativo que se muestra en la barra de estado inferior.</summary>
        public string MensajeEstado { get => _mensajeEstado; set { _mensajeEstado = value; OnPropertyChanged(); } }

        private string _colorEstado = "Black";
        /// <summary>Color hexadecimal o nombre de color para el texto de estado (negro normal, rojo para errores).</summary>
        public string ColorEstado { get => _colorEstado; set { _colorEstado = value; OnPropertyChanged(); } }

        /// <summary>Comando para iniciar el proceso de carga y extracción desde Excel.</summary>
        public RelayCommand CargarDatosCommand { get; set; }

        /// <summary>Comando para abrir el editor de configuración del extractor Python.</summary>
        public RelayCommand ConfigurarRutaExeCommand { get; set; }

        /// <summary>Comando para abrir el editor de mapa de dispositivos (categorías).</summary>
        public RelayCommand ConfigurarDispositivosCommand { get; set; }








        // =================================================================================================================
        // 2. CONSTRUCTOR

        /// <summary>
        /// Inicializa una nueva instancia de <see cref="MainViewModel"/>.
        /// </summary>
        /// <param name="tiaPortal">Instancia activa de TIA Portal.</param>
        /// <param name="plcSoftware">Software del PLC sobre el que se trabajará.</param>
        public MainViewModel(TiaPortal tiaPortal, PlcSoftware plcSoftware)
        {
            _tiaPortal = tiaPortal;
            _plcSoftware = plcSoftware;

            LogService.Clear();
            LogService.Write("Inicializando MainViewModel...");

            // Paso 1: Configura carpetas y archivos base si no existen
            AppConfigManager.InicializarEntorno();

            // Paso 2: Carga la configuración de qué dispositivos vamos a manejar
            Categorias = AppConfigManager.ObtenerMapaDispositivos();

            // Paso 3: Inicializa servicios de TIA Portal y sub-vistas
            _tiaService = new TiaService(plcSoftware);
            DispositivosViewModel = new DispositivosViewModel();
            LogViewModel = new LogViewModel();

            // Inyecta la configuración al sub-viewmodel de dispositivos
            DispositivosViewModel.Categorias = Categorias;
            DispositivosViewModel.SetTiaService(_tiaService);

            // Suscribe eventos para reflejar mensajes del servicio en la barra de estado
            DispositivosViewModel.StatusRequest = (msg, error) => ActualizarEstado(msg, error);
            _tiaService.OnStatusChanged = (msg, error) => ActualizarEstado(msg, error);

            if (Categorias.Count > 0)
                DispositivosViewModel.CategoriaSeleccionada = Categorias[0];

            MensajeEstado = $"Conectado a PLC: {plcSoftware.Name}";
            LogService.Write($"Conectado a PLC: {plcSoftware.Name}");

            // Mapeo de comandos
            CargarDatosCommand = new RelayCommand(CargarExcelYGenerarJson);
            ConfigurarRutaExeCommand = new RelayCommand(EjecutarConfiguracionRuta);
            ConfigurarDispositivosCommand = new RelayCommand(EjecutarConfiguracionDispositivos);
        }








        // =================================================================================================================
        // 3. LÓGICA DE EXTRACCIÓN Y CARGA

        /// <summary>
        /// Orquestador principal del proceso de carga:
        /// Selecciona el Excel, lanza el extractor Python, espera la generación de XML y carga los datos en memoria.
        /// </summary>
        private void CargarExcelYGenerarJson()
        {
            LogService.Write("Botón 'Cargar' pulsado.");

            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsm;*.xlsx",
                    Title = "Selecciona el archivo de definición"
                };

                if (openFileDialog.ShowDialog() != true) return;

                ArchivoExcelSeleccionado = openFileDialog.FileName;
                LogService.Write($"Archivo seleccionado: {ArchivoExcelSeleccionado}");

                string exePath = AppConfigManager.LeerExePath();
                LogService.Write($"Ruta extractor: {exePath}");

                if (!File.Exists(exePath))
                {
                    LogService.Write("ERROR: Extractor no encontrado", true);
                    ActualizarEstado("Error: No se encuentra ZC_Extractor.exe", true);
                    MessageBox.Show($"Extractor no encontrado en:\n{exePath}", "Error de configuración");
                    return;
                }

                // Limpieza de datos antiguos para evitar confusiones
                LogService.Write("Limpiando archivos exportados...");
                ActualizarEstado("Limpiando archivos exportados...");
                LimpiarCarpetaExportar(AppConfigManager.ExportPath);

                // Ejecución del extractor Python
                LogService.Write("Lanzando proceso Python...");
                ActualizarEstado("Ejecutando extractor Python...");
                string argumentos = $"--path \"{ArchivoExcelSeleccionado}\"";
                var proceso = Siemens.Engineering.AddIn.Utilities.Process.Start(exePath, argumentos);

                // Bucle de monitorización de archivos
                if (EsperarArchivosPython())
                {
                    LogService.Write("Archivos XML detectados con éxito.");
                    ActualizarEstado("Sincronizando base de datos interna...");

                    // Lee los XML generados y los convierte en objetos C#
                    CargarTodoDesdeCarpeta(AppConfigManager.ExportPath);

                    // Envía el diccionario con todos los datos al ViewModel de la tabla
                    DispositivosViewModel.SetDatos(_cacheIngenieria, _cacheDispConfig);
                    ActualizarEstado("Listo. Todos los módulos cargados.");
                }
                else
                {
                    ActualizarEstado("Error: Tiempo de espera agotado o faltan archivos XML.", true);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"CRASH EN CARGA: {ex.Message}", true);
                LogService.Write($"STACKTRACE: {ex.StackTrace}", true);
                ActualizarEstado("Crash general en el proceso.", true);
                MessageBox.Show($"{ex.Message}", "Error Crítico");
            }
        }




        // =================================================================================================================

        /// <summary>
        /// Monitoriza la carpeta de exportación hasta que aparecen todos los ficheros XML 
        /// configurados en <see cref="Categorias"/> más el archivo de configuración global.
        /// </summary>
        /// <returns>True si todos los archivos fueron detectados antes del timeout (30s), False en caso contrario.</returns>
        private bool EsperarArchivosPython()
        {
            LogService.Write($"Iniciando espera de archivos en: {AppConfigManager.ExportPath}");
            ActualizarEstado("Generando archivos XML (esperando hasta 30s)...");

            if (Categorias == null || Categorias.Count == 0)
            {
                LogService.Write("ERROR: La lista de Categorías está vacía. Revisa disp_settings.xml", true);
                return false;
            }

            for (int i = 0; i < 150; i++) // 30 segundos de timeout total (150 * 200ms)
            {
                string archivoFaltante = "";
                bool faltan = false;

                foreach (var cat in Categorias)
                {
                    string rutaCheck = Path.Combine(AppConfigManager.ExportPath, cat.XmlFile);
                    if (!File.Exists(rutaCheck))
                    {
                        faltan = true;
                        archivoFaltante = cat.XmlFile;
                        break;
                    }
                }

                if (!faltan)
                {
                    if (!File.Exists(AppConfigManager.dispConfig))
                    {
                        faltan = true;
                        archivoFaltante = Path.GetFileName(AppConfigManager.dispConfig);
                    }
                }

                if (!faltan)
                {
                    LogService.Write("¡ÉXITO! Todos los archivos encontrados.");
                    return true;
                }

                if (i % 10 == 0) LogService.Write($"Intento {i}/150: No se encuentra {archivoFaltante}");

                Thread.Sleep(200);
                ActualizarEstadoDuranteCiclo(); // Mantiene la UI viva procesando mensajes de Windows
            }

            LogService.Write("TIMEOUT: No se encontraron los archivos esperados.", true);
            return false;
        }




        // =================================================================================================================

        /// <summary>
        /// Limpia la caché interna y carga todos los datos desde los archivos XML de la carpeta de exportación.
        /// </summary>
        /// <param name="carpeta">Ruta de la carpeta que contiene los XML generados.</param>
        private void CargarTodoDesdeCarpeta(string carpeta)
        {
            _cacheIngenieria.Clear();
            _cacheDispConfig.Clear();

            _cacheDispConfig = LeerConfiguracionDispositivo(AppConfigManager.dispConfig);

            foreach (var cat in Categorias)
            {
                string ruta = Path.Combine(carpeta, cat.XmlFile);
                if (File.Exists(ruta))
                {
                    _cacheIngenieria[cat.Name] = LeerArchivoEspecifico(ruta, cat);
                }
            }
        }




        // =================================================================================================================

        /// <summary>
        /// Parsea el archivo de configuración de dimensiones globales.
        /// </summary>
        /// <param name="ruta">Ruta al archivo disp_config.xml.</param>
        /// <returns>Diccionario con los parámetros de configuración (Nombre -> Valor).</returns>
        private Dictionary<string, int> LeerConfiguracionDispositivo(string ruta)
        {
            var config = new Dictionary<string, int>();

            if (!File.Exists(ruta))
            {
                LogService.Write($"ADVERTENCIA: Archivo de configuración global no encontrado en {ruta}", true);
                return config;
            }

            try
            {
                XDocument doc = XDocument.Load(ruta);

                // Mapeamos cada nodo XML al objeto Disp_Config y luego al diccionario
                config = doc.Descendants("Item")
                            .Select(x => Disp_Config.FromXml(x))
                            .ToDictionary(c => c.Name, c => c.Value);

                LogService.Write($"[CONFIG] Cargados {config.Count} parámetros globales correctamente.");
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR al leer configuración global: {ex.Message}", true);
            }

            return config;
        }




        // =================================================================================================================

        /// <summary>
        /// Utiliza Reflexión para cargar dinámicamente una lista de objetos desde un XML, 
        /// basándose en la clase modelo definida en la categoría.
        /// </summary>
        /// <param name="ruta">Ruta al archivo XML de la categoría.</param>
        /// <param name="cat">Objeto de categoría que contiene el nombre del modelo C#.</param>
        /// <returns>Lista de objetos instanciados del tipo correspondiente.</returns>
        private List<object> LeerArchivoEspecifico(string ruta, DeviceCategory cat)
        {
            LogService.Write($"Procesando archivo: {cat.XmlFile} para modelo: {cat.ModelClass}");
            var lista = new List<object>();

            try
            {
                XDocument doc = XDocument.Load(ruta);
                var elementos = doc.Root.Elements();

                string nombreCompletoClase = $"ZC_ALM_TOOLS.Models.{cat.ModelClass}";
                Type tipoClase = Type.GetType(nombreCompletoClase);

                if (tipoClase == null)
                {
                    LogService.Write($"ERROR: No se encontró la clase {nombreCompletoClase}", true);
                    return lista;
                }

                // Busca el método estático FromXml en la clase modelo
                var metodoFromXml = tipoClase.GetMethod("FromXml",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (metodoFromXml == null)
                {
                    LogService.Write($"ERROR: {cat.ModelClass} no tiene método FromXml", true);
                    return lista;
                }

                foreach (var el in elementos)
                {
                    var objetoCargado = metodoFromXml?.Invoke(null, new object[] { el });
                    if (objetoCargado != null) lista.Add(objetoCargado);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error dinámico cargando {cat.ModelClass}: {ex.Message}");
            }
            return lista;
        }




        // =================================================================================================================

        /// <summary>
        /// Elimina todos los archivos dentro de la carpeta de exportación para evitar procesar datos obsoletos.
        /// </summary>
        /// <param name="ruta">Ruta de la carpeta a limpiar.</param>
        private void LimpiarCarpetaExportar(string ruta)
        {
            try
            {
                if (!Directory.Exists(ruta)) return;
                foreach (string archivo in Directory.GetFiles(ruta))
                {
                    try { File.Delete(archivo); } catch { }
                }
            }
            catch { }
        }








        // =================================================================================================================
        // 4. CONFIGURACIÓN Y EDITORES

        /// <summary>Comando para abrir el editor de configuración general.</summary>
        private void EjecutarConfiguracionRuta() => AbrirEditor(AppConfigManager.SettingsXmlFile, "Editando ajustes generales...");




        // =================================================================================================================

        /// <summary>Comando para abrir el editor del mapa de dispositivos.</summary>
        private void EjecutarConfiguracionDispositivos() => AbrirEditor(AppConfigManager.DeviceXmlFile, "Editando mapa de dispositivos...");




        // =================================================================================================================

        /// <summary>
        /// Abre un archivo de texto en el Bloc de Notas.
        /// </summary>
        /// <param name="ruta">Ruta del archivo a abrir.</param>
        /// <param name="mensaje">Mensaje a mostrar en la barra de estado.</param>
        private void AbrirEditor(string ruta, string mensaje)
        {
            if (!File.Exists(ruta)) return;
            Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", $"\"{ruta}\"");
            ActualizarEstado(mensaje);
        }




        // =================================================================================================================

        /// <summary>
        /// Actualiza la información de la barra de estado de la aplicación.
        /// </summary>
        /// <param name="mensaje">Texto a mostrar.</param>
        /// <param name="esError">Si es verdadero, el texto se mostrará en rojo.</param>
        private void ActualizarEstado(string mensaje, bool esError = false)
        {
            MensajeEstado = mensaje;
            ColorEstado = esError ? "Red" : "Black";
        }




        // =================================================================================================================

        /// <summary>
        /// Fuerza el procesamiento de mensajes de la cola de Windows para evitar que la interfaz se congele 
        /// durante procesos largos que se ejecutan en el hilo principal (UI Thread).
        /// </summary>
        private void ActualizarEstadoDuranteCiclo()
        {
            System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(delegate (object f)
                {
                    ((System.Windows.Threading.DispatcherFrame)f).Continue = false;
                    return null;
                }), frame);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
}