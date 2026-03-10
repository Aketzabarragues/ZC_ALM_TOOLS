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


        // ==================================================================================================================
        // CONSTRUCTOR
        public VciMainViewModel(TiaPortal tiaPortal, Project project, TiaVciService tiaVciService)
        {
            LogService.Write("[VCI-MAIN-VM] [VciMainViewModel] Inicializando VciMainViewModel...");

            _tiaPortal = tiaPortal;
            _tiaproject = project;

            // Inicializamos servicios
            _tiaVciService = tiaVciService;

            // Inicializamos viewmodels hijos
            MappingVM = new VciMappingViewModel(_tiaVciService);
            AuditVM = new VciAuditViewModel();

        }


    }
}