using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        public TiaHmiService _tiaHmiService;
        public TiaVciService _tiaVciService;

        public ObservableCollection<TiaTarget> PlcTargets { get; } = new ObservableCollection<TiaTarget>();
        public ObservableCollection<TiaTarget> HmiTargets { get; } = new ObservableCollection<TiaTarget>();
        public ObservableCollection<TiaTarget> ScadaTargets { get; } = new ObservableCollection<TiaTarget>();

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
        public GeneratorMainViewModel GeneratorVM { get; set; }
        public VciMainViewModel VciVM { get; set; }
        public SettingsMainViewModel SettingsVM { get; set; }


        // Propiedades de Status Bar (Globales)
        public string StatusMessage { get; set; }
        public string StatusColor { get; set; }
        public bool IsBusy { get; set; }


        // Comandos de Navegación
        public RelayCommand ShowGeneratorCommand { get; }
        public RelayCommand ShowVciCommand { get; }
        public RelayCommand ConfigSettingsCommand { get; set; }
        public RelayCommand DumpCacheCommand { get; set; }



        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public MainViewModel(TiaPortal tiaPortal, Project project)
        {
            _tiaPortal = tiaPortal;
            _project = project;

            _tiaPlcService = new TiaPlcService();
            _tiaVciService = new TiaVciService(tiaPortal, project);
            _tiaHmiService = new TiaHmiService();

            _tiaPlcService.SetTiaPortalInstance(_tiaPortal, _project);

            // Inicializamos los Módulos
            GeneratorVM = new GeneratorMainViewModel(_tiaPlcService, _tiaHmiService, PlcTargets, HmiTargets, ScadaTargets);
            VciVM = new VciMainViewModel(_tiaPortal, _project, _tiaPlcService, _tiaVciService, PlcTargets);
            SettingsVM = new SettingsMainViewModel();

            // Comandos de menú lateral
            ShowGeneratorCommand = new RelayCommand(() => CurrentView = GeneratorVM);
            ShowVciCommand = new RelayCommand(() => CurrentView = VciVM);
            ConfigSettingsCommand = new RelayCommand(() => CurrentView = SettingsVM);

            DumpCacheCommand = new RelayCommand(ExecuteDumpCache);


            // Suscripción al StatusService Global
            StatusService.OnStatusChanged += (msg, type) => {
                StatusMessage = msg;
                StatusColor = type == StatusType.Error ? "Red" : (type == StatusType.Warning ? "Orange" : "Green");
                OnPropertyChanged("StatusMessage"); OnPropertyChanged("StatusColor");
            };
            StatusService.OnBusyChanged += (busy) => { IsBusy = busy; OnPropertyChanged("IsBusy"); };

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
            // Como TiaManager ya separó el CurrentProject, es una sola línea segura:
            ProjectName = TiaManager.CurrentProject?.Name ?? "Sin proyecto abierto";
        }


        // ==================================================================================================================
        /// <summary>
        /// Metodo para escanear los dispositivos del proyecto y clasificarlos en PLC, HMI y SCADA. Luego se añaden a las listas enlazadas a los ComboBoxes de cada módulo.
        /// </summary>
        private void ScanProjectDevices()
        {
            StatusService.SetBusy(true);
            StatusService.Set("Buscando dispositivos en el proyecto...", StatusType.Warning);
            LogService.Write($"[MAIN-VM] [ScanProjectDevices] Buscando dispositivos en el proyecto...");

            try
            {
                var scannedDevices = TiaDeviceScanner.ScanProject(_project);

                // Vaciamos nuestras propias listas
                PlcTargets.Clear();
                HmiTargets.Clear();
                ScadaTargets.Clear();

                // Rellenamos nuestras propias listas (¡y los hijos se enteran automáticamente!)
                foreach (var target in scannedDevices)
                {
                    if (target.Type == TargetType.PLC) PlcTargets.Add(target);
                    else if (target.Type == TargetType.HMI) HmiTargets.Add(target);
                    else if (target.Type == TargetType.SCADA) ScadaTargets.Add(target);
                }

                SelectedTarget = PlcTargets.FirstOrDefault();
                StatusService.Set("Dispositivos escaneados.", StatusType.Ok);
                LogService.Write($"[MAIN-VM] [ScanProjectDevices] Dispositivos escaneados.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[MAIN-VM] [ScanProjectDevices] Error escaneando dispositivos: {ex.Message}", true);
                StatusService.Set("Error al escanear dispositivos.", StatusType.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo que se llama cada vez que el usuario cambia la selección del PLC. Se encarga de actualizar el servicio central de PLC, 
        /// indexar los bloques del PLC en memoria RAM y avisar a los módulos visuales para que actualicen su información en base al nuevo PLC seleccionado.
        /// </summary>
        private void OnTargetChanged()
        {
            if (SelectedTarget != null && SelectedTarget.SoftwareObject is PlcSoftware plc)
            {
                
                StatusService.Set($"Cambiando PLC a '{plc.Name}'. Indexando bloques del PLC en memoria RAM...", StatusType.Warning);


                // Forzamos a la interfaz gráfica a procesar todos los cambios visuales pendientes 
                try
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(delegate { }));
                }
                catch { /* Si da error por ser demasiado temprano en el constructor, lo ignoramos */ }

                // Avisamos al servicio central de que el usuario ha cambiado de PLC
                _tiaPlcService.UpdatePlc(plc);
                _tiaPlcService.BuildBlockCache();

                // Avisamos a los módulos visuales
                if (GeneratorVM != null) GeneratorVM.SelectedTarget = this.SelectedTarget;
                if (VciVM != null) VciVM.SelectedTarget = this.SelectedTarget;

                StatusService.Set($"PLC '{plc.Name}' enlazado e indexado correctamente.", StatusType.Ok);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para generar un volcado maestro que incluya tanto el contenido del PLC (código, datos, símbolos) como la configuración de la aplicación (AppConfig) y la caché de ingeniería (Excel).
        /// </summary>
        private void ExecuteDumpCache()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                Title = "Guardar volcado de la Caché y Configuración",
                FileName = $"Dump_{SelectedTarget?.Name}_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                StatusService.SetBusy(true);
                StatusService.Set("Exportando volcado maestro...", StatusType.Ok);

                try
                {
                    // 1. Volcado del PLC (Sobrescribe/Crea el archivo)
                    _tiaPlcService.DumpCacheToTxt(saveFileDialog.FileName);

                    // 2. Append de AppConfig y Engineering Cache
                    using (StreamWriter sw = File.AppendText(saveFileDialog.FileName))
                    {
                        sw.WriteLine("\n\n=========================================================");
                        sw.WriteLine("             VOLCADO DE APP_CONFIG (.json / .xml)        ");
                        sw.WriteLine("=========================================================");

                        var configDump = new
                        {
                            Global = AppConfigService.GetGlobalSettings(),
                            DeviceSettings = AppConfigService.GetDeviceSettings(),
                            Devices = AppConfigService.GetDeviceCategories(),
                            Process = AppConfigService.GetProcessConfig(),
                            Network = AppConfigService.GetNetworkConfig(),
                            PReal = AppConfigService.GetPRealConfig(),
                            PInt = AppConfigService.GetPIntConfig(),
                            Alarm = AppConfigService.GetAlarmConfig()
                        };
                        sw.WriteLine(JsonConvert.SerializeObject(configDump, Formatting.Indented));

                        sw.WriteLine("\n\n=========================================================");
                        sw.WriteLine("             VOLCADO DE ENGINEERING CACHÉ (EXCEL)        ");
                        sw.WriteLine("=========================================================");

                        if (GeneratorVM?._engineeringCache != null && GeneratorVM._engineeringCache.Any())
                        {
                            sw.WriteLine(JsonConvert.SerializeObject(GeneratorVM._engineeringCache, Formatting.Indented));
                        }
                        else
                        {
                            sw.WriteLine("Caché de ingeniería vacía (No se ha cargado Excel).");
                        }
                    }

                    StatusService.Set("Volcado maestro exportado correctamente.", StatusType.Ok);
                    LogService.Write($"[MAIN-VM] Volcado maestro generado en: {saveFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    LogService.Write($"[MAIN-VM] Error generando el volcado maestro: {ex.Message}", true);
                    StatusService.Set("Error al generar el volcado.", StatusType.Error);
                }
                finally
                {
                    StatusService.SetBusy(false);
                }
            }
        }




    }
}