using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{

    public class MainViewModel : ObservableObject
    {

        // =================================================================================================================
        // PROPIEDADES
        private TiaPlcService _tiaPlcService;
                
        // Caché de datos cargados
        private Dictionary<string, List<object>> _engineeringCache = new Dictionary<string, List<object>>();

        // Cache de configuracion xml
        private ConfigProcessSettings _configProcessesSettings;
        private ConfigDeviceSettings _configDeviceSettings;
        private ConfigGlobalSettings _configGlobalSettings;        
        private List<ConfigDeviceCategory> _configDeviceCategory { get; set; }

        // ViewModels y Configuración
        public DevicesViewModel DevicesVM { get; set; }
        public ProcessViewModel ProcessVM { get; set; }

        // Variable que indica que esta ejecutandose algo
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        // Variable que indica si se ha cargado un Excel correctamente
        private bool _isDataLoaded;
        public bool IsDataLoaded
        {
            get => _isDataLoaded;
            set { _isDataLoaded = value; OnPropertyChanged(); }
        }

        // Ruta del excel seleccionado
        private string _selectedExcelFile;
        public string SelectedExcelFile
        {
            get => _selectedExcelFile;
            set { _selectedExcelFile = value; OnPropertyChanged(); }
        }

        // Mensaje de estado
        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // Color de estado
        private string _statusColor = "Black";
        public string StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        // Comandos
        public RelayCommand LoadDataCommand { get; set; }
        public RelayCommand ConfigSettingsCommand { get; set; }



        // ==================================================================================================================
        // CONSTRUCTOR
        public MainViewModel(TiaPortal tiaPortal, PlcSoftware plcSoftware)
        {
            LogService.Clear();
            LogService.Write("Inicializando MainViewModel...");

            // Inicializar estado
            IsDataLoaded = false;

            // Inicializamos configuración y cargamos categorías
            AppConfigManager.InitializeEnvironment();
            _configProcessesSettings = AppConfigManager.GetProcessConfig();
            _configDeviceSettings = AppConfigManager.GetDeviceSettings();
            _configGlobalSettings = AppConfigManager.GetGlobalSettings();
            _configDeviceCategory = AppConfigManager.GetDeviceCategories();

            // Inicializamos servicios y viewmodels
            _tiaPlcService = new TiaPlcService(plcSoftware);

            DevicesVM = new DevicesViewModel();
            DevicesVM.Categories = _configDeviceCategory;
            DevicesVM.SetTiaService(_tiaPlcService);

            ProcessVM = new ProcessViewModel();
            ProcessVM.SetTiaService(_tiaPlcService);

            // Evento para actualizar el mensaje de estado
            StatusService.OnStatusChanged += UpdateStatus;
            StatusService.OnBusyChanged += (busy) => IsBusy = busy;

            // Seleccionamos una categoria en el viewmodel
            if (_configDeviceCategory.Count > 0)
                DevicesVM.SelectedCategory = _configDeviceCategory[0];

            StatusMessage = $"Conectado a PLC: {plcSoftware.Name}";
            LogService.Write($"Conectado a PLC: {plcSoftware.Name}");

            // Mapeo de comandos
            LoadDataCommand = new RelayCommand(LoadExcelAndGenerateJson);
            ConfigSettingsCommand = new RelayCommand(OpenSettingsEditor);

        }



        // ==================================================================================================================
        // LÓGICA DE EXTRACCIÓN Y CARGA
        private void LoadExcelAndGenerateJson()
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

                StatusService.SetBusy(true);

                SelectedExcelFile = openFileDialog.FileName;
                LogService.Write($"Archivo seleccionado: {SelectedExcelFile}");

                // Verificar ruta del extractor
                if (!File.Exists(_configGlobalSettings.ExtractorExePath))
                {
                    LogService.Write("ERROR: Extractor no encontrado", true);
                    UpdateStatus("Error: No se encuentra ZC_Extractor.exe", true);
                    MessageBox.Show($"Extractor no encontrado en:\n{_configGlobalSettings.ExtractorExePath}", "Error de configuración");
                    return;
                }


                ClearExportFolder(AppConfigManager.ExportPath);

                LogService.Write("Lanzando proceso Python...");
                UpdateStatus("Ejecutando extractor Python...");



                // 1. Ejecutar y comprobar si Python terminó con éxito
                if (StartExtractor())
                {
                    // 2. Si Python terminó bien, comprobamos que los archivos estén ahí
                    if (WaitForPythonFiles())
                    {
                        LogService.Write("Archivos XML detectados con éxito.");
                        UpdateStatus("Cargando datos en memoria...");

                        LoadAllFromFolder(AppConfigManager.ExportPath);

                        // Actualizar ViewModels
                        DevicesVM.LoadData(_engineeringCache, _configDeviceSettings);
                        ProcessVM.LoadData(_engineeringCache, _configProcessesSettings);

                        IsDataLoaded = true;
                        UpdateStatus("Listo. Todos los módulos cargados.");
                    }
                    else
                    {
                        UpdateStatus("Error: Python terminó pero no se encontraron los archivos XML.", true);
                    }
                }
                else
                {
                    // Si llegamos aquí, es que Python falló (ExitCode != 0)
                    UpdateStatus("Error en el script de extracción. Revisa el LOG.", true);
                    MessageBox.Show("El extractor de Python ha fallado. Consulta los detalles en la pestaña de Log.",
                                    "Error de Extracción", MessageBoxButton.OK, MessageBoxImage.Error);
                }


            }
            catch (Exception ex)
            {
                LogService.Write($"CRASH EN CARGA: {ex.Message}", true);
                LogService.Write($"CRASH EN CARGA:\n{ex.ToString()}", true);
                UpdateStatus("Crash general en el proceso.", true);
                MessageBox.Show($"{ex.Message}", "Error Crítico");
            }
            finally 
            {
                StatusService.SetBusy(false);
            }

        }



        // ==================================================================================================================
        // Metodo para lanzar el programa de extraccion de python
        private bool StartExtractor()
        {
            try
            {
                string arguments = $"--path \"{SelectedExcelFile}\"";

                // 1. Crear la info de inicio (Asegúrate de que sea la de Siemens)
                var startInfo = new Siemens.Engineering.AddIn.Utilities.ProcessStartInfo
                {
                    FileName = _configGlobalSettings.ExtractorExePath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                // 2. CREAR EL OBJETO PROCESO
                var myProcess = new Siemens.Engineering.AddIn.Utilities.Process();
                myProcess.StartInfo = startInfo;

                // 3. Suscribirse a los eventos ANTES de empezar
                myProcess.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) LogService.Write($"[PYTHON] {e.Data}");
                };

                myProcess.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) LogService.Write($"[PYTHON-ERR] {e.Data}", true);
                };

                // 4. LANZAR E INICIAR LECTURA
                if (myProcess.Start())
                {
                    myProcess.BeginOutputReadLine();
                    myProcess.BeginErrorReadLine();

                    LogService.Write("Extractor ejecutándose en segundo plano...");
                    myProcess.WaitForExit();

                    // Devolvemos true solo si terminó sin errores (ExitCode 0)
                    return myProcess.ExitCode == 0;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"Error crítico lanzando Python: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        // Metodo para esperar a que se encuentren todos los archivos esperados
        private bool WaitForPythonFiles()
        {        

            LogService.Write($"Iniciando espera en: {AppConfigManager.ExportPath}");

            // Creamos la lista de archivos que esperamos basándonos en la configuración
            List<string> expectedFiles = new List<string>();
            expectedFiles.AddRange(_configDeviceCategory.Select(c => c.XmlFile));
            expectedFiles.Add(_configProcessesSettings.ProcessXml);
            expectedFiles.Add(_configProcessesSettings.PRealXml);
            expectedFiles.Add(_configProcessesSettings.PIntXml);
            expectedFiles.Add(_configDeviceSettings.DeviceDataConfigXml);

            for (int i = 0; i < 150; i++)
            {
                bool allFound = true;
                foreach (var file in expectedFiles)
                {
                    if (string.IsNullOrEmpty(file)) continue;
                    if (!File.Exists(Path.Combine(AppConfigManager.ExportPath, file)))
                    {
                        allFound = false;
                        break;
                    }
                }

                if (allFound) return true;

                Thread.Sleep(200);
                UpdateStatusFrame();
            }
            return false;
        }



        // ==================================================================================================================
        // Metodo para cargar todos los archivos desde una carpeta
        private void LoadAllFromFolder(string folderPath)
        {
            _engineeringCache.Clear();

            // Cargar dispositivos de cada categoría
            foreach (var cat in _configDeviceCategory)
            {
                string filePath = Path.Combine(folderPath, cat.XmlFile);
                if (File.Exists(filePath))
                {
                    _engineeringCache[cat.Name] = DataService.LoadDispCategoryData(filePath, cat);
                }
            }

            // Cargar numero maximo de dispositivos
            if (_configDeviceSettings != null)
            {
                string path = Path.Combine(folderPath, _configDeviceSettings.DeviceDataConfigXml);
                if (File.Exists(path))
                {
                    // Cargamos como lista de objetos Disp_Config
                    var data = DataService.LoadDeviceNMax(path);
                    _engineeringCache[_configDeviceSettings.Disp_N_Max] = data.Cast<object>().ToList();
                }
            }

            // Cargar configuracion de procesos
            if (_configProcessesSettings != null)
            {
                // Lista de procesos
                string pathProcess = Path.Combine(folderPath, _configProcessesSettings.ProcessXml);
                if (File.Exists(pathProcess))
                {
                    var data = DataService.LoadProcess(pathProcess);
                    _engineeringCache[_configProcessesSettings.ProcessName] = data.Cast<object>().ToList();
                }

                // Parámetros Reales
                string pathPReal = Path.Combine(folderPath, _configProcessesSettings.PRealXml);
                if (File.Exists(pathPReal))
                {
                    var data = DataService.LoadParameters(pathPReal);
                    _engineeringCache[_configProcessesSettings.PRealName] = data.Cast<object>().ToList();
                }

                // Parámetros Enteros
                string pathPInt = Path.Combine(folderPath, _configProcessesSettings.PIntXml);
                if (File.Exists(pathPInt))
                {
                    var data = DataService.LoadParameters(pathPInt);
                    _engineeringCache[_configProcessesSettings.PIntName] = data.Cast<object>().ToList();
                }

                // Alarmas
                string pathAlm = Path.Combine(folderPath, _configProcessesSettings.AlarmXml);
                if (File.Exists(pathAlm))
                {
                    var data = DataService.LoadAlarms(pathAlm);
                    _engineeringCache[_configProcessesSettings.AlarmName] = data.Cast<object>().ToList();
                }
            }

        }



        // ==================================================================================================================
        // Limpiar la carpeta de exportacion de archivos
        private void ClearExportFolder(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string f in Directory.GetFiles(path))
            {
                try { File.Delete(f); } catch { }
            }
        }



        // ==================================================================================================================
        // CONFIGURACIÓN Y UTILIDADES UI
        private void OpenSettingsEditor() => OpenEditor(AppConfigManager.AppConfigFile, "Editando ajustes...");

        private void OpenEditor(string path, string message)
        {
            if (!File.Exists(path)) return;
            Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", $"\"{path}\"");
            UpdateStatus(message);
        }

        // Metodo para actualizar la barra de estado
        private void UpdateStatus(string message, bool isError = false)
        {
            StatusMessage = message;
            StatusColor = isError ? "Red" : "Black";
            UpdateStatusFrame();
        }




    }
}