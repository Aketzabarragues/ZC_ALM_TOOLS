using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Siemens.Engineering;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Models.Vci;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;
using ZC_ALM_TOOLS.Services.Vci;

namespace ZC_ALM_TOOLS.ViewModels.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel que gestiona la pestaña mapeado de archivos VCI, cruzando los datos del disco con TIA Portal
    /// </summary>
    public class VciMappingViewModel : ObservableObject
    {
        // Nuevos servicios inyectados
        private readonly TiaPlcCacheService _cacheService;
        private readonly TiaVciService _tiaVciService;

        // Esta propiedad se actualiza automáticamente desde el VciMainViewModel
        private string _workspacePath;
        public string WorkspacePath
        {
            get => _workspacePath;
            set
            {
                if (_workspacePath != value)
                {
                    _workspacePath = value;
                    OnPropertyChanged();

                    ClearData("Workspace modificado. Pendiente de análisis...");
                }
            }
        }

        private string _workspaceStatusText;
        public string WorkspaceStatusText
        {
            get => _workspaceStatusText;
            set { _workspaceStatusText = value; OnPropertyChanged(); }
        }

        private string _workspaceName;
        public string WorkspaceName
        {
            get => _workspaceName;
            set { _workspaceName = value; OnPropertyChanged(); }
        }

        public ObservableCollection<VciMappingAction> MappingActions { get; set; }

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;

        // Comandos
        public AsyncRelayCommand AnalyzeProjectCommand { get; }
        public AsyncRelayCommand ApplyMappingsCommand { get; }
        public AsyncRelayCommand UnmapBlocksCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand DeselectAllCommand { get; }



        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciMappingViewModel(
            TiaPlcCacheService cacheService,
            TiaVciService tiaVciService,
            ILogService logService,
            IStatusService statusService)
        {
            _cacheService = cacheService;
            _tiaVciService = tiaVciService;
            _logService = logService;
            _statusService = statusService;

            MappingActions = new ObservableCollection<VciMappingAction>();
            WorkspaceStatusText = "Esperando análisis...";

            // Enlazamos directamente a las tareas asíncronas
            AnalyzeProjectCommand = new AsyncRelayCommand(ExecuteAnalyzeProject, CanExecuteAnalyze);
            ApplyMappingsCommand = new AsyncRelayCommand(ExecuteApplyMappings, CanExecuteApply);
            UnmapBlocksCommand = new AsyncRelayCommand(ExecuteUnmapBlocks, CanExecuteUnmap);

            // Operaciones de UI inmediatas (se quedan como síncronas)
            SelectAllCommand = new RelayCommand(() => SetAllSelection(true));
            DeselectAllCommand = new RelayCommand(() => SetAllSelection(false));
        }



        // ==================================================================================================================
        /// <summary>
        /// Aplica un estado de selección masiva a todos los elementos del listado.
        /// </summary>
        private void SetAllSelection(bool state)
        {
            foreach (var item in MappingActions)
            {
                if (item.IsSelectable)
                {
                    item.IsSelected = state;
                }
            }
        }



        // ==================================================================================================================
        private bool CanExecuteAnalyze() => !string.IsNullOrWhiteSpace(WorkspacePath);
        // ==================================================================================================================
        /// <summary>
        /// Metodo para comparar y analizar los bloques del PLC con los archivos XML del VCI
        /// </summary>
        private async Task ExecuteAnalyzeProject()
        {
            _logService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Iniciando escaneo cruzado en: {WorkspacePath}");

            try
            {
                MappingActions.Clear();

                // Leemos los datos desde el disco
                _statusService.Set("[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Extrayendo archivos del disco...", StatusType.Ok);
                await Task.Delay(50);

                var vciWorkspaceService = new VciWorkspaceService();
                var localFilesDict = vciWorkspaceService.GetVciFilesFromWorkspace(WorkspacePath);
                _logService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Archivos VCI leídos del disco duro: {localFilesDict.Count}");

                // Leemos la cache del PLC
                _statusService.Set("[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Extrayendo bloques del PLC...", StatusType.Ok);
                await Task.Delay(50);

                // Consulta a la caché en lugar del servicio antiguo
                var plcBlocks = _cacheService.GetAllBlocks();
                _logService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Bloques recuperados de la caché del PLC: {plcBlocks.Count()}");

                // Consultamos a Tia portal el estado
                _statusService.Set("[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Consultando estado de mapeos en TIA Portal...", StatusType.Ok);
                await Task.Delay(50);

                _logService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Solicitando objetos mapeados al TIA VCI Service para el Workspace '{WorkspaceName}'...");
                var mappedObjects = _tiaVciService.GetMappedObjects(WorkspaceName);
                _logService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Objetos actualmente mapeados en TIA Portal: {mappedObjects.Count}");

                var mappedBlockNames = new HashSet<string>();
                foreach (dynamic obj in mappedObjects)
                {
                    try { mappedBlockNames.Add((string)obj.Name); } catch { }
                }

                // Cruzamos los datos
                _statusService.Set("[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Cruzando y comparando datos...", StatusType.Ok);
                await Task.Delay(50);

                int matchLinkedCount = 0;
                int pendingMapCount = 0;
                int missingExportCount = 0;

                foreach (var plcBlock in plcBlocks)
                {
                    // ¿Existe el archivo XML en el disco duro?
                    if (localFilesDict.TryGetValue(plcBlock.Name, out string diskPath))
                    {
                        // BÚSQUEDA INSTANTÁNEA EN RAM (Sin llamar a TIA Portal)
                        bool isMapped = mappedBlockNames.Contains(plcBlock.Name);

                        if (isMapped)
                        {
                            MappingActions.Add(new VciMappingAction
                            {
                                BlockName = plcBlock.Name,
                                BlockType = plcBlock.SimpleType,
                                DiskPath = diskPath,
                                State = VciMatchState.YaEnlazado
                            });
                            matchLinkedCount++;
                        }
                        else
                        {
                            MappingActions.Add(new VciMappingAction
                            {
                                BlockName = plcBlock.Name,
                                BlockType = plcBlock.SimpleType,
                                DiskPath = diskPath,
                                State = VciMatchState.ListoParaEnlazar
                            });
                            pendingMapCount++;
                        }

                        localFilesDict.Remove(plcBlock.Name);
                    }
                    else
                    {
                        MappingActions.Add(new VciMappingAction
                        {
                            BlockName = plcBlock.Name,
                            BlockType = plcBlock.SimpleType,
                            DiskPath = "--- No existe en disco ---",
                            State = VciMatchState.FaltaExportar
                        });
                        missingExportCount++;
                    }
                }

                // Los que sobran en la lista de disco son los que no existen en el PLC
                foreach (var local in localFilesDict)
                {
                    MappingActions.Add(new VciMappingAction
                    {
                        BlockName = local.Key,
                        BlockType = "XML",
                        DiskPath = local.Value,
                        State = VciMatchState.Conflicto
                    });
                }

                _logService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Bucle finalizado. Resultados: {matchLinkedCount} Enlazados, {pendingMapCount} Por Enlazar, {missingExportCount} Solo PLC, {localFilesDict.Count} Solo Disco.");

                // Ordenamos y volcamos al datagrid
                _logService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Ordenando y actualizando la interfaz gráfica...");

                var sortedList = MappingActions.OrderBy(x =>
                {
                    switch (x.State)
                    {
                        case VciMatchState.ListoParaEnlazar: return 1;
                        case VciMatchState.YaEnlazado: return 2;
                        case VciMatchState.FaltaExportar: return 3;
                        case VciMatchState.Conflicto: return 4;
                        default: return 5;
                    }
                }).ToList();

                MappingActions.Clear();
                foreach (var item in sortedList) MappingActions.Add(item);

                WorkspaceStatusText = $"Análisis completado en: {WorkspacePath}";
                _statusService.Set($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Análisis: {matchLinkedCount} enlazados, {pendingMapCount} por enlazar, {missingExportCount} solo PLC, {localFilesDict.Count} solo disco.", StatusType.Ok);
            }
            catch (Exception ex)
            {
                _statusService.Set($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Error al analizar el proyecto: {ex.Message}", StatusType.Error);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar que la selección del PLC ha cambiado globalmente
        /// </summary>
        public void NotifyPlcChanged(string plcName)
        {
            _logService.Write($"[VCI-MAPPING-VM] [NotifyPlcChanged] El PLC de origen ha cambiado a '{plcName}'. Limpiando mapeos...");
            ClearData($"PLC cambiado a '{plcName}'. Pendiente de análisis...");
        }



        // ==================================================================================================================
        /// <summary>
        /// Limpia el DataGrid y actualiza el texto de estado
        /// </summary>
        private void ClearData(string reasonMessage)
        {
            if (MappingActions != null && MappingActions.Count > 0)
            {
                MappingActions.Clear();
            }
            WorkspaceStatusText = reasonMessage;
        }



        // ==================================================================================================================
        private bool CanExecuteApply() => MappingActions != null && MappingActions.Count > 0;
        // ==================================================================================================================
        /// <summary>
        /// Ejecuta la creación de los Mapeos VCI para los bloques que existen en ambos lados.
        /// </summary>
        private async Task ExecuteApplyMappings()
        {
            var itemsToMap = MappingActions.Where(m => m.IsSelected &&
                            (m.State == VciMatchState.ListoParaEnlazar || m.State == VciMatchState.ErrorAlEnlazar)).ToList();

            if (!itemsToMap.Any())
            {
                _statusService.Set("[VCI-MAPPING-VM] [ExecuteApplyMappings] No hay ningún bloque seleccionado para mapear.", StatusType.Warning);
                return;
            }

            _logService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Iniciando mapeo de {itemsToMap.Count} bloques...");
            await Task.Delay(50);

            try
            {
                string wsName = WorkspaceName;
                string basePath = WorkspacePath.TrimEnd('\\') + "\\";

                // 1. Preparamos la lista (El Lote)
                var batchList = new List<(string BlockName, IEngineeringObject PlcObject, string RelativePath)>();

                foreach (var item in itemsToMap)
                {
                    // Consulta a la caché para buscar el bloque COM original
                    var plcBlock = _cacheService.FindBlockByName(item.BlockName);

                    if (plcBlock != null)
                    {
                        string relativePath = item.DiskPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                            ? "\\" + item.DiskPath.Substring(basePath.Length).TrimStart('\\')
                            : "\\" + Path.GetFileName(item.DiskPath);

                        batchList.Add((item.BlockName, plcBlock, relativePath));
                    }
                    else
                    {
                        item.State = VciMatchState.ErrorAlEnlazar;
                        item.IsSelected = false;
                    }
                }

                // 2. Enviamos el lote completo al servicio
                var results = await _tiaVciService.MapObjectsToWorkspaceAsync(wsName, batchList);

                // 3. Procesamos los resultados visuales
                int successCount = 0;
                foreach (var item in itemsToMap)
                {
                    if (results.TryGetValue(item.BlockName, out VciMapResult res))
                    {
                        if (res == VciMapResult.Success)
                        {
                            item.State = VciMatchState.YaEnlazado;
                            successCount++;
                        }
                        else if (res == VciMapResult.SuccessWithWarning)
                        {
                            item.State = VciMatchState.ErrorAlEnlazar;
                            successCount++;
                        }
                        else item.State = VciMatchState.ErrorAlEnlazar;
                    }
                    item.IsSelected = false;
                }

                if (successCount == itemsToMap.Count)
                {
                    _statusService.Set($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Se han mapeado todos los bloques ({successCount}) correctamente.", StatusType.Ok);
                }
                else
                {
                    _statusService.Set($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Mapeo finalizado con errores. {successCount} de {itemsToMap.Count} exitosos.", StatusType.Error);
                }
            }
            catch (Exception ex)
            {
                _statusService.Set($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Error al aplicar los mapeos: {ex.Message}", StatusType.Error);
            }
        }



        // ==================================================================================================================
        private bool CanExecuteUnmap() => MappingActions != null && MappingActions.Count > 0;
        // ==================================================================================================================
        /// <summary>
        /// Ejecuta la eliminación de los Mapeos VCI para los bloques seleccionados que ya estaban enlazados.
        /// </summary>
        private async Task ExecuteUnmapBlocks()
        {
            var itemsToUnmap = MappingActions.Where(m => m.IsSelected && m.State == VciMatchState.YaEnlazado).ToList();

            if (!itemsToUnmap.Any())
            {
                _statusService.Set("[VCI-MAPPING-VM] [ExecuteUnmapBlocks] No hay ningún bloque 'Enlazado' seleccionado para desvincular.", StatusType.Warning);
                return;
            }

            _statusService.Set("[VCI-MAPPING-VM] [ExecuteUnmapBlocks] Iniciando desvinculación masiva...", StatusType.Warning);
            await Task.Delay(50);

            try
            {
                string wsName = WorkspaceName;

                // 1. Preparamos el lote
                var batchList = new List<(string BlockName, IEngineeringObject PlcObject)>();
                foreach (var item in itemsToUnmap)
                {
                    // Consulta a la caché para encontrar el objeto original
                    var plcBlock = _cacheService.FindBlockByName(item.BlockName);
                    if (plcBlock != null) batchList.Add((item.BlockName, plcBlock));
                }

                // 2. Enviamos al servicio
                int successCount = await _tiaVciService.UnmapObjectsFromWorkspaceAsync(wsName, batchList);

                _statusService.Set($"[VCI-MAPPING-VM] [ExecuteUnmapBlocks] Se han eliminado {successCount} mapeos en TIA Portal.", StatusType.Ok);
                foreach (var item in itemsToUnmap) item.IsSelected = false;

                // Refrescamos la vista
                await ExecuteAnalyzeProject();
            }
            catch (Exception ex)
            {
                _statusService.Set($"[VCI-MAPPING-VM] [ExecuteUnmapBlocks] Error al desvincular mapeos: {ex.Message}", StatusType.Error);
            }
        }

    }
}