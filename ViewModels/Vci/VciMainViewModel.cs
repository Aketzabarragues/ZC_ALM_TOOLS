using Siemens.Engineering;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Services;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Vci
{
    public class VciMainViewModel : ObservableObject
    {

        // =================================================================================================================
        // Tia portal
        private readonly Project _tiaproject;
        private readonly TiaPortal _tiaPortal;
        private TiaVciService _tiaVciService;

        // ViewModels Hijos
        public VciMappingViewModel MappingVM { get; set; }
        public VciAuditViewModel AuditVM { get; set; }

        // =================================================================================================================
        // Variables de UI (Status Bar)

        // Variable que indica que esta ejecutandose algo
        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        // Mensaje de estado
        private string _statusMessage;
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // Color de estado
        private string _statusColor = "Black";
        public string StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }


        // ==================================================================================================================
        // CONSTRUCTOR
        public VciMainViewModel(TiaPortal tiaPortal, Project project)
        {
            LogService.Write("[VCI-MAIN-VM] [VciMainViewModel] Inicializando VciMainViewModel...");

            _tiaPortal = tiaPortal;
            _tiaproject = project;

            // Inicializamos servicios
            _tiaVciService = new TiaVciService(_tiaproject);

            // Inicializamos viewmodels hijos
            MappingVM = new VciMappingViewModel(_tiaVciService);
            AuditVM = new VciAuditViewModel();

            // Eventos para actualizar la barra de estado
            StatusService.OnStatusChanged += UpdateStatus;
            StatusService.OnBusyChanged += (busy) => IsBusy = busy;

            // Inicializar estado
            UpdateStatus("Herramienta VCI inicializada correctamente.", StatusType.Ok);
        }


        // ==================================================================================================================
        // CONFIGURACIÓN Y UTILIDADES UI

        // Metodo para actualizar la barra de estado
        private void UpdateStatus(string message, StatusType type = StatusType.Ok)
        {
            StatusMessage = message;

            if (type == StatusType.Ok)
                StatusColor = "Black";
            else if (type == StatusType.Warning)
                StatusColor = "Orange";
            else if (type == StatusType.Error)
                StatusColor = "Red";
        }
    }
}