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
        private TiaService _tiaService;
                
        // Caché de datos cargados
        private Dictionary<string, List<object>> _engineeringCache = new Dictionary<string, List<object>>();

        // Cache de configuracion xml
        private ConfigProcessSettings _configProcessesSettings;
        private ConfigDeviceSettings _configDeviceSettings;
        private ConfigGlobalSettings _configGlobalSettings;        
        private List<ConfigDeviceCategory> _configDeviceCategory { get; set; }

        // ViewModels y Configuración
        public DevicesViewModel DevicesVM { get; set; }
        public ProcessViewModel ProcessesVM { get; set; }

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
            _tiaService = new TiaService(plcSoftware);

            DevicesVM = new DevicesViewModel();
            DevicesVM.Category = _configDeviceCategory;
            DevicesVM.SetTiaService(_tiaService);

            ProcessesVM = new ProcessViewModel();
            ProcessesVM.SetTiaService(_tiaService);

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

                string arguments = $"--path \"{SelectedExcelFile}\"";

                // Ejecutar proceso externo
                Siemens.Engineering.AddIn.Utilities.Process.Start(_configGlobalSettings.ExtractorExePath, arguments);

                // Esperar a que Python genere los XML
                if (WaitForPythonFiles())
                {
                    LogService.Write("Archivos XML detectados con éxito.");
                    UpdateStatus("Cargando datos en memoria...");

                    LoadAllFromFolder(AppConfigManager.ExportPath);

                    // Pasar datos al ViewModel de dispositivos
                    DevicesVM.LoadData(_engineeringCache);
                    ProcessesVM.LoadData(_engineeringCache);

                    IsDataLoaded = true;
                    UpdateStatus("Listo. Todos los módulos cargados.");
                }
                else
                {
                    UpdateStatus("Error: Tiempo de espera agotado.", true);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"CRASH EN CARGA: {ex.Message}", true);
                UpdateStatus("Crash general en el proceso.", true);
                MessageBox.Show($"{ex.Message}", "Error Crítico");
            }
            finally 
            {
                StatusService.SetBusy(false);
            }

        }




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
                    var data = DataService.LoadParameters(pathAlm);
                    _engineeringCache[_configProcessesSettings.AlarmName] = data.Cast<object>().ToList();
                }
            }           

        }


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