using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel que gestiona la pestaña de auditoria de VCI
    /// </summary>
    public class VciAuditViewModel : ObservableObject
    {

        private readonly TiaPlcService _tiaPlcService;

        private ObservableCollection<CachedPlcBlock> _blocks;
        public ObservableCollection<CachedPlcBlock> Blocks
        {
            get => _blocks;
            set { _blocks = value; OnPropertyChanged(); }
        }

        public ICommand LoadBlocksCommand { get; }
        public ICommand UpdateDependenciesCommand { get; }

        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciAuditViewModel(TiaPlcService tiaPlcService)
        {
            _tiaPlcService = tiaPlcService;
            Blocks = new ObservableCollection<CachedPlcBlock>();

            LoadBlocksCommand = new RelayCommand(ExecuteLoadBlocks);
            UpdateDependenciesCommand = new RelayCommand(ExecuteUpdateDependencies, CanExecuteUpdateDependencies);
        }



        private void ExecuteLoadBlocks()
        {
            try
            {
                var allBlocks = _tiaPlcService.GetAllBlocks();
                Blocks = new ObservableCollection<CachedPlcBlock>(allBlocks.OrderBy(b => b.SimpleType).ThenBy(b => b.Number));
                StatusService.Set($"Se han cargado {Blocks.Count} bloques en auditoría.", StatusType.Ok);
            }
            catch (Exception ex)
            {
                LogService.Write($"[VCI-AUDIT] Error cargando bloques: {ex.Message}", true);
            }
        }

        private bool CanExecuteUpdateDependencies()
        {
            // Solo se puede pulsar el botón si hay al menos un bloque válido seleccionado
            return Blocks != null && Blocks.Any(b => b.IsSelected && b.CanUpdateDependencies);
        }

        private async void ExecuteUpdateDependencies()
        {
            var selectedBlocks = Blocks.Where(b => b.IsSelected && b.CanUpdateDependencies).ToList();
            if (!selectedBlocks.Any()) return;

            StatusService.Set($"Iniciando actualización de dependencias de {selectedBlocks.Count} bloques...", StatusType.Warning);

            await Task.Delay(50);

            // Ejecutamos en segundo plano para que la UI no se quede "No responde"
            bool success =  await _tiaPlcService.UpdateMassiveSclDependencies(selectedBlocks);

            if (success)
            {
                StatusService.Set("Dependencias actualizadas y bloques re-importados con éxito.", StatusType.Ok);
            }
            else
            {
                StatusService.Set("Error durante la actualización de dependencias. Revisa el log.", StatusType.Error);
            }
        }


    }
}