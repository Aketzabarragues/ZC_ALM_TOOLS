using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ZC_ALM_TOOLS.Core;
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
        private readonly TiaPlcService _tiaPlcService;
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

        public RelayCommand AnalyzeProjectCommand { get; }
        public RelayCommand ApplyMappingsCommand { get; }
        public RelayCommand UnmapBlocksCommand { get; }


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciMappingViewModel(TiaPlcService tiaPlcService, TiaVciService tiaVciService)
        {
            _tiaPlcService = tiaPlcService;
            _tiaVciService = tiaVciService;

            MappingActions = new ObservableCollection<VciMappingAction>();
            WorkspaceStatusText = "Esperando análisis...";

            AnalyzeProjectCommand = new RelayCommand(ExecuteAnalyzeProject, CanExecuteAnalyze);
            ApplyMappingsCommand = new RelayCommand(ExecuteApplyMappings, CanExecuteApply);
            UnmapBlocksCommand = new RelayCommand(ExecuteUnmapBlocks, CanExecuteUnmap);
        }

        private bool CanExecuteAnalyze() => !string.IsNullOrWhiteSpace(WorkspacePath);



        // ==================================================================================================================
        /// <summary>
        /// Metodo para comparar y analizar los bloques del PLC con los archivos XML del VCI
        /// </summary>
        private void ExecuteAnalyzeProject()
        {
            LogService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Iniciando escaneo cruzado en: {WorkspacePath}");
            StatusService.SetBusy(true);

            try
            {
                MappingActions.Clear();

                // 1. LECTURA DE DATOS
                StatusService.Set("Extrayendo bloques del PLC y archivos del disco...", StatusType.Ok);

                var vciWorkspaceService = new VciWorkspaceService();
                var localFilesDict = vciWorkspaceService.GetVciFilesFromWorkspace(WorkspacePath);

                // Usamos la caché del PLC (instantánea)
                var plcBlocks = _tiaPlcService.GetAllBlocks();

                // Obtenemos el listado de archivos ya mapeados en TIA Portal
                StatusService.Set("Cruzando datos...", StatusType.Ok);
                int matchLinkedCount = 0;
                int pendingMapCount = 0;
                int missingExportCount = 0;

                // OBTENEMOS LOS OBJETOS COM REALES YA MAPEADOS (en lugar de solo los nombres)
                var mappedObjects = _tiaVciService.GetMappedObjects(WorkspaceName);

                foreach (var plcBlock in plcBlocks)
                {
                    // ¿Existe el archivo XML en el disco duro?
                    if (localFilesDict.TryGetValue(plcBlock.Name, out string diskPath))
                    {
                        // Necesitamos el objeto exacto del PLC para evitar confundirlo con otro PLC
                        var realPlcBlock = _tiaPlcService.FindBlockByName(plcBlock.Name);

                        // Comprobamos si el objeto exacto de este PLC está en la lista de mapeados
                        bool isMapped = false;
                        if (realPlcBlock != null)
                        {
                            // La API de Siemens usa .Equals() para comparar las identidades de los objetos
                            isMapped = mappedObjects.Any(m => m.Equals(realPlcBlock));
                        }

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
                        // ... (el resto del código sigue exactamente igual hacia abajo)
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

                // 3. ORDENACIÓN (1º Falta Enlazar (Verde) -> 2º Enlazados -> 3º Solo PLC -> 4º Solo VCI)
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
                StatusService.Set($"Análisis: {matchLinkedCount} enlazados, {pendingMapCount} por enlazar, {missingExportCount} solo PLC, {localFilesDict.Count} solo disco.", StatusType.Ok);
            }
            catch (Exception ex)
            {
                LogService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] EXCEPCIÓN: {ex.Message}", true);
                StatusService.Set("Error al analizar el proyecto.", StatusType.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar que la selección del PLC ha cambiado globalmente
        /// </summary>
        public void NotifyPlcChanged(string plcName)
        {
            LogService.Write($"[VCI-MAPPING-VM] [NotifyPlcChanged] El PLC de origen ha cambiado a '{plcName}'. Limpiando mapeos...");
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
        private void ExecuteApplyMappings()
        {
            var itemsToMap = MappingActions.Where(m => m.IsSelected && m.State == VciMatchState.ListoParaEnlazar).ToList();

            if (!itemsToMap.Any())
            {
                StatusService.Set("No hay ningún bloque seleccionado para mapear.", StatusType.Warning);
                return;
            }

            LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Iniciando mapeo de {itemsToMap.Count} bloques...");
            StatusService.SetBusy(true);

            try
            {
                string wsName = WorkspaceName;

                LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Nombre de Workspace detectado para TIA Portal: '{wsName}'");

                int successCount = 0;
                string basePath = WorkspacePath.TrimEnd('\\') + "\\";

                foreach (var item in itemsToMap)
                {
                    StatusService.Set($"Vinculando bloque: {item.BlockName}...", StatusType.Warning);
                    LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] --- Procesando '{item.BlockName}' ---");
                    LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Ruta absoluta en disco: '{item.DiskPath}'");

                    var plcBlock = _tiaPlcService.FindBlockByName(item.BlockName);

                    if (plcBlock != null)
                    {
                        string relativePath = "";

                        if (item.DiskPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = item.DiskPath.Substring(basePath.Length);
                            relativePath = "\\" + relativePath.TrimStart('\\');
                        }
                        else
                        {
                            relativePath = "\\" + Path.GetFileName(item.DiskPath);
                        }

                        LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Bloque encontrado en PLC. Intentando mapear con Ruta Relativa: '{relativePath}'");

                        bool ok = _tiaVciService.MapObjectToWorkspace(wsName, plcBlock, relativePath);
                        if (ok)
                        {
                            successCount++;
                            LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] ÉXITO: '{item.BlockName}' mapeado correctamente.");
                        }
                        else
                        {
                            LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] FALLO: El servicio VCI devolvió False para '{item.BlockName}'.", true);
                        }
                    }
                    else
                    {
                        LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] FALLO: No se pudo encontrar el bloque '{item.BlockName}' en la caché del PLC.", true);
                    }
                }

                LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] Mapeo finalizado. {successCount} de {itemsToMap.Count} exitosos.");
                StatusService.Set($"Se han mapeado {successCount} bloques en TIA Portal correctamente.", StatusType.Ok);

                foreach (var item in itemsToMap) item.IsSelected = false;

                ExecuteAnalyzeProject();

            }
            catch (Exception ex)
            {
                LogService.Write($"[VCI-MAPPING-VM] [ExecuteApplyMappings] EXCEPCIÓN: {ex.Message}", true);
                StatusService.Set("Error al aplicar los mapeos. Revisa los logs.", StatusType.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }



        // ==================================================================================================================
        private bool CanExecuteUnmap() => MappingActions != null && MappingActions.Count > 0;
        // ==================================================================================================================
        // ==================================================================================================================
        /// <summary>
        /// Ejecuta la eliminación de los Mapeos VCI para los bloques seleccionados que ya estaban enlazados.
        /// </summary>
        private void ExecuteUnmapBlocks()
        {
            // Filtramos SOLO los que el usuario ha marcado y que están "Ya Enlazados" (Azules)
            var itemsToUnmap = MappingActions.Where(m => m.IsSelected && m.State == VciMatchState.YaEnlazado).ToList();

            if (!itemsToUnmap.Any())
            {
                StatusService.Set("No hay ningún bloque 'Enlazado' seleccionado para desvincular.", StatusType.Warning);
                return;
            }

            LogService.Write($"[VCI-MAPPING-VM] [ExecuteUnmapBlocks] Iniciando desvinculación de {itemsToUnmap.Count} bloques...");
            StatusService.SetBusy(true);

            try
            {
                string wsName = WorkspaceName;
                int successCount = 0;

                foreach (var item in itemsToUnmap)
                {
                    StatusService.Set($"Desvinculando bloque: {item.BlockName}...", StatusType.Warning);

                    var plcBlock = _tiaPlcService.FindBlockByName(item.BlockName);

                    if (plcBlock != null)
                    {
                        bool ok = _tiaVciService.UnmapObjectFromWorkspace(wsName, plcBlock);
                        if (ok)
                        {
                            successCount++;
                            LogService.Write($"[VCI-MAPPING-VM] [ExecuteUnmapBlocks] ÉXITO: '{item.BlockName}' desvinculado correctamente.");
                        }
                    }
                }

                LogService.Write($"[VCI-MAPPING-VM] [ExecuteUnmapBlocks] Desvinculación finalizada. {successCount} de {itemsToUnmap.Count} exitosos.");
                StatusService.Set($"Se han eliminado {successCount} mapeos en TIA Portal.", StatusType.Ok);

                foreach (var item in itemsToUnmap) item.IsSelected = false;

                // Refrescamos la vista para que los bloques vuelvan a ponerse de color verde
                ExecuteAnalyzeProject();
            }
            catch (Exception ex)
            {
                LogService.Write($"[VCI-MAPPING-VM] [ExecuteUnmapBlocks] EXCEPCIÓN: {ex.Message}", true);
                StatusService.Set("Error al desvincular mapeos. Revisa los logs.", StatusType.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }



    }
}