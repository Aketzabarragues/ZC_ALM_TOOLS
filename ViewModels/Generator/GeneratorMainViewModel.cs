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
        private readonly TargetStateService _targetStateService;

        public ObservableCollection<TiaTarget> PlcTargets => _targetStateService.PlcTargets;
        public ObservableCollection<TiaTarget> HmiTargets => _targetStateService.HmiTargets;
        public ObservableCollection<TiaTarget> ScadaTargets => _targetStateService.ScadaTargets;

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

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;
        private readonly IAppConfigService _appConfigService;
        private readonly IDataService _dataService;

        // Comandos
        public AsyncRelayCommand LoadDataCommand { get; }


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public GeneratorMainViewModel(
            TargetStateService targetStateService,
            DevicesViewModel devicesVM,
            ParamsAlarmsViewModel paramsAlarmsVM,
            ProcessGeneratorViewModel processGeneratorVM, 
            ILogService logService, IStatusService statusService,
            IAppConfigService appConfigService,
            IDataService dataService)
        {


            _targetStateService = targetStateService;

            _logService = logService;
            _statusService = statusService;
            _appConfigService = appConfigService;
            _dataService = dataService;


            _logService.Clear();
            _logService.Write("[GENERATOR-MAIN-VM] [GeneratorMainViewModel] Inicializando GeneratorMainViewModel...");

            // Seleccionamos el primer PLC por defecto para la comparación
            SelectedTarget = PlcTargets.FirstOrDefault(t => t.Type == TargetType.PLC);

            // Inicializamos configuración y cargamos categorías
            _configNetworkSettings = _appConfigService.GetNetworkConfig();
            _configProcessesSettings = _appConfigService.GetProcessConfig();
            _configDeviceSettings = _appConfigService.GetDeviceSettings();
            _configGlobalSettings = _appConfigService.GetGlobalSettings();
            _configDeviceCategories = _appConfigService.GetDeviceCategories();

            // Inicializamos viewmodels
            DevicesVM = devicesVM;
            ParamsAlarmsVM = paramsAlarmsVM;
            ProcessGeneratorVM = processGeneratorVM;

            // Mapeo de comando asíncrono
            LoadDataCommand = new AsyncRelayCommand(LoadExcelAndGenerateJson);

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

                // Como StatusService ahora también loguea, esto es suficiente
                _statusService.Set($"[GENERATOR-MAIN-VM] [NotifyPlcChanged] Objetivo cambiado a: {SelectedTarget.Name}", StatusType.Ok);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer y generar JSON con los datos de Excel
        /// </summary>
        private async Task LoadExcelAndGenerateJson()
        {
            _logService.Write("[GENERATOR-MAIN-VM] [LoadExcelAndGenerateJson] Iniciando lectura excel.");

            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsm;*.xlsx",
                    Title = "Selecciona el archivo de definición"
                };

                if (openFileDialog.ShowDialog() != true) return;

                SelectedExcelFile = openFileDialog.FileName;
                _logService.Write($"[GENERATOR-MAIN-VM] [LoadExcelAndGenerateJson] Excel seleccionado: {SelectedExcelFile}");

                _statusService.Set("[GENERATOR-MAIN-VM] [LoadExcelAndGenerateJson] Leyendo Excel en memoria RAM...", StatusType.Ok);

                // Lanzar extracción asíncrona de datos
                await ReadExcelDataAsync(SelectedExcelFile);

            }
            catch (Exception ex)
            {
                _statusService.Set($"[GENERATOR-MAIN-VM] [LoadExcelAndGenerateJson] Error leyendo Excel: {ex.Message}\n{ex.StackTrace}", StatusType.Error);
                MessageBox.Show($"{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo asíncrono para leer los datos del Excel y almacenarlos en el caché de ingeniería, inyectando la configuración desde JSON.
        /// </summary>
        private async Task ReadExcelDataAsync(string excelPath)
        {
            _engineeringCache.Clear();

            // Cargamos categorías de Dispositivos (Concurrentes y Thread-safe)
            var deviceTasks = _configDeviceCategories.Select(async cat =>
            {
                var data = await _dataService.LoadDispCategoryDataAsync(excelPath, cat);
                lock (_engineeringCache) { _engineeringCache[cat.Name] = data; }
            }).ToList();

            await Task.WhenAll(deviceTasks);

            // Cargamos Config_Disp (Límites de arrays en PLC extraídos de nombres definidos)
            if (_configDeviceSettings != null)
            {
                var data = await _dataService.LoadDeviceNMaxAsync(excelPath, _configDeviceSettings);
                lock (_engineeringCache) { _engineeringCache[_configDeviceSettings.Name] = data.Cast<object>().ToList(); }
            }

            // Cargamos Procesos, Parámetros y Alarmas inyectando configuración de JSON
            if (_configProcessesSettings != null)
            {
                // Usamos los nombres definidos en el JSON (ej: _configProcessesSettings.ProcessName)
                var processData = await _dataService.LoadProcessAsync(excelPath, _configProcessesSettings.ExcelSheet, _configProcessesSettings.ExcelTable);
                lock (_engineeringCache) { _engineeringCache[_configProcessesSettings.Name] = processData.Cast<object>().ToList(); }

                var pRealCfg = _appConfigService.GetPRealConfig();
                if (pRealCfg != null)
                {
                    var prealData = await _dataService.LoadParametersAsync(excelPath, pRealCfg.ExcelSheet, pRealCfg.ExcelTable);
                    lock (_engineeringCache) { _engineeringCache[pRealCfg.Name] = prealData.Cast<object>().ToList(); }
                }

                var pIntCfg = _appConfigService.GetPIntConfig();
                if (pIntCfg != null)
                {
                    var pintData = await _dataService.LoadParametersAsync(excelPath, pIntCfg.ExcelSheet, pIntCfg.ExcelTable);
                    lock (_engineeringCache) { _engineeringCache[pIntCfg.Name] = pintData.Cast<object>().ToList(); }
                }

                var almCfg = _appConfigService.GetAlarmConfig();
                if (almCfg != null)
                {
                    var alarmData = await _dataService.LoadAlarmsAsync(excelPath, almCfg.ExcelSheet, almCfg.ExcelTable);
                    lock (_engineeringCache) { _engineeringCache[almCfg.Name] = alarmData.Cast<object>().ToList(); }
                }
            }

            // Conexiones (Topología de Red)
            if (_configNetworkSettings != null)
            {

                var connData = await _dataService.LoadConectionsAsync(excelPath, _configNetworkSettings.ExcelSheet, _configNetworkSettings.ExcelTable);
                lock (_engineeringCache) { _engineeringCache[_configNetworkSettings.Name] = connData.Cast<object>().ToList(); }
            }

            // Volvemos al Hilo UI de WPF para enlazar los ViewModels
            Application.Current.Dispatcher.Invoke(() =>
            {
                DevicesVM.LoadData(_engineeringCache, _configDeviceSettings);
                ParamsAlarmsVM.LoadData(_engineeringCache, _configProcessesSettings);
                ProcessGeneratorVM.LoadData(_engineeringCache, _configProcessesSettings, _configGlobalSettings, _configNetworkSettings);

                IsDataLoaded = true;

                _statusService.Set("[GENERATOR-MAIN-VM] [ReadExcelDataAsync] Datos cargados desde el Excel correctamente.", StatusType.Ok);
            });
        }

    }
}