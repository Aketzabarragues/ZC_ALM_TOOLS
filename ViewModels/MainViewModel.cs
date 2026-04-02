using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;
using ZC_ALM_TOOLS.ViewModels.Generator;
using ZC_ALM_TOOLS.ViewModels.Settings;
using ZC_ALM_TOOLS.ViewModels.Vci;

namespace ZC_ALM_TOOLS.ViewModels
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel principal de la aplicación, encargado de gestionar la conexión con Tia Portal, mantener la información global del proyecto y los dispositivos, 
    /// y coordinar la navegación entre los módulos visuales (Generador, VCI, Configuración).
    /// </summary>
    public class MainViewModel : ObservableObject
    {

        // =================================================================================================================
        // Tia portal
        private readonly TiaPortal _tiaPortal;
        private readonly Project _project;
        public TiaPlcService _tiaPlcService;
        private readonly TargetStateService _targetStateService;

        public ObservableCollection<TiaTarget> PlcTargets => _targetStateService.PlcTargets;
        public ObservableCollection<TiaTarget> HmiTargets => _targetStateService.HmiTargets;
        public ObservableCollection<TiaTarget> ScadaTargets => _targetStateService.ScadaTargets;

        // Selección Global de PLC
        private TiaTarget _selectedTarget;
        public TiaTarget SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                _selectedTarget = value;
                OnPropertyChanged(); OnTargetChanged();
            }
        }

        // Información del Proyecto
        private string _projectName = "Desconectado";
        public string ProjectName
        {
            get => _projectName;
            set
            {
                _projectName = value;
                OnPropertyChanged(nameof(ProjectName));
            }
        }

        // Navegación de Módulos
        private object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }


        // ViewModels de los Módulos
        public GeneratorMainViewModel GeneratorVM { get; }
        public VciMainViewModel VciVM { get; }
        public SettingsMainViewModel SettingsVM { get; }


        private readonly IStatusService _statusService;
        private readonly IAppConfigService _appConfigService;

        // Propiedades de Status Bar (Globales)
        public string StatusMessage { get; set; }
        public string StatusColor { get; set; }
        public bool IsBusy { get; set; }


        // Comandos
        public RelayCommand ShowGeneratorCommand { get; }
        public RelayCommand ShowVciCommand { get; }
        public RelayCommand ConfigSettingsCommand { get; set; }
        public RelayCommand ReloadCacheCommand { get; set; }

        // Comandos asincronos
        public AsyncRelayCommand DumpCacheCommand { get; set; }


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public MainViewModel(TiaPortal tiaPortal, Project project,
            TiaPlcService tiaPlcService,
            TargetStateService targetStateService,
            GeneratorMainViewModel generatorVM,
            VciMainViewModel vciVM,
            SettingsMainViewModel settingsVM,
            IStatusService statusService,
            IAppConfigService appConfigService)
        {

            _tiaPortal = tiaPortal;
            _project = project;
            _tiaPlcService = tiaPlcService;
            _targetStateService = targetStateService;

            _statusService = statusService;
            _appConfigService = appConfigService;

            // Inicializamos los Módulos
            GeneratorVM = generatorVM;
            VciVM = vciVM;
            SettingsVM = settingsVM;

            // Comandos de menú lateral
            ShowGeneratorCommand = new RelayCommand(() => CurrentView = GeneratorVM);
            ShowVciCommand = new RelayCommand(() => CurrentView = VciVM);
            ConfigSettingsCommand = new RelayCommand(() => CurrentView = SettingsVM);

            // Comandos
            ReloadCacheCommand = new RelayCommand(OnTargetChanged);
            DumpCacheCommand = new AsyncRelayCommand(ExecuteDumpCache);

            // Suscripción al StatusService Global
            _statusService.OnStatusChanged += (msg, type) => {
                StatusMessage = msg;
                StatusColor = type == StatusType.Error ? "Red" : (type == StatusType.Warning ? "Orange" : "Green");
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(StatusColor));
            };
            _statusService.OnBusyChanged += (busy) => { IsBusy = busy; OnPropertyChanged(nameof(IsBusy)); };

            // Vista por defecto al abrir
            CurrentView = GeneratorVM;

            LoadProjectInfo();
            ScanProjectDevices();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para cargar la información del proyecto (nombre) y mostrarla en la barra de estado
        /// </summary>
        private void LoadProjectInfo()
        {
            ProjectName = _project?.Name ?? "Sin proyecto abierto";
        }


        // ==================================================================================================================
        /// <summary>
        /// Metodo para escanear los dispositivos del proyecto y clasificarlos en PLC, HMI y SCADA. 
        /// Luego se añaden a las listas enlazadas a los ComboBoxes de cada módulo.
        /// </summary>
        private void ScanProjectDevices()
        {
            // Nota: Este método se llama en el constructor, no desde un comando. 
            // Lo dejamos de momento con el control manual de IsBusy para no complicar la inicialización.
            _statusService.SetBusy(true);
            _statusService.Set("[MAIN-VM] [ScanProjectDevices] Buscando dispositivos en el proyecto...", StatusType.Warning);

            try
            {
                var scannedDevices = TiaDeviceScanner.ScanProject(_project);

                PlcTargets.Clear();
                HmiTargets.Clear();
                ScadaTargets.Clear();

                foreach (var target in scannedDevices)
                {
                    if (target.Type == TargetType.PLC) PlcTargets.Add(target);
                    else if (target.Type == TargetType.HMI) HmiTargets.Add(target);
                    else if (target.Type == TargetType.SCADA) ScadaTargets.Add(target);
                }

                SelectedTarget = PlcTargets.FirstOrDefault();
                _statusService.Set("[MAIN-VM] [ScanProjectDevices] Dispositivos escaneados correctamente.", StatusType.Ok);
            }
            catch (Exception ex)
            {
                _statusService.Set($"[MAIN-VM] [ScanProjectDevices] Error al escanear dispositivos: {ex.Message}", StatusType.Error);
            }
            finally
            {
                _statusService.SetBusy(false);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo que se llama cada vez que el usuario cambia la selección del PLC. 
        /// </summary>
        private void OnTargetChanged()
        {
            if (SelectedTarget != null && SelectedTarget.SoftwareObject is PlcSoftware plc)
            {
                _statusService.Set($"[MAIN-VM] [OnTargetChanged] Cambiando PLC a '{plc.Name}'. Indexando bloques del PLC en memoria RAM...", StatusType.Warning);

                try
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(delegate { }));
                }
                catch { }

                // Avisamos al servicio central de que el usuario ha cambiado de PLC
                _tiaPlcService.UpdatePlc(plc);
                _tiaPlcService.BuildBlockCache();

                // Avisamos a los módulos visuales
                if (GeneratorVM != null) GeneratorVM.SelectedTarget = this.SelectedTarget;
                if (VciVM != null) VciVM.SelectedTarget = this.SelectedTarget;

                _statusService.Set($"[MAIN-VM] [OnTargetChanged] PLC '{plc.Name}' enlazado e indexado correctamente.", StatusType.Ok);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para generar un volcado maestro que incluya el contenido del PLC, la configuración y la caché de ingeniería.
        /// </summary>
        private async Task ExecuteDumpCache()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                Title = "Guardar volcado de la Caché y Configuración",
                FileName = $"Dump_{SelectedTarget?.Name}_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                _statusService.Set("[MAIN-VM] Exportando volcado maestro...", StatusType.Ok);
                await Task.Delay(50); // Pausa visual

                try
                {
                    // Capturamos el nombre de archivo y la caché de UI antes de irnos a segundo plano
                    string fileName = saveFileDialog.FileName;
                    var engineeringCache = GeneratorVM?._engineeringCache;

                    // Nos vamos a un hilo secundario para no congelar la app al serializar y escribir
                    await Task.Run(() =>
                    {
                        // 1. Volcado del PLC (Sobrescribe/Crea el archivo)
                        _tiaPlcService.DumpCacheToTxt(fileName);

                        // 2. Append de AppConfig y Engineering Cache
                        using (StreamWriter sw = File.AppendText(fileName))
                        {
                            sw.WriteLine("\n\n=========================================================");
                            sw.WriteLine("             VOLCADO DE APP_CONFIG (.json / .xml)        ");
                            sw.WriteLine("=========================================================");

                            var configDump = new
                            {
                                Global = _appConfigService.GetGlobalSettings(),
                                DeviceSettings = _appConfigService.GetDeviceSettings(),
                                Devices = _appConfigService.GetDeviceCategories(),
                                Process = _appConfigService.GetProcessConfig(),
                                Network = _appConfigService.GetNetworkConfig(),
                                PReal = _appConfigService.GetPRealConfig(),
                                PInt = _appConfigService.GetPIntConfig(),
                                Alarm = _appConfigService.GetAlarmConfig()
                            };
                            sw.WriteLine(JsonConvert.SerializeObject(configDump, Formatting.Indented));

                            sw.WriteLine("\n\n=========================================================");
                            sw.WriteLine("             VOLCADO DE ENGINEERING CACHÉ (EXCEL)        ");
                            sw.WriteLine("=========================================================");

                            if (engineeringCache != null && engineeringCache.Any())
                            {
                                sw.WriteLine(JsonConvert.SerializeObject(engineeringCache, Formatting.Indented));
                            }
                            else
                            {
                                sw.WriteLine("Caché de ingeniería vacía (No se ha cargado Excel).");
                            }
                        }
                    });

                    _statusService.Set($"[MAIN-VM] Volcado maestro exportado correctamente en: {fileName}", StatusType.Ok);
                }
                catch (Exception ex)
                {
                    _statusService.Set($"[MAIN-VM] Error al generar el volcado: {ex.Message}", StatusType.Error);
                }
            }
        }


    }
}