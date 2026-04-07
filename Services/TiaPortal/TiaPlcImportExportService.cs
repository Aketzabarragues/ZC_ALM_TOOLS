using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{

    /// <summary>
    /// Servicio dedicado a la importación y exportación de bloques de código y tablas de variables en TIA Portal, 
    /// proporcionando métodos para manejar estas operaciones de forma segura, eficiente y con capacidad de rollback en caso de errores durante procesos masivos.
    /// </summary>
    public class TiaPlcImportExportService
    {

        private readonly Siemens.Engineering.TiaPortal _tiaApp;
        private readonly Project _currentProject;
        private readonly TiaPlcCacheService _cacheService;

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;


        public TiaPlcImportExportService(
            Siemens.Engineering.TiaPortal tiaApp,
            Project project,
            TiaPlcCacheService cacheService,
            ILogService logService,
            IStatusService statusService)
        {

            _tiaApp = tiaApp;
            _currentProject = project;
            _cacheService = cacheService;
            _logService = logService;
            _statusService = statusService;

        }



        // ==================================================================================================================
        /// <summary>
        /// Helper privado para navegar o crear la estructura de carpetas de Tablas de Variables en el PLC.
        /// </summary>
        private PlcTagTableUserGroup GetOrCreateTagTableGroup(PlcTagTableUserGroupComposition rootGroups, string groupPath)
        {
            if (string.IsNullOrEmpty(groupPath)) return null;

            string[] folders = groupPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            PlcTagTableUserGroup currentGroup = null;
            PlcTagTableUserGroupComposition currentGroupCollection = rootGroups;

            foreach (string folder in folders)
            {
                currentGroup = currentGroupCollection.Find(folder);
                if (currentGroup == null)
                {
                    currentGroup = currentGroupCollection.Create(folder); // Si no existe, crea la carpeta en TIA Portal
                }
                currentGroupCollection = currentGroup.Groups; // Bajamos un nivel para la siguiente iteración
            }

            return currentGroup;
        }



        // ==================================================================================================================
        /// <summary>
        /// Helper privado para navegar o crear la estructura de carpetas (Grupos) en el PLC.
        /// </summary>
        private PlcBlockUserGroup GetOrCreateBlockGroup(PlcBlockUserGroupComposition rootGroups, string groupPath)
        {
            if (string.IsNullOrEmpty(groupPath)) return null;

            string[] folders = groupPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            PlcBlockUserGroup currentGroup = null;
            PlcBlockUserGroupComposition currentGroupCollection = rootGroups;

            foreach (string folder in folders)
            {
                currentGroup = currentGroupCollection.Find(folder);
                if (currentGroup == null)
                {
                    currentGroup = currentGroupCollection.Create(folder); // Si no existe, crea la carpeta en TIA Portal
                }
                currentGroupCollection = currentGroup.Groups; // Bajamos un nivel para la siguiente iteración
            }

            return currentGroup;
        }



        // ==================================================================================================================
        /// <summary>
        /// Crea una nueva tabla de variables en TIA Portal (Fase 2.5)
        /// </summary>
        public async Task<bool> CreateTagTableAsync(string tableName)
        {
            try
            {
                if (_cacheService.CurrentPlc == null) return false;
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Creando Tabla de Variables..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Crear Tabla {tableName}"))
                    {
                        if (_cacheService.FindTagTableByName(tableName) == null)
                        {
                            _cacheService.CurrentPlc.TagTableGroup.TagTables.Create(tableName);
                        }
                        transaction.CommitOnDispose();
                    }
                }
                _cacheService.BuildBlockCache();
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [CreateTagTableAsync] Error creando tabla de variables '{tableName}': {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Exportar tabla de variables de dispositivos a XML de forma asíncrona
        /// </summary>
        public async Task<bool> ExportDispTagTableAsync(string tableName, string xmlPath)
        {
            try
            {
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
                var table = _cacheService.FindTagTableByName(tableName);
                if (table == null) return false;

                await Task.Delay(25); // Cede el hilo a WPF

                table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                return true;
            }
            catch { return false; }
        }



        // ==================================================================================================================
        /// <summary>
        /// Exportar un bloque a XML de forma asíncrona
        /// </summary>
        public async Task<bool> ExportBlockToXmlAsync(string blockName, string destinationPath)
        {
            try
            {
                var block = _cacheService.FindBlockByName(blockName);
                if (block == null)
                {
                    _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [ExportBlockToXmlAsync] No se encontró el bloque '{blockName}'.", true);
                    return false;
                }

                if (File.Exists(destinationPath)) File.Delete(destinationPath);

                await Task.Delay(25); // Cede el hilo a WPF para pintar la UI antes de bloquear

                block.Export(new FileInfo(destinationPath), ExportOptions.WithDefaults);
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [ExportBlockToXmlAsync] Bloque '{blockName}' exportado a {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [ExportBlockToXmlAsync] Error exportando bloque '{blockName}': {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Exporta un bloque o UDT como archivo fuente de texto plano (.scl, .awl, .db, .udt)
        /// </summary>
        public async Task<bool> ExportAsSourceAsync(object item, string destinationPath)
        {
            
            try
            {
                if (_cacheService.CurrentPlc == null) return false;

                // Borramos el archivo si ya existía de una compilación anterior
                if (File.Exists(destinationPath)) File.Delete(destinationPath);

                // Si es un bloque de código (FC, FB, OB, DB)
                if (item is PlcBlock block)
                {
                    _cacheService.CurrentPlc.ExternalSourceGroup.GenerateSource(new List<PlcBlock> { block }, new FileInfo(destinationPath), GenerateOptions.None);
                    return true;
                }
                // Si es un Tipo de Datos de Usuario (UDT)
                else if (item is PlcType udt)
                {
                    _cacheService.CurrentPlc.ExternalSourceGroup.GenerateSource(new List<PlcType> { udt }, new FileInfo(destinationPath), GenerateOptions.None);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [ExportAsSourceAsync] Error exportando fuente a {destinationPath}: {ex.Message}", true);
                return false;
            }
            
        }



        // ==================================================================================================================
        /// <summary>
        /// Importa masivamente respetando la estructura de carpetas.
        /// </summary>
        public async Task<bool> ImportBlocksMassivelyAsync(Dictionary<string, string> xmlPathsWithGroups, string tableNameToRollback = "", string tableGroupPathToRollback = "")
        {
            try
            {
                if (_cacheService.CurrentPlc == null) return false;

                var sortedKeys = xmlPathsWithGroups.Keys.OrderBy(p =>
                {
                    string name = Path.GetFileName(p).ToUpper();
                    if (name.StartsWith("FC")) return 1;
                    if (name.StartsWith("FB")) return 2;
                    if (name.StartsWith("DB")) return 3;
                    return 4;
                }).ToList();

                List<string> successfullyImportedBlocks = new List<string>();
                bool allOk = true;
                int counter = 1;

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Importando bloques generados..."))
                {
                    var rootBlockGroup = _cacheService.CurrentPlc.BlockGroup;

                    foreach (var xmlPath in sortedKeys)
                    {
                        if (File.Exists(xmlPath))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(xmlPath);
                            string targetGroupPath = xmlPathsWithGroups[xmlPath];

                            _statusService.Set($"[TIA-PLC-IMP-EXP-SERVICE] Importando ({counter}/{sortedKeys.Count}): {fileName}...", StatusType.Warning);
                            await Task.Delay(10);

                            try
                            {
                                using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Import {fileName}"))
                                {
                                    var targetFolder = GetOrCreateBlockGroup(rootBlockGroup.Groups, targetGroupPath);
                                    var targetBlockComposition = targetFolder != null ? targetFolder.Blocks : rootBlockGroup.Blocks;

                                    targetBlockComposition.Import(new FileInfo(xmlPath), ImportOptions.Override);
                                    transaction.CommitOnDispose();
                                }
                                successfullyImportedBlocks.Add(fileName);
                            }
                            catch (Exception blockEx)
                            {
                                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] Error fatal en '{fileName}': {blockEx.Message}", true);
                                allOk = false;
                                break;
                            }
                            counter++;
                        }
                    }

                    // --- LÓGICA DE ROLLBACK TOTAL ---
                    if (!allOk)
                    {
                        _statusService.Set("[TIA-PLC-IMP-EXP-SERVICE] Error detectado. Ejecutando Rollback...", StatusType.Error);

                        // 1. Borramos los bloques importados
                        foreach (var importedName in successfullyImportedBlocks)
                        {
                            try
                            {
                                var xmlPathOriginal = sortedKeys.FirstOrDefault(k => Path.GetFileNameWithoutExtension(k) == importedName);
                                if (xmlPathOriginal != null)
                                {
                                    string groupPath = xmlPathsWithGroups[xmlPathOriginal];
                                    var targetFolder = GetOrCreateBlockGroup(rootBlockGroup.Groups, groupPath);
                                    var blockComposition = targetFolder != null ? targetFolder.Blocks : rootBlockGroup.Blocks;

                                    var blockToDelete = blockComposition.Find(importedName);
                                    if (blockToDelete != null) blockToDelete.Delete();
                                }
                            }
                            catch { }
                        }

                        // 2. Borramos la tabla de variables (Buscándola en su carpeta correspondiente)
                        if (!string.IsNullOrEmpty(tableNameToRollback))
                        {
                            try
                            {
                                var targetFolder = GetOrCreateTagTableGroup(_cacheService.CurrentPlc.TagTableGroup.Groups, tableGroupPathToRollback);
                                var tagTableComposition = targetFolder != null ? targetFolder.TagTables : _cacheService.CurrentPlc.TagTableGroup.TagTables;

                                var table = tagTableComposition.Find(tableNameToRollback);
                                if (table != null) table.Delete();
                            }
                            catch { }
                        }

                        // 3. LIMPIEZA DE CARPETAS VACÍAS
                        // a) Limpiamos las carpetas de bloques
                        foreach (var path in xmlPathsWithGroups.Values.Distinct())
                        {
                            CleanupEmptyBlockGroups(rootBlockGroup.Groups, path);
                        }

                        // b) Limpiamos las carpetas de tablas
                        if (!string.IsNullOrEmpty(tableGroupPathToRollback))
                        {
                            CleanupEmptyTagTableGroups(_cacheService.CurrentPlc.TagTableGroup.Groups, tableGroupPathToRollback);
                        }
                    }
                }

                _cacheService.BuildBlockCache();
                return allOk;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] Error en importación masiva: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Importa un archivo XML de una Tabla de Variables y la ubica en su carpeta correspondiente.
        /// </summary>
        public async Task<bool> ImportTagTableAsync(string xmlPath, string groupPath)
        {
            try
            {
                if (_cacheService.CurrentPlc == null || !File.Exists(xmlPath)) return false;

                string fileName = Path.GetFileNameWithoutExtension(xmlPath);
                _statusService.Set($"[TIA-PLC-IMP-EXP-SERVICE] [ImportTagTableAsync] Importando Tabla de Variables: {fileName}...", StatusType.Warning);
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Importando Tabla de Variables..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Importación Tabla {fileName}"))
                    {
                        var rootTagTableGroup = _cacheService.CurrentPlc.TagTableGroup;

                        // Buscamos o creamos la carpeta destino para la tabla
                        var targetFolder = GetOrCreateTagTableGroup(rootTagTableGroup.Groups, groupPath);
                        var targetTagTableComposition = targetFolder != null ? targetFolder.TagTables : rootTagTableGroup.TagTables;

                        // Importamos en la carpeta correcta
                        targetTagTableComposition.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        transaction.CommitOnDispose();
                    }
                }

                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [ImportTagTableAsync] Tabla de variables importada con éxito: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [ImportTagTableAsync] Error al importar la tabla de variables: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Compila un bloque específico de forma asíncrona
        /// </summary>
        public async Task<bool> CompileBlockAsync(string blockName)
        {
            try
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [CompileBlockAsync] Buscando bloque '{blockName}' para compilar...");
                var block = _cacheService.FindBlockByName(blockName);

                if (block == null)
                {
                    _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [CompileBlockAsync] No se encontró el bloque '{blockName}'", true);
                    return false;
                }

                ICompilable compileService = block.GetService<ICompilable>();
                if (compileService != null)
                {
                    _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [CompileBlockAsync] Compilando: {blockName}...");

                    await Task.Delay(25); // Cede el hilo a WPF antes de la compilación pesada

                    CompilerResult result = compileService.Compile();
                    _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [CompileBlockAsync] Resultado Compilación: {result.State} (Errores: {result.ErrorCount})");
                    return result.State != CompilerResultState.Error;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] [CompileBlockAsync] Fallo al compilar: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Compila TODO el bloque de software del PLC de una sola vez, resolviendo las dependencias cruzadas automáticamente.
        /// </summary>
        public async Task<bool> CompileSoftwareAsync()
        {
            try
            {
                if (_cacheService.CurrentPlc == null) return false;

                _statusService.Set("[TIA-PLC-IMP-EXP-SERVICE] Compilando todo el software del PLC...", StatusType.Warning);
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Compilando Software (ZC ALM TOOLS)..."))
                {
                    // Obtenemos el servicio de compilación directamente del grupo de bloques
                    var compileService = _cacheService.CurrentPlc.BlockGroup.GetService<ICompilable>();
                    if (compileService != null)
                    {
                        var result = compileService.Compile();
                        // Compilación exitosa o con Advertencias se considera OK
                        return result.State == CompilerResultState.Success || result.State == CompilerResultState.Warning;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-IMP-EXP-SERVICE] Error en compilación de software: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Borra las carpetas de bloques de abajo hacia arriba, solo si están completamente vacías.
        /// </summary>
        private void CleanupEmptyBlockGroups(PlcBlockUserGroupComposition rootGroups, string groupPath)
        {
            if (string.IsNullOrEmpty(groupPath)) return;
            string[] folders = groupPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            // Recorremos de la subcarpeta más profunda hacia la raíz
            for (int i = folders.Length; i > 0; i--)
            {
                PlcBlockUserGroup currentGroup = null;
                PlcBlockUserGroupComposition currentGroupCollection = rootGroups;
                bool found = true;

                for (int j = 0; j < i; j++)
                {
                    currentGroup = currentGroupCollection.Find(folders[j]);
                    if (currentGroup == null) { found = false; break; }
                    currentGroupCollection = currentGroup.Groups;
                }

                if (found && currentGroup != null)
                {
                    // Solo se borra si no tiene bloques ni otras subcarpetas
                    if (currentGroup.Blocks.Count == 0 && currentGroup.Groups.Count == 0)
                    {
                        try { currentGroup.Delete(); } catch { }
                    }
                }
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Borra las carpetas de tablas de variables de abajo hacia arriba, solo si están completamente vacías.
        /// </summary>
        private void CleanupEmptyTagTableGroups(PlcTagTableUserGroupComposition rootGroups, string groupPath)
        {
            if (string.IsNullOrEmpty(groupPath)) return;
            string[] folders = groupPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = folders.Length; i > 0; i--)
            {
                PlcTagTableUserGroup currentGroup = null;
                PlcTagTableUserGroupComposition currentGroupCollection = rootGroups;
                bool found = true;

                for (int j = 0; j < i; j++)
                {
                    currentGroup = currentGroupCollection.Find(folders[j]);
                    if (currentGroup == null) { found = false; break; }
                    currentGroupCollection = currentGroup.Groups;
                }

                if (found && currentGroup != null)
                {
                    if (currentGroup.TagTables.Count == 0 && currentGroup.Groups.Count == 0)
                    {
                        try { currentGroup.Delete(); } catch { }
                    }
                }
            }
        }



    }
}
