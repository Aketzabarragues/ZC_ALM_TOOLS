using System;
using System.Collections.Generic;
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
using ZC_ALM_TOOLS.Services;
using ZC_ALM_TOOLS.Models;

namespace ZC_ALM_TOOLS.ViewModels
{
    /* * RESUMEN DE PASOS DEL MAIN VIEW MODEL:
     * 1. INICIALIZACIÓN: Se configuran las rutas, se cargan los ajustes (settings.xml) y el mapa de dispositivos (disp_settings.xml).
     * 2. INTERFAZ DE USUARIO: Gestiona el comando para seleccionar un archivo Excel de definición.
     * 3. EXTRACCIÓN EXTERNA: Lanza el proceso Python (ZC_Extractor.exe) encargado de leer el Excel y generar archivos XML temporales.
     * 4. SINCRONIZACIÓN Y ESPERA: El programa monitoriza la carpeta de exportación hasta que todos los XML esperados están listos.
     * 5. CARGA DINÁMICA (REFLEXIÓN): Lee cada XML y, mediante reflexión, instancia automáticamente el modelo C# correspondiente (Disp_V, Disp_ED, etc.).
     * 6. INYECCIÓN DE DATOS: Envía la información procesada al DispositivosViewModel para su visualización en el DataGrid.
     * 7. GESTIÓN DE LOGS: Registra cada evento importante en un archivo de depuración para facilitar el mantenimiento.
     */

    public class MainViewModel : ObservableObject
    {
        // =================================================================================================================
        // 1. PROPIEDADES Y SERVICIOS

        private TiaService _tiaService;
        private TiaPortal _tiaPortal;
        private PlcSoftware _plcSoftware;

        // Diccionario que almacena los objetos cargados (Key: Nombre de categoría, Value: Lista de objetos)
        private Dictionary<string, List<object>> _cacheIngenieria = new Dictionary<string, List<object>>();
        private Dictionary<string, int> _cacheDispConfig = new Dictionary<string, int>();
        public DispositivosViewModel DispositivosViewModel { get; set; }

        // Lista de categorías configuradas en disp_settings.xml
        public List<DeviceCategory> Categorias { get; set; }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        private string _archivoExcelSeleccionado;
        public string ArchivoExcelSeleccionado { get => _archivoExcelSeleccionado; set { _archivoExcelSeleccionado = value; OnPropertyChanged(); } }

        private string _mensajeEstado;
        public string MensajeEstado { get => _mensajeEstado; set { _mensajeEstado = value; OnPropertyChanged(); } }

        private string _colorEstado = "Black";
        public string ColorEstado { get => _colorEstado; set { _colorEstado = value; OnPropertyChanged(); } }

        // Comandos enlazados a los botones de la View
        public RelayCommand CargarDatosCommand { get; set; }
        public RelayCommand ConfigurarRutaExeCommand { get; set; }
        public RelayCommand ConfigurarDispositivosCommand { get; set; }


        // =================================================================================================================
        // 2. CONSTRUCTOR

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

        // Orquestador principal: Selecciona Excel -> Lanza Python -> Espera XML -> Carga en Memoria
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

                // Ejecución del extractor Python de forma asíncrona para el sistema operativo
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

        // Monitoriza la carpeta hasta que aparecen todos los ficheros configurados en disp_settings.xml
        private bool EsperarArchivosPython()
        {
            LogService.Write($"Iniciando espera de archivos en: {AppConfigManager.ExportPath}");
            ActualizarEstado("Generando archivos XML (esperando hasta 30s)...");

            if (Categorias == null || Categorias.Count == 0)
            {
                LogService.Write("ERROR: La lista de Categorías está vacía. Revisa disp_settings.xml", true);
                return false;
            }

            for (int i = 0; i < 150; i++) // 30 segundos de timeout total
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
                    // Usamos la variable que ya tienes definida en AppConfigManager
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
                ActualizarEstadoDuranteCiclo(); // Mantiene la UI viva
            }

            LogService.Write("TIMEOUT: No se encontraron los archivos esperados.", true);
            return false;
        }

        // Recorre la carpeta de exportación cargando cada categoría configurada
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


        // Método lectura configuracion dispositivo
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

                // --- CAMBIO AQUÍ: Usamos el método FromXml de tu modelo ---
                config = doc.Descendants("Item")
                            .Select(x => Disp_Config.FromXml(x)) // Mapeamos cada nodo XML al objeto Disp_Config
                            .ToDictionary(c => c.Name, c => c.Value); // Luego lo convertimos al diccionario

                LogService.Write($"[CONFIG] Cargados {config.Count} parámetros globales correctamente usando modelo Disp_Config.");
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR al leer configuración global: {ex.Message}", true);
            }

            return config;
        }

        // Método maestro: Usa Reflexión para invocar el método "FromXml" de la clase definida en la configuración
        private List<object> LeerArchivoEspecifico(string ruta, DeviceCategory cat)
        {
            LogService.Write($"Procesando archivo: {cat.XmlFile} para modelo: {cat.ModelClass}");
            var lista = new List<object>();

            try
            {
                XDocument doc = XDocument.Load(ruta);
                var elementos = doc.Root.Elements();

                // Busca la clase modelo por nombre de string (ej: "Disp_V")
                string nombreCompletoClase = $"ZC_ALM_TOOLS.Models.{cat.ModelClass}";
                Type tipoClase = Type.GetType(nombreCompletoClase);

                if (tipoClase == null)
                {
                    LogService.Write($"ERROR: No se encontró la clase {nombreCompletoClase}", true);
                    return lista;
                }

                // Obtiene el método estático FromXml del modelo
                var metodoFromXml = tipoClase.GetMethod("FromXml",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (metodoFromXml == null)
                {
                    LogService.Write($"ERROR: {cat.ModelClass} no tiene método FromXml", true);
                    return lista;
                }

                foreach (var el in elementos)
                {
                    // Ejecuta el método FromXml y añade el objeto resultante a la lista
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

        // Elimina residuos de exportaciones anteriores
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

        private void EjecutarConfiguracionRuta() => AbrirEditor(AppConfigManager.SettingsXmlFile, "Editando ajustes generales...");

        private void EjecutarConfiguracionDispositivos() => AbrirEditor(AppConfigManager.DeviceXmlFile, "Editando mapa de dispositivos...");

        // Abre archivos XML en el Bloc de Notas para edición rápida
        private void AbrirEditor(string ruta, string mensaje)
        {
            if (!File.Exists(ruta)) return;
            Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", $"\"{ruta}\"");
            ActualizarEstado(mensaje);
        }

        // Helper para actualizar la barra inferior de la aplicación
        private void ActualizarEstado(string mensaje, bool esError = false)
        {
            MensajeEstado = mensaje;
            ColorEstado = esError ? "Red" : "Black";
        }

        // "Truco" para procesar eventos de la UI durante un hilo de ejecución pesado y evitar que se congele
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