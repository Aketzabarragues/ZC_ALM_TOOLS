using System;
using System.Collections.Generic;
using System.IO;
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
        private TiaPortal _tiaPortal;
        private PlcSoftware _plcSoftware;

        // Caché de datos cargados
        private Dictionary<string, List<object>> _engineeringCache = new Dictionary<string, List<object>>();
        private Dictionary<string, int> _globalConfigCache = new Dictionary<string, int>();

        // ViewModels y Configuración
        public DevicesViewModel DevicesVM { get; set; }
        public List<DeviceCategory> Categories { get; set; }

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
        public RelayCommand ConfigExtractorCommand { get; set; }
        public RelayCommand ConfigDispCommand { get; set; }



        // ==================================================================================================================
        // CONSTRUCTOR
        public MainViewModel(TiaPortal tiaPortal, PlcSoftware plcSoftware)
        {
            _tiaPortal = tiaPortal;
            _plcSoftware = plcSoftware;

            LogService.Clear();
            LogService.Write("Inicializando MainViewModel...");

            // Inicializar estado
            IsDataLoaded = false;

            // Inicializamos configuración y cargamos categorías
            AppConfigManager.InitializeEnvironment();
            Categories = AppConfigManager.GetDeviceCategories();

            // Inicializamos servicios y viewmodels
            _tiaService = new TiaService(plcSoftware);
            DevicesVM = new DevicesViewModel();

            DevicesVM.Categories = Categories;
            DevicesVM.SetTiaService(_tiaService);

            // Evento para actualizar el mensaje de estado
            StatusService.OnStatusChanged += UpdateStatus;
            StatusService.OnBusyChanged += (busy) => IsBusy = busy;

            // Seleccionamos una categoria en el viewmodel
            if (Categories.Count > 0)
                DevicesVM.SelectedCategory = Categories[0];

            StatusMessage = $"Conectado a PLC: {plcSoftware.Name}";
            LogService.Write($"Conectado a PLC: {plcSoftware.Name}");

            // Mapeo de comandos
            LoadDataCommand = new RelayCommand(LoadExcelAndGenerateJson);
            ConfigExtractorCommand = new RelayCommand(OpenSettingsEditor);
            ConfigDispCommand = new RelayCommand(OpenDeviceSettingEditor);
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
                string exePath = AppConfigManager.ReadExePath();
                if (!File.Exists(exePath))
                {
                    LogService.Write("ERROR: Extractor no encontrado", true);
                    UpdateStatus("Error: No se encuentra ZC_Extractor.exe", true);
                    MessageBox.Show($"Extractor no encontrado en:\n{exePath}", "Error de configuración");
                    return;
                }


                ClearExportFolder(AppConfigManager.ExportPath);

                LogService.Write("Lanzando proceso Python...");
                UpdateStatus("Ejecutando extractor Python...");

                string arguments = $"--path \"{SelectedExcelFile}\"";

                // Ejecutar proceso externo
                Siemens.Engineering.AddIn.Utilities.Process.Start(exePath, arguments);

                // Esperar a que Python genere los XML
                if (WaitForPythonFiles())
                {
                    LogService.Write("Archivos XML detectados con éxito.");
                    UpdateStatus("Cargando datos en memoria...");

                    LoadAllFromFolder(AppConfigManager.ExportPath);

                    // Pasar datos al ViewModel de dispositivos
                    DevicesVM.LoadData(_engineeringCache, _globalConfigCache);

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

            for (int i = 0; i < 150; i++) // Timeout aprox 30s (150 * 200ms)
            {
                bool missing = false;
                string missingFile = "";

                // Comprobar XML de cada categoría
                foreach (var cat in Categories)
                {
                    string checkPath = Path.Combine(AppConfigManager.ExportPath, cat.XmlFile);
                    if (!File.Exists(checkPath))
                    {
                        missing = true;
                        missingFile = cat.XmlFile;
                        break;
                    }
                }

                // Comprobar XML de configuración global
                if (!missing && !File.Exists(AppConfigManager.DeviceDataConfig))
                {
                    missing = true;
                    missingFile = "config_disp.xml";
                }

                if (!missing) return true; // Todo encontrado

                if (i % 10 == 0) LogService.Write($"Esperando {missingFile}...");


                Thread.Sleep(200);

                UpdateStatusFrame();
            }
            return false;
        }


        // Metodo para cargar todos los archivos desde una carpeta
        private void LoadAllFromFolder(string folderPath)
        {
            _engineeringCache.Clear();
            _globalConfigCache.Clear();

            // Cargar configuración global usando DataService
            _globalConfigCache = DataService.LoadGlobalConfig(AppConfigManager.DeviceDataConfig);

            // Cargar dispositivos de cada categoría
            foreach (var cat in Categories)
            {
                string filePath = Path.Combine(folderPath, cat.XmlFile);
                if (File.Exists(filePath))
                {
                    _engineeringCache[cat.Name] = DataService.LoadDispCategoryData(filePath, cat);
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
        private void OpenSettingsEditor() => OpenEditor(AppConfigManager.SettingsFile, "Editando ajustes...");
        private void OpenDeviceSettingEditor() => OpenEditor(AppConfigManager.DeviceSettingsFile, "Editando configuracion dispositivos...");

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