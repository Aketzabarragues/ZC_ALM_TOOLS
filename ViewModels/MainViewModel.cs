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

        private TiaService _tiaService;
        private TiaPortal _tiaPortal;
        private PlcSoftware _plcSoftware;

        private Dictionary<string, List<object>> _cacheIngenieria = new Dictionary<string, List<object>>();
        private Dictionary<string, int> _cacheDispConfig = new Dictionary<string, int>();

        public DispositivosViewModel DispositivosViewModel { get; set; }
        public List<DeviceCategory> Categorias { get; set; }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        private string _archivoExcelSeleccionado;
        public string ArchivoExcelSeleccionado { get => _archivoExcelSeleccionado; set { _archivoExcelSeleccionado = value; OnPropertyChanged(); } }

        private string _mensajeEstado;
        public string MensajeEstado { get => _mensajeEstado; set { _mensajeEstado = value; OnPropertyChanged(); } }

        private string _colorEstado = "Black";
        public string ColorEstado { get => _colorEstado; set { _colorEstado = value; OnPropertyChanged(); } }

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

            AppConfigManager.InicializarEntorno();
            Categorias = AppConfigManager.ObtenerMapaDispositivos();

            _tiaService = new TiaService(plcSoftware);
            DispositivosViewModel = new DispositivosViewModel();

            DispositivosViewModel.Categorias = Categorias;
            DispositivosViewModel.SetTiaService(_tiaService);

            // Suscripción de eventos para la barra de estado
            DispositivosViewModel.StatusRequest = (msg, error) => ActualizarEstado(msg, error);
            _tiaService.OnStatusChanged = (msg, error) => ActualizarEstado(msg, error);

            if (Categorias.Count > 0)
                DispositivosViewModel.CategoriaSeleccionada = Categorias[0];

            MensajeEstado = $"Conectado a PLC: {plcSoftware.Name}";
            LogService.Write($"Conectado a PLC: {plcSoftware.Name}");

            CargarDatosCommand = new RelayCommand(CargarExcelYGenerarJson);
            ConfigurarRutaExeCommand = new RelayCommand(EjecutarConfiguracionRuta);
            ConfigurarDispositivosCommand = new RelayCommand(EjecutarConfiguracionDispositivos);
        }

        // =================================================================================================================
        // 3. LÓGICA DE EXTRACCIÓN Y CARGA

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
                if (!File.Exists(exePath))
                {
                    LogService.Write("ERROR: Extractor no encontrado", true);
                    ActualizarEstado("Error: No se encuentra ZC_Extractor.exe", true);
                    MessageBox.Show($"Extractor no encontrado en:\n{exePath}", "Error de configuración");
                    return;
                }

                IsBusy = true;
                LimpiarCarpetaExportar(AppConfigManager.ExportPath);

                LogService.Write("Lanzando proceso Python...");
                ActualizarEstado("Ejecutando extractor Python...");
                string argumentos = $"--path \"{ArchivoExcelSeleccionado}\"";

                // Uso de la utilidad de proceso de Siemens Openness
                Siemens.Engineering.AddIn.Utilities.Process.Start(exePath, argumentos);

                if (EsperarArchivosPython())
                {
                    LogService.Write("Archivos XML detectados con éxito.");
                    ActualizarEstado("Sincronizando base de datos interna...");

                    CargarTodoDesdeCarpeta(AppConfigManager.ExportPath);
                    DispositivosViewModel.SetDatos(_cacheIngenieria, _cacheDispConfig);

                    ActualizarEstado("Listo. Todos los módulos cargados.");
                }
                else
                {
                    ActualizarEstado("Error: Tiempo de espera agotado.", true);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"CRASH EN CARGA: {ex.Message}", true);
                ActualizarEstado("Crash general en el proceso.", true);
                MessageBox.Show($"{ex.Message}", "Error Crítico");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool EsperarArchivosPython()
        {
            LogService.Write($"Iniciando espera en: {AppConfigManager.ExportPath}");

            for (int i = 0; i < 150; i++)
            {
                bool faltan = false;
                string archivoFaltante = "";

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

                if (!faltan && !File.Exists(AppConfigManager.dispConfig))
                {
                    faltan = true;
                    archivoFaltante = "disp_config.xml";
                }

                if (!faltan) return true;

                if (i % 10 == 0) LogService.Write($"Esperando {archivoFaltante}...");

                Thread.Sleep(200);
                ActualizarEstadoDuranteCiclo();
            }
            return false;
        }

        private void CargarTodoDesdeCarpeta(string carpeta)
        {
            _cacheIngenieria.Clear();
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

        private Dictionary<string, int> LeerConfiguracionDispositivo(string ruta)
        {
            var config = new Dictionary<string, int>();
            if (!File.Exists(ruta)) return config;

            try
            {
                XDocument doc = XDocument.Load(ruta);
                config = doc.Descendants("Item")
                            .Select(x => Disp_Config.FromXml(x))
                            .ToDictionary(c => c.Name, c => c.Value);
            }
            catch (Exception ex) { LogService.Write($"Error config: {ex.Message}", true); }
            return config;
        }

        private List<object> LeerArchivoEspecifico(string ruta, DeviceCategory cat)
        {
            var lista = new List<object>();
            try
            {
                XDocument doc = XDocument.Load(ruta);
                string nombreCompletoClase = $"ZC_ALM_TOOLS.Models.{cat.ModelClass}";
                Type tipoClase = Type.GetType(nombreCompletoClase);

                if (tipoClase == null) return lista;

                var metodoFromXml = tipoClase.GetMethod("FromXml", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                foreach (var el in doc.Root.Elements())
                {
                    var objeto = metodoFromXml?.Invoke(null, new object[] { el });
                    if (objeto != null) lista.Add(objeto);
                }
            }
            catch (Exception ex) { LogService.Write($"Error {cat.ModelClass}: {ex.Message}", true); }
            return lista;
        }

        private void LimpiarCarpetaExportar(string ruta)
        {
            if (!Directory.Exists(ruta)) return;
            foreach (string f in Directory.GetFiles(ruta))
            {
                try { File.Delete(f); } catch { }
            }
        }

        // =================================================================================================================
        // 4. CONFIGURACIÓN Y EDITORES

        private void EjecutarConfiguracionRuta() => AbrirEditor(AppConfigManager.SettingsXmlFile, "Editando ajustes...");
        private void EjecutarConfiguracionDispositivos() => AbrirEditor(AppConfigManager.DeviceXmlFile, "Editando mapa...");

        private void AbrirEditor(string ruta, string mensaje)
        {
            if (!File.Exists(ruta)) return;
            Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", $"\"{ruta}\"");
            ActualizarEstado(mensaje);
        }

        private void ActualizarEstado(string mensaje, bool esError = false)
        {
            MensajeEstado = mensaje;
            ColorEstado = esError ? "Red" : "Black";
        }

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