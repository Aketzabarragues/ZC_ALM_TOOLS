using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;
using ZC_ALM_TOOLS.ViewModels.Generator; // Ajusta según tus namespaces
using ZC_ALM_TOOLS.ViewModels.Vci;

namespace ZC_ALM_TOOLS.ViewModels
{
    public class MainViewModel : ObservableObject
    {

        // =================================================================================================================
        // Tia portal
        private readonly TiaPortal _tiaPortal;
        private readonly Project _project;
        public TiaPlcService _tiaPlcService;
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



        // Propiedades de Status Bar (Globales)
        public string StatusMessage { get; set; }
        public string StatusColor { get; set; }
        public bool IsBusy { get; set; }


        // Comandos de Navegación
        public RelayCommand ShowGeneratorCommand { get; }
        public RelayCommand ShowVciCommand { get; }
        public RelayCommand ConfigSettingsCommand { get; set; }
        public RelayCommand DumpCacheCommand { get; set; }



        // =================================================================================================================
        // CONSTRUCTOR
        public MainViewModel(TiaPortal tiaPortal, Project project)
        {
            _tiaPortal = tiaPortal;
            _project = project;

            _tiaPlcService = new TiaPlcService();
            _tiaVciService = new TiaVciService(project);


            // Inicializamos los Módulos
            GeneratorVM = new GeneratorMainViewModel(_tiaPlcService, PlcTargets, HmiTargets, ScadaTargets);
            VciVM = new VciMainViewModel(_tiaPortal, _project, _tiaVciService, PlcTargets);

            // Comandos de menú lateral
            ShowGeneratorCommand = new RelayCommand(() => CurrentView = GeneratorVM);
            ShowVciCommand = new RelayCommand(() => CurrentView = VciVM);
            ConfigSettingsCommand = new RelayCommand(OpenSettingsEditor);
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

            ScanProjectDevices();
        }



        // =================================================================================================================
        // Actualizar el listado de equipos en el proyecto
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
        


        // =================================================================================================================
        // Actualizar el PLC al cambio en el combobox
        private  void OnTargetChanged()
        {
            if (SelectedTarget != null && SelectedTarget.SoftwareObject is PlcSoftware plc)
            {
                
                StatusService.Set($"Cambiando PLC a '{plc.Name}'. Indexando bloques del PLC en memoria RAM...", StatusType.Warning);


                // 2. EL TRUCO PARA WFP EN ADD-INS: 
                // Forzamos a la interfaz gráfica a procesar todos los cambios visuales pendientes 
                // (como cerrar el menú del ComboBox y mostrar el texto de arriba) en este mismo milisegundo,
                // sin cambiar de hilo y sin usar Task.Delay.
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
                //if (VciVM != null) VciVM.NotifyPlcChanged(SelectedTarget.Name);

                StatusService.Set($"PLC '{plc.Name}' enlazado e indexado correctamente.", StatusType.Ok);
            }
        }



        // =================================================================================================================
        // Abrir editor de configuracion
        private void OpenSettingsEditor() => OpenEditor(AppConfigService.AppConfigFile, "Editando ajustes...");

        private void OpenEditor(string path, string message)
        {
            if (!File.Exists(path)) return;
            Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", $"\"{path}\"");
            StatusService.Set(message, StatusType.Ok);
        }



        // =================================================================================================================
        // Exportar volcado de caché a TXT
        private void ExecuteDumpCache()
        {


            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                Title = "Guardar volcado de la Caché",
                FileName = $"TiaCacheDump_{SelectedTarget?.Name}.txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                StatusService.SetBusy(true);
                StatusService.Set("Exportando volcado de caché...", StatusType.Ok);
                _tiaPlcService.DumpCacheToTxt(saveFileDialog.FileName);
                StatusService.Set("Caché exportada correctamente.", StatusType.Ok);
                StatusService.SetBusy(false);
            }
        }




    }
}