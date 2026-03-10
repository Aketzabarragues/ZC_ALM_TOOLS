using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;
using Microsoft.Win32;
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

        private readonly TiaPortal _tiaPortal;
        private readonly Project _project;
        public TiaPlcService _tiaPlcService;
        public TiaVciService _tiaVciService;


        // Selección Global de PLC
        public ObservableCollection<TiaTarget> PlcTargets { get; set; }
        private TiaTarget _selectedTarget;
        public TiaTarget SelectedTarget
        {
            get => _selectedTarget;
            set { _selectedTarget = value; OnPropertyChanged(); OnTargetChanged(); }
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

            // Escaneo inicial de dispositivos en el proyecto
            var scannedDevices = TiaDeviceScanner.ScanProject(_project);
            PlcTargets = new ObservableCollection<TiaTarget>(scannedDevices.Where(t => t.Type == TargetType.PLC));
            SelectedTarget = PlcTargets.FirstOrDefault();

            // Inicializamos los Módulos
            GeneratorVM = new GeneratorMainViewModel(_tiaPortal, _project, _tiaPlcService);
            VciVM = new VciMainViewModel(_tiaPortal, _project, _tiaVciService);

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
        }



        private void OnTargetChanged()
        {
            if (SelectedTarget != null && SelectedTarget.SoftwareObject is PlcSoftware plc)
            {
                // Avisamos al servicio central de que el usuario ha cambiado de PLC
                _tiaPlcService.UpdatePlc(plc);

                // Avisamos a los módulos visuales
                if (GeneratorVM != null) GeneratorVM.SelectedTarget = this.SelectedTarget;
                //if (VciVM != null) VciVM.NotifyPlcChanged(SelectedTarget.Name);
            }
        }



        // ==================================================================================================================
        // CONFIGURACIÓN Y UTILIDADES UI
        private void OpenSettingsEditor() => OpenEditor(AppConfigService.AppConfigFile, "Editando ajustes...");

        private void OpenEditor(string path, string message)
        {
            if (!File.Exists(path)) return;
            Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", $"\"{path}\"");
            StatusService.Set(message, StatusType.Ok);
        }



        // ==================================================================================================================
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