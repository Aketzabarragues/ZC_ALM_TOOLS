using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.Generator;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel principal del modulo de generacion
    /// </summary>
    public class GeneratorMainViewModel : ObservableObject
    {

        // =================================================================================================================
        // Tia portal
        private readonly TiaPlcService _tiaPlcService;
        private readonly TiaHmiService _tiaHmiService;

        public ObservableCollection<TiaTarget> PlcTargets { get; }
        public ObservableCollection<TiaTarget> HmiTargets { get; }
        public ObservableCollection<TiaTarget> ScadaTargets { get; }

        private TiaTarget _selectedTarget;
        public TiaTarget SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                _selectedTarget = value;
                OnPropertyChanged();
                NotifyPlcChanged();
            }
        }

        // Caché de datos cargados
        //private readonly Dictionary<string, List<object>> _engineeringCache = new Dictionary<string, List<object>>();
        public Dictionary<string, List<object>> _engineeringCache { get; } = new Dictionary<string, List<object>>();

        // Cache de configuracion xml
        private ConfigNetworkSettings _configNetworkSettings;
        private ConfigProcessSettings _configProcessesSettings;
        private ConfigDeviceSettings _configDeviceSettings;
        private ConfigGlobalSettings _configGlobalSettings;
        private List<ConfigDeviceCategory> _configDeviceCategories;


        // ViewModels y Configuración
        public DevicesViewModel DevicesVM { get; set; }
        public ParamsAlarmsViewModel ParamsAlarmsVM { get; set; }
        public ProcessGeneratorViewModel ProcessGeneratorVM { get; set; }


        // Variable que indica si se ha cargado un Excel correctamente
        private bool _isDataLoaded;
        public bool IsDataLoaded
        {
            get => _isDataLoaded;
            set { _isDataLoaded = value; OnPropertyChanged(); }
        }

        private string _selectedExcelFile;
        public string SelectedExcelFile
        {
            get => _selectedExcelFile;
            set { _selectedExcelFile = value; OnPropertyChanged(); }
        }

        public RelayCommand LoadDataCommand { get; }




        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public GeneratorMainViewModel(TiaPlcService tiaPlcService,
                                      TiaHmiService tiaHmiService,
                                      ObservableCollection<TiaTarget> plcTargets,
                                      ObservableCollection<TiaTarget> hmiTargets,
                                      ObservableCollection<TiaTarget> scadaTargets)
        {
            
            LogService.Clear();
            LogService.Write("[MAIN-VM] [MainViewModel] Inicializando MainViewModel...");


            // Buscamos todos los dispositivos del proyecto         
            PlcTargets = plcTargets;
            HmiTargets = hmiTargets;
            ScadaTargets = scadaTargets;

            // Inicializamos servicios de Tia portal
            _tiaPlcService = tiaPlcService;
            _tiaHmiService = tiaHmiService;


            // Seleccionamos el primer PLC por defecto para la comparación
            SelectedTarget = PlcTargets.FirstOrDefault(t => t.Type == TargetType.PLC);

            // Inicializamos configuración y cargamos categorías
            AppConfigService.InitializeEnvironment();
            _configNetworkSettings = AppConfigService.GetNetworkConfig();
            _configProcessesSettings = AppConfigService.GetProcessConfig();
            _configDeviceSettings = AppConfigService.GetDeviceSettings();
            _configGlobalSettings = AppConfigService.GetGlobalSettings();
            _configDeviceCategories = AppConfigService.GetDeviceCategories();

            // Inicializamos viewmodels
            DevicesVM = new DevicesViewModel(_tiaPlcService, _tiaHmiService, _configDeviceCategories, HmiTargets);
            ParamsAlarmsVM = new ParamsAlarmsViewModel(_tiaPlcService);

            ProcessGeneratorVM = new ProcessGeneratorViewModel(_tiaPlcService, _tiaHmiService, HmiTargets);
            ProcessGeneratorVM.LoadTemplates(_configGlobalSettings);


            // Seleccionamos una categoria en el viewmodel
            if (_configDeviceCategories.Count > 0)
                DevicesVM.SelectedCategory = _configDeviceCategories[0];

            // Mapeo de comandos
            LoadDataCommand = new RelayCommand(LoadExcelAndGenerateJson);

            // Inicializar estado
            IsDataLoaded = false;
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar el PLC de trabajo cuando el usuario cambia la selección
        /// </summary>
        private void NotifyPlcChanged()
        {
            if (SelectedTarget != null && SelectedTarget.SoftwareObject is PlcSoftware plc)
            {
                DevicesVM?.NotifyPlcChanged(SelectedTarget.Name);
                ParamsAlarmsVM?.NotifyPlcChanged(SelectedTarget.Name);
                ProcessGeneratorVM?.NotifyPlcChanged(SelectedTarget.Name);

                StatusService.Set($"Objetivo cambiado a: {SelectedTarget.Name}", StatusType.Ok);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer y generar JSON con los datos de Excel
        /// </summary>
        private async void LoadExcelAndGenerateJson()
        {
            LogService.Write("[MAIN-VM] [LoadExcelAndGenerateJson] Iniciando lectura excel.");

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
                LogService.Write($"[MAIN-VM] [LoadExcelAndGenerateJson] Excel seleccionado: {SelectedExcelFile}");

                StatusService.Set("Leyendo Excel en memoria RAM...", StatusType.Ok);

                // Lanzar extracción asíncrona de datos
                await ReadExcelDataAsync(SelectedExcelFile);

            }
            catch (Exception ex)
            {
                LogService.Write($"[MAIN-VM] [LoadExcelAndGenerateJson] Error: {ex.Message}\n{ex.StackTrace}", true);
                StatusService.Set("Error general leyendo Excel.", StatusType.Error);
                MessageBox.Show($"{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }

        private async Task ReadExcelDataAsync(string excelPath)
        {
            _engineeringCache.Clear();

            // 1. Cargamos categorías de Dispositivos (Concurrentes y Thread-safe)
            var deviceTasks = _configDeviceCategories.Select(async cat =>
            {
                var data = await DataService.LoadDispCategoryDataAsync(excelPath, cat);
                lock (_engineeringCache) { _engineeringCache[cat.Name] = data; }
            }).ToList();

            await Task.WhenAll(deviceTasks);

            // 2. Cargamos Config_Disp (Límites de arrays en PLC extraídos de nombres definidos)
            if (_configDeviceSettings != null)
            {
                var data = await DataService.LoadDeviceNMaxAsync(excelPath, _configDeviceSettings);
                lock (_engineeringCache) { _engineeringCache["CONFIG_LIMITS"] = data.Cast<object>().ToList(); }
            }

            // 3. Cargamos Procesos, Parámetros y Alarmas inyectando configuración de JSON
            if (_configProcessesSettings != null)
            {
                // Usamos los nombres definidos en el JSON (ej: _configProcessesSettings.ProcessName)
                var processData = await DataService.LoadProcessAsync(excelPath, _configProcessesSettings.ExcelSheet, _configProcessesSettings.ExcelTable);
                lock (_engineeringCache) { _engineeringCache[_configProcessesSettings.ProcessName] = processData.Cast<object>().ToList(); }

                var pRealCfg = AppConfigService.GetPRealConfig();
                if (pRealCfg != null)
                {
                    var prealData = await DataService.LoadParametersAsync(excelPath, pRealCfg.ExcelSheet, pRealCfg.ExcelTable);
                    lock (_engineeringCache) { _engineeringCache[_configProcessesSettings.PRealName] = prealData.Cast<object>().ToList(); }
                }

                var pIntCfg = AppConfigService.GetPIntConfig();
                if (pIntCfg != null)
                {
                    var pintData = await DataService.LoadParametersAsync(excelPath, pIntCfg.ExcelSheet, pIntCfg.ExcelTable);
                    lock (_engineeringCache) { _engineeringCache[_configProcessesSettings.PIntName] = pintData.Cast<object>().ToList(); }
                }

                var almCfg = AppConfigService.GetAlarmConfig();
                if (almCfg != null)
                {
                    var alarmData = await DataService.LoadAlarmsAsync(excelPath, almCfg.ExcelSheet, almCfg.ExcelTable);
                    lock (_engineeringCache) { _engineeringCache[_configProcessesSettings.AlarmName] = alarmData.Cast<object>().ToList(); }
                }
            }

            // 4. Conexiones (Topología de Red)
            if (_configNetworkSettings != null)
            {
                // Nota: Si en el futuro agregas NetworkName al JSON, reemplaza este string también
                var connData = await DataService.LoadConectionsAsync(excelPath, _configNetworkSettings.ExcelSheet, _configNetworkSettings.ExcelTable);
                lock (_engineeringCache) { _engineeringCache["Conexiones"] = connData.Cast<object>().ToList(); }
            }

            // Volvemos al Hilo UI de WPF para enlazar los ViewModels
            Application.Current.Dispatcher.Invoke(() =>
            {
                DevicesVM.LoadData(_engineeringCache, _configDeviceSettings);
                ParamsAlarmsVM.LoadData(_engineeringCache, _configProcessesSettings);
                ProcessGeneratorVM.LoadData(_engineeringCache, _configProcessesSettings, _configGlobalSettings, _configNetworkSettings);

                IsDataLoaded = true;
                StatusService.Set("Datos cargados desde el Excel correctamente.", StatusType.Ok);
                LogService.Write($"[MAIN-VM] [ReadExcelDataAsync] Datos cargados desde el Excel correctamente."); 
            });
        }

    }
}