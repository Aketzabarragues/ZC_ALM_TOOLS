using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.VersionControl; // <-- NAMESPACE CORRECTO
using ZC_ALM_TOOLS.Models;
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
        private readonly Project _project;


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public TiaVciService(Project project)
        {
            _project = project;
        }



        // ==================================================================================================================
        /// <summary>
        /// Obtiene la lista de Workspaces configurados actualmente en el proyecto de TIA Portal.
        /// </summary>
        public List<VciWorkspaceModel> GetConfiguredWorkspaces()
        {
            var workspaces = new List<VciWorkspaceModel>();

            if (_project == null) return workspaces;

            try
            {
                // CORRECTO: Se obtiene como servicio del proyecto
                var vciService = _project.GetService<VersionControlInterface>();
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
                LogService.Write($"[TIA-VCI-SERVICE] [GetConfiguredWorkspaces] Se han encontrado {workspaces.Count} Workspaces en el proyecto.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-VCI-SERVICE] [GetConfiguredWorkspaces] Error al leer Workspaces: {ex.Message}", true);
            }

            return workspaces;
        }



        // ==================================================================================================================
        /// <summary>
        /// Crea un nuevo Workspace en TIA Portal asociado a una ruta de disco local.
        /// </summary>
        public VciWorkspaceModel CreateWorkspace(string name, string diskPath)
        {
            if (_project == null) return null;

            try
            {
                LogService.Write($"[TIA-VCI-SERVICE] [CreateWorkspace] Creando Workspace '{name}' en '{diskPath}'...");

                DirectoryInfo dirInfo = new DirectoryInfo(diskPath);
                if (!dirInfo.Exists) dirInfo.Create(); // Aseguramos que la carpeta exista

                var vciService = _project.GetService<VersionControlInterface>();
                if (vciService == null) throw new Exception("El servicio VCI no está disponible en este proyecto.");

                // Se crea el workspace y se le asigna el directorio raíz en disco
                Workspace newWs = vciService.WorkspaceGroup.Workspaces.Create(name);
                newWs.RootPath = dirInfo;

                return new VciWorkspaceModel
                {
                    Name = newWs.Name,
                    Path = newWs.RootPath.FullName,
                    SoftwareWorkspace = newWs
                };
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-VCI-SERVICE] [CreateWorkspace] Error creando Workspace: {ex.Message}", true);
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
            if (_project == null) return false;

            try
            {
                var vciService = _project.GetService<VersionControlInterface>();
                if (vciService == null) return false;

                Workspace ws = vciService.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws != null)
                {
                    ws.Delete();
                    LogService.Write($"[TIA-VCI-SERVICE] [DeleteWorkspace] Workspace '{workspaceName}' eliminado de TIA Portal.");
                    return true;
                }

                LogService.Write($"[TIA-VCI-SERVICE] [DeleteWorkspace] No se encontró el Workspace '{workspaceName}'.");
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-VCI-SERVICE] [DeleteWorkspace] Error al borrar Workspace '{workspaceName}': {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Cambia la ruta física (RootPath) de un Workspace existente en TIA Portal.
        /// </summary>
        public bool UpdateWorkspacePath(string workspaceName, string newPath)
        {
            if (_project == null) return false;
            try
            {
                var vciService = _project.GetService<VersionControlInterface>();
                Workspace ws = vciService?.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws != null)
                {
                    ws.RootPath = new DirectoryInfo(newPath);
                    LogService.Write($"[TIA-VCI-SERVICE] Ruta del Workspace '{workspaceName}' cambiada a '{newPath}'.");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-VCI-SERVICE] Error al cambiar ruta del Workspace: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Crea o actualiza un vínculo (Mapping) VCI entre un objeto del TIA Portal y una ruta relativa en el Workspace.
        /// Adicionalmente, fuerza a TIA Portal a comprobar el estado de sincronización.
        /// </summary>
        public VciMapResult MapObjectToWorkspace(string workspaceName, IEngineeringObject plcObject, string relativePath)
        {
            if (_project == null)
            {
                LogService.Write("[TIA-VCI-SERVICE] [MapObjectToWorkspace] ERROR: La referencia al proyecto (_project) es nula.", true);
                return VciMapResult.Error;
            }

            try
            {
                var vciService = _project.GetService<VersionControlInterface>();
                if (vciService == null)
                {
                    LogService.Write("[TIA-VCI-SERVICE] [MapObjectToWorkspace] ERROR: No se pudo obtener el VersionControlInterface.", true);
                    return VciMapResult.Error;
                }

                Workspace ws = vciService.WorkspaceGroup.Workspaces.Find(workspaceName);
                if (ws == null)
                {
                    LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] ERROR: No se encontró ningún Workspace llamado '{workspaceName}' en el proyecto.", true);
                    return VciMapResult.Error;
                }

                WorkspaceMapping activeMapping = null;

                // Comprobamos si el objeto ya está mapeado iterando sobre los mapeos existentes
                foreach (WorkspaceMapping mapping in ws.Mappings)
                {
                    if (mapping.LinkedProjectObject == plcObject)
                    {
                        if (mapping.RelativeWorkspacePath != relativePath)
                        {
                            mapping.RelativeWorkspacePath = relativePath;
                            LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] Mapeo existente actualizado a '{relativePath}'.");
                        }
                        else
                        {
                            LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] El objeto ya estaba mapeado correctamente en '{relativePath}'.");
                        }

                        activeMapping = mapping;
                        break;
                    }
                }

                // Si no estaba mapeado, lo creamos
                if (activeMapping == null)
                {
                    LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] El objeto no tenía mapeo. Llamando a Mappings.Create('{relativePath}')...");
                    activeMapping = ws.Mappings.Create(relativePath, plcObject);
                    LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] Nuevo mapeo creado con éxito.");
                }

                // 3. ACTUALIZAR ESTADO DE SINCRONIZACIÓN (Magia de Openness)
                if (activeMapping != null)
                {
                    var syncStatusService = activeMapping.GetService<IndividualObjectSynchronizationStatus>();
                    if (syncStatusService != null)
                    {
                        try
                        {
                            LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] Forzando a TIA Portal a comparar el archivo con el PLC (UpdateStatus)...");
                            syncStatusService.UpdateStatus();
                            return VciMapResult.Success;
                        }
                        catch (Exception syncEx)
                        {
                            LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] Advertencia: El mapeo se creó, pero TIA Portal no pudo comprobar el estado (UpdateStatus). Motivo: {syncEx.Message}");
                            return VciMapResult.SuccessWithWarning;
                        }
                    }
                    else
                    {
                        LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] Advertencia: El objeto mapeado no soporta comprobación de estado de sincronización.");
                        return VciMapResult.SuccessWithWarning;
                    }
                }

                return VciMapResult.Error;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-VCI-SERVICE] [MapObjectToWorkspace] EXCEPCIÓN al mapear hacia '{relativePath}': {ex.Message}", true);
                return VciMapResult.Error;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Devuelve una lista con los objetos reales de ingeniería que ya están mapeados en el Workspace.
        /// </summary>
        public List<IEngineeringObject> GetMappedObjects(string workspaceName)
        {
            var mappedObjects = new List<IEngineeringObject>();
            if (_project == null) return mappedObjects;

            try
            {
                var vciService = _project.GetService<VersionControlInterface>();
                Workspace ws = vciService?.WorkspaceGroup.Workspaces.Find(workspaceName);

                if (ws != null)
                {
                    foreach (WorkspaceMapping mapping in ws.Mappings)
                    {
                        if (mapping.LinkedProjectObject != null)
                        {
                            mappedObjects.Add(mapping.LinkedProjectObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-VCI-SERVICE] Error al leer objetos mapeados: {ex.Message}", true);
            }

            return mappedObjects;
        }



        // ==================================================================================================================
        /// <summary>
        /// Elimina el vínculo (Mapping) VCI de un objeto en TIA Portal. No borra el archivo físico.
        /// </summary>
        public bool UnmapObjectFromWorkspace(string workspaceName, IEngineeringObject plcObject)
        {
            if (_project == null) return false;

            try
            {
                var vciService = _project.GetService<VersionControlInterface>();
                Workspace ws = vciService?.WorkspaceGroup.Workspaces.Find(workspaceName);

                if (ws != null)
                {
                    foreach (WorkspaceMapping mapping in ws.Mappings)
                    {
                        // Si encontramos el mapeo que apunta a nuestro bloque exacto, lo borramos
                        if (mapping.LinkedProjectObject != null && mapping.LinkedProjectObject.Equals(plcObject))
                        {
                            mapping.Delete();
                            LogService.Write($"[TIA-VCI-SERVICE] [UnmapObjectFromWorkspace] Mapeo eliminado para el objeto en TIA Portal.");
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-VCI-SERVICE] [UnmapObjectFromWorkspace] Error al eliminar mapeo: {ex.Message}", true);
                return false;
            }
        }

    }
}