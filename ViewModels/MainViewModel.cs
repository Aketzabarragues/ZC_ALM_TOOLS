using System;
using System.Collections.ObjectModel;
using System.Linq;
using Siemens.Engineering;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Models.Common;
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
        public GeneratorMainViewModel GeneratorVM { get; set; } // Tu VM del generador
        public VciMainViewModel VciVM { get; set; }             // Tu VM del VCI

        // Comandos de Navegación
        public RelayCommand ShowGeneratorCommand { get; }
        public RelayCommand ShowVciCommand { get; }

        // Propiedades de Status Bar (Globales)
        public string StatusMessage { get; set; }
        public string StatusColor { get; set; }
        public bool IsBusy { get; set; }

        public MainViewModel(TiaPortal tiaPortal, Project project)
        {
            _tiaPortal = tiaPortal;
            _project = project;

            // 1. Escaneo inicial de PLCs (Centralizado)
            var scannedDevices = TiaDeviceScanner.ScanProject(_project);
            PlcTargets = new ObservableCollection<TiaTarget>(scannedDevices.Where(t => t.Type == TargetType.PLC));
            SelectedTarget = PlcTargets.FirstOrDefault();

            // 2. Inicializamos los Módulos
            GeneratorVM = new GeneratorMainViewModel(_tiaPortal, _project);
            VciVM = new VciMainViewModel(_tiaPortal, _project);

            // 3. Comandos de menú lateral
            ShowGeneratorCommand = new RelayCommand(() => CurrentView = GeneratorVM);
            ShowVciCommand = new RelayCommand(() => CurrentView = VciVM);

            // 4. Suscripción al StatusService Global
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
            // Cuando cambias el PLC en el menú global, avisamos a los módulos
            if (GeneratorVM != null)
            {
                GeneratorVM.SelectedTarget = this.SelectedTarget;
            }
            
            //GeneratorVM?.NotifyPlcChanged(SelectedTarget?.Name);
            // VciVM?.NotifyPlcChanged(SelectedTarget?.Name);
        }
    }
}