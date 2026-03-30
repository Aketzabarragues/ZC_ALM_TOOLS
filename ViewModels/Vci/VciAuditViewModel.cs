using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Models.Vci;
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

        private ObservableCollection<VciSelectableItem> _blocks;
        public ObservableCollection<VciSelectableItem> Blocks
        {
            get => _blocks;
            set { _blocks = value; OnPropertyChanged(); }
        }

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;

        // Comandos
        public AsyncRelayCommand LoadBlocksCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand DeselectAllCommand { get; }
        public AsyncRelayCommand UpdateDependenciesCommand { get; }

        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciAuditViewModel(
            TiaPlcService tiaPlcService, 
            ILogService logService, 
            IStatusService statusService)
        {
            _tiaPlcService = tiaPlcService;
            _logService = logService;
            _statusService = statusService;

            Blocks = new ObservableCollection<VciSelectableItem>();

            // Asignación de los comandos
            LoadBlocksCommand = new AsyncRelayCommand(ExecuteLoadBlocks);
            SelectAllCommand = new RelayCommand(() => SetAllSelection(true));
            DeselectAllCommand = new RelayCommand(() => SetAllSelection(false));
            UpdateDependenciesCommand = new AsyncRelayCommand(ExecuteUpdateDependencies, CanExecuteUpdateDependencies);
        }

        // ==================================================================================================================
        /// <summary>
        /// Cargar lista de bloques desde el servicio, filtrando solo aquellos que permiten actualización de dependencias.
        /// </summary>
        private async Task ExecuteLoadBlocks()
        {
            try
            {
                _statusService.Set("[VCI-AUDIT-VM] [ExecuteLoadBlocks] Cargando bloques para auditoría...", StatusType.Ok);
                await Task.Delay(50); // Pausa visual para que WPF pinte el mensaje

                var allBlocks = _tiaPlcService.GetAllBlocks();

                // Filtramos para mostrar SOLO los bloques cuyas dependencias se pueden actualizar.
                var selectableBlocks = allBlocks
                    .Where(b => b.CanUpdateDependencies)
                    .Select(b => new VciSelectableItem
                    {
                        OriginalItem = b,
                        IsSelected = false,
                        Name = b.Name,
                        SimpleType = b.SimpleType,
                        FolderPath = b.FolderPath,
                        Number = b.Number,
                        ProgrammingLanguage = b.ProgrammingLanguage,
                        CanUpdateDependencies = b.CanUpdateDependencies,
                        IsExportable = b.IsExportable
                    })
                    .OrderBy(i => i.SimpleType)
                    .ThenBy(i => i.Number);

                Blocks = new ObservableCollection<VciSelectableItem>(selectableBlocks);

                _statusService.Set($"[VCI-AUDIT] [ExecuteLoadBlocks] Se han cargado {Blocks.Count} bloques en auditoría.", StatusType.Ok);
            }
            catch (Exception ex)
            {
                _statusService.Set($"[VCI-AUDIT] [ExecuteLoadBlocks] Error cargando bloques: {ex.Message}", StatusType.Error);
            }
        }

        // ==================================================================================================================
        /// <summary>
        /// Aplica un estado de selección masiva a todos los elementos del listado.
        /// </summary>
        private void SetAllSelection(bool state)
        {
            foreach (var item in Blocks)
            {
                item.IsSelected = state;
            }
        }

        // ==================================================================================================================
        private bool CanExecuteUpdateDependencies()
        {
            // Verificamos si hay algún bloque seleccionado cuyo OriginalItem (CachedPlcBlock) permita actualización
            return Blocks != null && Blocks.Any(b =>
                b.IsSelected &&
                (b.OriginalItem as CachedPlcBlock)?.CanUpdateDependencies == true);
        }

        // ==================================================================================================================
        /// <summary>
        /// Actualiza las dependencias de los bloques seleccionados, ejecutando el proceso en segundo plano para evitar que la UI se congele.
        /// </summary>
        private async Task ExecuteUpdateDependencies()
        {
            // Filtramos los elementos seleccionados y extraemos el CachedPlcBlock original
            List<CachedPlcBlock> selectedBlocks = Blocks
                .Where(b => b.IsSelected && (b.OriginalItem as CachedPlcBlock)?.CanUpdateDependencies == true)
                .Select(b => b.OriginalItem as CachedPlcBlock)
                .ToList();

            if (!selectedBlocks.Any()) return;

            _statusService.Set($"[VCI-AUDIT] [ExecuteUpdateDependencies] Iniciando actualización de dependencias de {selectedBlocks.Count} bloques...", StatusType.Warning);

            await Task.Delay(50); // Pausa visual antes del trabajo pesado

            // Ejecutamos en segundo plano para que la UI no se quede "No responde"
            bool success = await _tiaPlcService.UpdateMassiveSclDependencies(selectedBlocks);

            if (success)
            {
                _statusService.Set("[VCI-AUDIT] [ExecuteUpdateDependencies] Dependencias actualizadas y bloques re-importados con éxito.", StatusType.Ok);

                _tiaPlcService.BuildBlockCache();

                // Recargamos los datos actualizados
                await ExecuteLoadBlocks();

                // Opcional: Recargar la vista o desmarcar checks tras el éxito
                foreach (var b in Blocks) b.IsSelected = false;
            }
            else
            {
                _statusService.Set("[VCI-AUDIT] [ExecuteUpdateDependencies] Error durante la actualización de dependencias. Revisa el log.", StatusType.Error);
            }
        }
    }
}