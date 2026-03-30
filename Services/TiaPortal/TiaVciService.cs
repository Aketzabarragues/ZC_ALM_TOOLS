using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using ZC_ALM_TOOLS.Models.Vci;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{



    // ==================================================================================================================
    /// <summary>
    /// Servicio para interactuar con la Interfaz de Control de Versiones (VCI) de TIA Portal mediante Openness.
    /// </summary>
    public class TiaVciService
    {
        private readonly Project _currentProject;
        private readonly Siemens.Engineering.TiaPortal _tiaApp;

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;


        // ==================================================================================================================
        /// <summary>
        /// Constructor: Recibe las instancias de TIA Portal y el proyecto actual, junto con los servicios de logging y status para reportar durante las operaciones.
        /// </summary>
        public TiaVciService(Siemens.Engineering.TiaPortal tiaApp, Project project,
                              ILogService logService, IStatusService statusService)
        {
            _tiaApp = tiaApp;
            _currentProject = project;

            _logService = logService;
            _statusService = statusService;
        }



        // ==================================================================================================================
        /// <summary>
        /// Obtiene la lista de Workspaces configurados actualmente en el proyecto de TIA Portal.
        /// </summary>
        public List<VciWorkspaceModel> GetConfiguredWorkspaces()
        {
            var workspaces = new List<VciWorkspaceModel>();

            if (_currentProject == null) return workspaces;

            try
            {
                // CORRECTO: Se obtiene como servicio del proyecto
                var vciService = _currentProject.GetService<VersionControlInterface>();
                if (vciService == null) return workspaces;

                foreach (Workspace ws in vciService.WorkspaceGroup.Workspaces)
                {
                    workspaces.Add(new VciWorkspaceModel
                    {
                        Name = ws.Name,
                        Path = ws.RootPath?.FullName ?? "Ruta no definida",
                        SoftwareWorkspace = ws
                    });
                }
                _logService.Write($"[TIA-VCI-SERVICE] [GetConfiguredWorkspaces] Se han encontrado {workspaces.Count} Workspaces en el proyecto.");
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-VCI-SERVICE] [GetConfiguredWorkspaces] Error al leer Workspaces: {ex.Message}", true);
            }

            return workspaces;
        }



        // ==================================================================================================================
        /// <summary>
        /// Crea un nuevo Workspace en TIA Portal asociado a una ruta de disco local.
        /// </summary>
        public VciWorkspaceModel CreateWorkspace(string name, string diskPath)
        {
            if (_currentProject == null) return null;

            try
            {
                _logService.Write($"[TIA-VCI-SERVICE] [CreateWorkspace] Creando Workspace '{name}' en '{diskPath}'...");

                DirectoryInfo dirInfo = new DirectoryInfo(diskPath);
                if (!dirInfo.Exists) dirInfo.Create(); // Aseguramos que la carpeta exista

                var vciService = _currentProject.GetService<VersionControlInterface>();
                if (vciService == null) throw new Exception("El servicio VCI no está disponible en este proyecto.");

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Creando Workspace VCI..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Crear Workspace {name}"))
                    {
                        // Se crea el workspace y se le asigna el directorio raíz en disco
                        Workspace newWs = vciService.WorkspaceGroup.Workspaces.Create(name);
                        newWs.RootPath = dirInfo;

                        transaction.CommitOnDispose();

                        return new VciWorkspaceModel
                        {
                            Name = newWs.Name,
                            Path = newWs.RootPath.FullName,
                            SoftwareWorkspace = newWs
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-VCI-SERVICE] [CreateWorkspace] Error creando Workspace: {ex.Message}", true);
                return null;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Borra un Workspace existente en TIA Portal por su nombre. 
        /// (Nota: Solo borra el enlace en TIA Portal, no borra los archivos del disco duro).
        /// </summary>
        public bool DeleteWorkspace(string workspaceName)
        {
            if (_currentProject == null) return false;

            try
            {
                var vciService = _currentProject.GetService<VersionControlInterface>();
                if (vciService == null) return false;

                Workspace ws = vciService.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws != null)
                {
                    using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Borrando Workspace VCI..."))
                    {
                        using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Borrar Workspace {workspaceName}"))
                        {
                            ws.Delete();

                            transaction.CommitOnDispose();

                            _logService.Write($"[TIA-VCI-SERVICE] [DeleteWorkspace] Workspace '{workspaceName}' eliminado de TIA Portal.");
                            return true;
                        }
                    }
                }

                _logService.Write($"[TIA-VCI-SERVICE] [DeleteWorkspace] No se encontró el Workspace '{workspaceName}'.");
                return false;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-VCI-SERVICE] [DeleteWorkspace] Error al borrar Workspace '{workspaceName}': {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Cambia la ruta física (RootPath) de un Workspace existente en TIA Portal.
        /// </summary>
        public bool UpdateWorkspacePath(string workspaceName, string newPath)
        {
            if (_currentProject == null) return false;
            try
            {
                var vciService = _currentProject.GetService<VersionControlInterface>();
                Workspace ws = vciService?.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws != null)
                {
                    using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Actualizando ruta Workspace..."))
                    {
                        using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Cambiar ruta Workspace {workspaceName}"))
                        {
                            ws.RootPath = new DirectoryInfo(newPath);
                            transaction.CommitOnDispose();
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-VCI-SERVICE] [UpdateWorkspacePath] Error al cambiar ruta del Workspace: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Crea o actualiza un vínculo (Mapping) VCI entre un objeto del TIA Portal y una ruta relativa en el Workspace.
        /// Adicionalmente, fuerza a TIA Portal a comprobar el estado de sincronización.
        /// </summary>
        public async Task<Dictionary<string, VciMapResult>> MapObjectsToWorkspaceAsync(string workspaceName, List<(string BlockName, IEngineeringObject PlcObject, string RelativePath)> itemsToMap)
        {
            var results = new Dictionary<string, VciMapResult>();
            if (_currentProject == null || _tiaApp == null) return results;

            try
            {
                var vciService = _currentProject.GetService<VersionControlInterface>();
                Workspace ws = vciService?.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws == null) return results;

                // CONFIGURACIÓN DEL TAMAÑO DEL LOTE
                // 10 es un número muy seguro para evitar que TIA Portal colapse la RAM con los bloques rotos
                int batchSize = 10;
                int totalProcessed = 0;

                for (int i = 0; i < itemsToMap.Count; i += batchSize)
                {
                    // Extraemos el lote actual (ej: del 0 al 9, del 10 al 19...)
                    var currentBatch = itemsToMap.Skip(i).Take(batchSize).ToList();
                    int batchNumber = (i / batchSize) + 1;
                    int totalBatches = (int)Math.Ceiling((double)itemsToMap.Count / batchSize);

                    await Task.Delay(25); // Cedemos hilo a WPF antes del acceso exclusivo

                    // Abrimos el acceso exclusivo SOLO para este lote pequeño
                    using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess($"Mapeando VCI (Lote {batchNumber}/{totalBatches})..."))
                    {
                        // Precargamos los mapeos en RAM para la velocidad
                        var existingMappings = new Dictionary<IEngineeringObject, WorkspaceMapping>();
                        foreach (WorkspaceMapping map in ws.Mappings)
                        {
                            if (map.LinkedProjectObject != null && !existingMappings.ContainsKey(map.LinkedProjectObject))
                            {
                                existingMappings.Add(map.LinkedProjectObject, map);
                            }
                        }

                        foreach (var item in currentBatch)
                        {
                            totalProcessed++;
                            _statusService.Set($"[TIA-VCI-SERVICE] [MapObjectsToWorkspace] Vinculando [{totalProcessed}/{itemsToMap.Count}]: {item.BlockName}...", StatusType.Warning);

                            await Task.Delay(10); // Respiro visual por cada bloque

                            try
                            {
                                if (existingMappings.TryGetValue(item.PlcObject, out WorkspaceMapping activeMapping))
                                {
                                    if (activeMapping.RelativeWorkspacePath != item.RelativePath)
                                        activeMapping.RelativeWorkspacePath = item.RelativePath;
                                }
                                else
                                {
                                    activeMapping = ws.Mappings.Create(item.RelativePath, item.PlcObject);
                                }

                                if (activeMapping != null)
                                {
                                    var syncStatusService = activeMapping.GetService<IndividualObjectSynchronizationStatus>();
                                    if (syncStatusService != null)
                                    {
                                        syncStatusService.UpdateStatus();
                                        results.Add(item.BlockName, VciMapResult.Success);
                                    }
                                    else
                                    {
                                        results.Add(item.BlockName, VciMapResult.SuccessWithWarning);
                                    }
                                }
                            }
                            catch (EngineeringException engEx)
                            {
                                _logService.Write($"[TIA-VCI-SERVICE] [MapObjectsToWorkspace] El bloque '{item.BlockName}' falló (¿inconsistente?). Detalle: {engEx.Message}");
                                _statusService.Set($"[TIA-VCI-SERVICE] [MapObjectsToWorkspace] El bloque '{item.BlockName}' falló (¿inconsistente?).", StatusType.Error);
                                results.Add(item.BlockName, VciMapResult.Error);
                            }
                            catch (Exception ex)
                            {
                                _logService.Write($"[TIA-VCI-SERVICE] [MapObjectsToWorkspace] Error en '{item.BlockName}': {ex.Message}", true);
                                _statusService.Set($"[TIA-VCI-SERVICE] [MapObjectsToWorkspace] Error en '{item.BlockName}'.", StatusType.Error);
                                results.Add(item.BlockName, VciMapResult.Error);
                            }
                        }
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-VCI-SERVICE] [MapObjectsToWorkspace] Error masivo: {ex.Message}", true);
                _statusService.Set($"[TIA-VCI-SERVICE] [MapObjectsToWorkspace] Error general.", StatusType.Error);
            }

            return results;
        }



        // ==================================================================================================================
        /// <summary>
        /// Elimina el vínculo (Mapping) VCI de un objeto en TIA Portal. No borra el archivo físico.
        /// </summary>
        public async Task<int> UnmapObjectsFromWorkspaceAsync(string workspaceName, List<(string BlockName, IEngineeringObject PlcObject)> itemsToUnmap)
        {
            int successCount = 0;
            if (_currentProject == null || _tiaApp == null) return 0;

            try
            {
                var vciService = _currentProject.GetService<VersionControlInterface>();
                Workspace ws = vciService?.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws == null) return 0;

                await Task.Delay(25); // Cedemos hilo a WPF antes de bloquear

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Desvinculando bloques en VCI..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Desvincular {itemsToUnmap.Count} bloques VCI"))
                    {
                        var existingMappings = new Dictionary<IEngineeringObject, WorkspaceMapping>();
                        foreach (WorkspaceMapping map in ws.Mappings)
                        {
                            if (map.LinkedProjectObject != null && !existingMappings.ContainsKey(map.LinkedProjectObject))
                            {
                                existingMappings.Add(map.LinkedProjectObject, map);
                            }
                        }

                        int current = 0;
                        foreach (var item in itemsToUnmap)
                        {
                            current++;
                            _statusService.Set($"[TIA-VCI-SERVICE] [UnmapObjectsFromWorkspace] Desvinculando [{current}/{itemsToUnmap.Count}]: {item.BlockName}...", StatusType.Warning);

                            await Task.Delay(10);

                            if (existingMappings.TryGetValue(item.PlcObject, out WorkspaceMapping mapping))
                            {
                                mapping.Delete();
                                successCount++;
                            }
                        }

                        await Task.Delay(25);
                        transaction.CommitOnDispose();
                    }
                }
            }
            catch (Exception ex) 
            { 
                _logService.Write($"[TIA-VCI-SERVICE] [UnmapObjectsFromWorkspace] Error Unmap masivo: {ex.Message}", true); 
            }

            return successCount;
        }



        // ==================================================================================================================
        /// <summary>
        /// Devuelve una lista con los objetos reales de ingeniería que ya están mapeados en el Workspace.
        /// </summary>
        public List<IEngineeringObject> GetMappedObjects(string workspaceName)
        {
            // ... (Tu código actual se mantiene igual) ...
            var mappedObjects = new List<IEngineeringObject>();
            if (_currentProject == null) return mappedObjects;
            try
            {
                var vciService = _currentProject.GetService<VersionControlInterface>();
                Workspace ws = vciService?.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws != null)
                {
                    foreach (WorkspaceMapping mapping in ws.Mappings)
                    {
                        if (mapping.LinkedProjectObject != null) mappedObjects.Add(mapping.LinkedProjectObject);
                    }
                }
            }
            catch { }
            return mappedObjects;
        }
    }

    
}