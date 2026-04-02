using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.Library;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{

    /// <summary>
    /// Servicio para comunicación directa con Siemens Openness
    /// </summary>
    public class TiaPlcService
    {

        private PlcSoftware _currentPlc;
        private Project _currentProject;
        private Siemens.Engineering.TiaPortal _tiaApp;

        // Diccionarios de caché en RAM
        private List<CachedPlcBlock> _plcCache;
        private List<CachedPlcTagTable> _tagTableCache;
        private List<CachedPlcType> _typeCache;
        private bool _isCacheBuilt = false;

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;
        private readonly IAppConfigService _appConfigService;

        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public TiaPlcService(Siemens.Engineering.TiaPortal tiaApp, Project project,
                             ILogService logService, IStatusService statusService,
                             IAppConfigService appConfigService)
        {
            _tiaApp = tiaApp;
            _currentProject = project;

            _logService = logService;
            _statusService = statusService;
            _appConfigService = appConfigService;
        }



        /// <summary>
        /// Busca una librería global por nombre y, si no está abierta, la abre desde la ruta especificada.
        /// </summary>
        public GlobalLibrary GetOrOpenGlobalLibrary(string libraryPath)
        {
            if (_tiaApp == null)
            {
                _logService.Write("[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] ERROR: Instancia de TIA Portal no asignada al servicio.", true);
                return null;
            }

            if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath))
            {
                _logService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] La ruta es inválida o el archivo no existe: {libraryPath}", true);
                return null;
            }

            try
            {
                FileInfo libFile = new FileInfo(libraryPath);
                string libraryName = Path.GetFileNameWithoutExtension(libFile.Name);

                // 1. Comprobar si ya está abierta en la instancia actual
                var openedLibrary = _tiaApp.GlobalLibraries.FirstOrDefault(l =>
                    l.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

                if (openedLibrary != null)
                {
                    _logService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] Librería global '{libraryName}' ya se encuentra abierta.");
                    return openedLibrary;
                }

                // 2. Si no está abierta, pedir a Openness que la abra
                _statusService.Set($"Abriendo librería global '{libraryName}'...", StatusType.Warning);

                // OpenMode.ReadOnly es crucial para evitar bloqueos si la librería está en uso por otro proceso
                var newOpenedLibrary = _tiaApp.GlobalLibraries.Open(libFile, OpenMode.ReadOnly);

                _logService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] Librería '{libraryName}' abierta correctamente.");
                return newOpenedLibrary;
            }
            catch (EngineeringSecurityException)
            {
                // Se relanza para que el ViewModel lo capture y avise al usuario en la UI
                throw;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] Excepción al abrir la librería: {ex.Message}", true);
                return null;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Asignacion de PLC seleccionado
        /// </summary>
        public void UpdatePlc(PlcSoftware plcSoftware)
        {
            if (_currentPlc != plcSoftware)
            {
                _currentPlc = plcSoftware;

                // Si cambiamos de PLC, destruimos la caché antigua
                _isCacheBuilt = false;
                _plcCache?.Clear();
                _tagTableCache?.Clear();
                _typeCache?.Clear();
                _logService.Write("[TIA-PLC-SERVICE] [UpdatePlc] PLC modificado. Caché invalidada.");
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Construye el índice completo del PLC en memoria RAM
        /// </summary>
        public void BuildBlockCache()
        {
            try
            {
                if (_currentPlc == null) return;

                _plcCache = new List<CachedPlcBlock>();
                _tagTableCache = new List<CachedPlcTagTable>();
                _typeCache = new List<CachedPlcType>();

                _logService.Write("[TIA-PLC-SERVICE] [BuildBlockCache] Indexando todos los bloques del PLC en memoria...");

                PopulateCacheRecursively(_currentPlc.BlockGroup, "Root");
                PopulateTagTableCacheRecursively(_currentPlc.TagTableGroup, "Variables de PLC");
                PopulateTypeCacheRecursively(_currentPlc.TypeGroup, "Tipos de datos PLC");

                _isCacheBuilt = true;
                _logService.Write($"[TIA-PLC-SERVICE] [BuildBlockCache] Indexación completa: {_plcCache.Count} bloques guardados en caché.");
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [BuildBlockCache] Error construyendo la caché: {ex.Message}", true);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Relleno de la cache de Tipos de Datos de Usuario (UDTs)
        /// </summary>
        private void PopulateTypeCacheRecursively(PlcTypeGroup group, string currentPath)
        {
            foreach (var type in group.Types)
            {
                _typeCache.Add(new CachedPlcType
                {
                    Type = type,
                    Name = type.Name,
                    FolderPath = currentPath
                });
            }

            foreach (var subFolder in group.Groups)
            {
                string nextPath = currentPath == "Tipos de datos PLC" ? subFolder.Name : currentPath + "\\" + subFolder.Name;
                PopulateTypeCacheRecursively(subFolder, nextPath);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Relleno de la cache de tabla de variables
        /// </summary>
        private void PopulateTagTableCacheRecursively(PlcTagTableGroup group, string currentPath)
        {
            foreach (var table in group.TagTables)
            {
                _tagTableCache.Add(new CachedPlcTagTable
                {
                    Table = table,
                    Name = table.Name,
                    FolderPath = currentPath
                });
            }

            foreach (var subFolder in group.Groups)
            {
                string nextPath = currentPath == "Variables de PLC" ? subFolder.Name : currentPath + "\\" + subFolder.Name;
                PopulateTagTableCacheRecursively(subFolder, nextPath);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Relleno de la cache de bloques
        /// </summary>
        private void PopulateCacheRecursively(PlcBlockGroup group, string currentPath)
        {
            foreach (var block in group.Blocks)
            {
                // Averiguar el tipo simple
                string simpleType = "";
                if (block is GlobalDB || block is InstanceDB || block is ArrayDB) simpleType = "DB";
                else if (block is FC) simpleType = "FC";
                else if (block is FB) simpleType = "FB";
                else if (block is OB) simpleType = "OB";

                // Añadir a nuestra única lista
                _plcCache.Add(new CachedPlcBlock
                {
                    Block = block,
                    Name = block.Name,
                    Number = block.Number,
                    ApiType = block.GetType().Name,
                    SimpleType = simpleType,
                    FolderPath = currentPath,
                    ProgrammingLanguage = block.ProgrammingLanguage.ToString()
                });
            }

            foreach (var subFolder in group.Groups)
            {
                string nextPath = currentPath == "Root" ? subFolder.Name : currentPath + "\\" + subFolder.Name;
                PopulateCacheRecursively(subFolder, nextPath);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Devuelve la cache de los UDTs del PLC
        /// </summary>
        public List<CachedPlcType> GetAllTypes()
        {
            if (!_isCacheBuilt) BuildBlockCache();
            return _typeCache ?? new List<CachedPlcType>();
        }



        // ==================================================================================================================
        /// <summary>
        /// Debuelve la cache de los bloques del PLC
        /// </summary>
        public List<CachedPlcBlock> GetAllBlocks()
        {
            if (!_isCacheBuilt) BuildBlockCache();
            return _plcCache ?? new List<CachedPlcBlock>();
        }



        // ==================================================================================================================
        /// <summary>
        /// Exportar el contenido de la caché a un archivo TXT para análisis
        /// </summary>
        public void DumpCacheToTxt(string filePath)
        {
            try
            {
                if (!_isCacheBuilt || _plcCache == null) return;

                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("=========================================================");
                    writer.WriteLine("             VOLCADO DE CACHÉ DE TIA PORTAL              ");
                    writer.WriteLine("=========================================================");
                    writer.WriteLine($"Fecha de volcado: {DateTime.Now}");
                    writer.WriteLine($"PLC: {_currentPlc?.Name}");
                    writer.WriteLine($"Total Bloques: {_plcCache.Count} | Total Tablas: {_tagTableCache?.Count ?? 0} | Total UDTs: { _typeCache?.Count ?? 0}");
                    writer.WriteLine("=========================================================\n");

                    writer.WriteLine("=== BLOQUES (OB/FC/FB/DB) ===");

                    // Ordenamos la lista alfabéticamente solo para imprimirla bonita
                    foreach (var item in _plcCache.OrderBy(b => b.Name))
                    {
                        writer.WriteLine($"[Nombre] {item.Name,-35} | [Num] {item.Number,-5} | [Tipo API] {item.ApiType,-12} | [Ruta] {item.FolderPath}");
                    }

                    writer.WriteLine("\n=== TABLAS DE VARIABLES ===");
                    if (_tagTableCache != null)
                    {
                        foreach (var item in _tagTableCache.OrderBy(t => t.Name))
                        {
                            writer.WriteLine($"[Nombre] {item.Name,-35} | [Ruta] {item.FolderPath}");
                        }
                    }

                    writer.WriteLine("\n=== TIPOS DE DATOS DE USUARIO (UDT) ===");
                    if (_typeCache != null)
                    {
                        foreach (var item in _typeCache.OrderBy(t => t.Name))
                        {
                            writer.WriteLine($"[Nombre] {item.Name,-35} | [Ruta] {item.FolderPath}");
                        }
                    }
                }
                _logService.Write($"[TIA-PLC-SERVICE] [DumpCache] Caché exportada exitosamente a: {filePath}");
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [DumpCache] Error exportando la caché: {ex.Message}", true);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar tabla de variables
        /// </summary>
        public PlcTagTable FindTagTableByName(string tableName)
        {
            if (_currentPlc == null) return null;

            // Si la cache no esta construida, lo hacemos ahora
            if (!_isCacheBuilt) BuildBlockCache();

            return _tagTableCache?.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))?.Table;
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar bloque por nombre
        /// </summary>
        public PlcBlock FindBlockByName(string blockName)
        {
            if (_currentPlc == null) return null;

            // Si la cache no esta construida, lo hacemos ahora
            if (!_isCacheBuilt) BuildBlockCache();

            return _plcCache?.FirstOrDefault(b => b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase))?.Block;
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar UDT por nombre
        /// </summary>
        public PlcType FindTypeByName(string typeName)
        {
            if (_currentPlc == null) return null;

            if (!_isCacheBuilt) BuildBlockCache();

            return _typeCache?.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))?.Type;
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar bloque por numero
        /// </summary>
        public PlcBlock FindBlockByNumber(int number, string blockType)
        {
            if (_currentPlc == null) return null;

            // Si la cache no esta construida, lo hacemos ahora
            if (!_isCacheBuilt) BuildBlockCache();

            return _plcCache?.FirstOrDefault(b => b.Number == number && b.SimpleType.Equals(blockType, StringComparison.OrdinalIgnoreCase))?.Block;
        }




        // ==================================================================================================================
        /// <summary>
        /// Lee el valor de una constante global
        /// </summary>
        public int ReadGlobalConstant(string tableName, string constantName)
        {
            try
            {
                var table = FindTagTableByName(tableName);
                if (table == null) return -1;

                var constant = table.UserConstants.Find(constantName);
                if (constant != null && int.TryParse(constant.Value, out int value))
                {
                    return value;
                }
                return 0;
            }
            catch
            {
                return -1;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Exportar un bloque a XML de forma asíncrona
        /// </summary>
        public async Task<bool> ExportBlockToXmlAsync(string blockName, string destinationPath)
        {
            try
            {
                var block = FindBlockByName(blockName);
                if (block == null)
                {
                    _logService.Write($"[TIA-PLC-SERVICE] [ExportBlockToXmlAsync] No se encontró el bloque '{blockName}'.", true);
                    return false;
                }

                if (File.Exists(destinationPath)) File.Delete(destinationPath);

                await Task.Delay(25); // Cede el hilo a WPF para pintar la UI antes de bloquear

                block.Export(new FileInfo(destinationPath), ExportOptions.WithDefaults);
                _logService.Write($"[TIA-PLC-SERVICE] [ExportBlockToXmlAsync] Bloque '{blockName}' exportado a {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [ExportBlockToXmlAsync] Error exportando bloque '{blockName}': {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Exporta un bloque o UDT como archivo fuente de texto plano (.scl, .awl, .db, .udt)
        /// </summary>
        public async Task<bool> ExportAsSourceAsync(object item, string destinationPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_currentPlc == null) return false;

                    // Borramos el archivo si ya existía de una compilación anterior
                    if (File.Exists(destinationPath)) File.Delete(destinationPath);

                    // Si es un bloque de código (FC, FB, OB, DB)
                    if (item is PlcBlock block)
                    {
                        _currentPlc.ExternalSourceGroup.GenerateSource(new List<PlcBlock> { block }, new FileInfo(destinationPath), GenerateOptions.None);
                        return true;
                    }
                    // Si es un Tipo de Datos de Usuario (UDT)
                    else if (item is PlcType udt)
                    {
                        _currentPlc.ExternalSourceGroup.GenerateSource(new List<PlcType> { udt }, new FileInfo(destinationPath), GenerateOptions.None);
                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logService.Write($"[TIA-PLC-SERVICE] [ExportAsSourceAsync] Error exportando fuente a {destinationPath}: {ex.Message}", true);
                    return false;
                }
            });
        }



        // ==================================================================================================================
        /// <summary>
        /// Proceso maestro para actualizar y reimportar masivamente dependencias SCL
        /// </summary>
        public async Task<bool> UpdateMassiveSclDependencies(List<CachedPlcBlock> blocksToProcess)
        {
            try
            {
                _statusService.Set($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Iniciando proceso para {blocksToProcess.Count} bloques.", StatusType.Ok); 

                string tempDir = AppConfigService.TempExportPathVci;
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                // Exportaremos los bloques finales aquí para dárselos a tu herramienta Python de documentación
                string pythonDocsDir = _appConfigService.GetGlobalSettings().DocExportSourcesPath;
                if (string.IsNullOrWhiteSpace(pythonDocsDir))
                {
                    pythonDocsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ZC_Exportaciones_SCL");
                }

                StringBuilder massiveSclFile = new StringBuilder();

                int counter = 1;
                foreach (var cachedBlock in blocksToProcess)
                {
                    _statusService.Set($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Analizando e inyectando {cachedBlock.Name} ({counter}/{blocksToProcess.Count})...", StatusType.Ok);

                    string xmlTempPath = Path.Combine(tempDir, $"{cachedBlock.Name}.xml");
                    string sclTempPath = Path.Combine(pythonDocsDir, $"{cachedBlock.Name}.scl");

                    // 1. Exportar a XML para leer dependencias "seguras" (Con Auto-Compilación)
                    if (File.Exists(xmlTempPath)) File.Delete(xmlTempPath);

                    try
                    {
                        await Task.Delay(25); // Cedemos el hilo a WPF antes de exportar
                        cachedBlock.Block.Export(new FileInfo(xmlTempPath), ExportOptions.WithDefaults);
                    }
                    catch (Exception ex) when (ex.Message.Contains("Inconsistent"))
                    {
                        _statusService.Set($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] El bloque {cachedBlock.Name} requiere compilación previa...", StatusType.Warning);

                        // Usamos el método CompileBlockAsync (que ya tiene su propio Delay interno)
                        if (await CompileBlockAsync(cachedBlock.Name))
                        {
                            await Task.Delay(25); // Cedemos el hilo nuevamente
                            cachedBlock.Block.Export(new FileInfo(xmlTempPath), ExportOptions.WithDefaults);
                        }
                        else
                        {
                            throw new Exception($"El bloque {cachedBlock.Name} tiene errores de programación en TIA Portal y no se puede compilar ni exportar.");
                        }
                    }

                    string dependenciesText = ExtractDependenciesFromXml(xmlTempPath);

                    // 2. Exportar el bloque limpio a SCL para la herramienta Python y para modificarlo
                    if (File.Exists(sclTempPath)) File.Delete(sclTempPath);
                    var listForExport = new List<PlcBlock> { cachedBlock.Block };

                    await Task.Delay(25);

                    _currentPlc.ExternalSourceGroup.GenerateSource(listForExport, new FileInfo(sclTempPath), GenerateOptions.None);

                    // 3. Inyectar dependencias mediante C# (Expresiones regulares)
                    var utf8Bom = new UTF8Encoding(true);

                    string sclContent = File.ReadAllText(sclTempPath, utf8Bom);
                    string updatedScl = InjectRequiresIntoScl(sclContent, dependenciesText);

                    // Guardamos el SCL individual actualizado
                    File.WriteAllText(sclTempPath, updatedScl, utf8Bom);

                    // 4. Añadirlo a nuestro "Mega Archivo" de importación masiva para TIA Portal
                    massiveSclFile.AppendLine(updatedScl);
                    massiveSclFile.AppendLine();

                    counter++;
                }

                _statusService.Set($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Importando masivamente a TIA Portal. Por favor, espera...", StatusType.Warning);

                // 5. IMPORTACIÓN MASIVA a TIA PORTAL
                string massiveImportPath = Path.Combine(tempDir, "MassiveImport.scl");
                var finalUtf8Bom = new System.Text.UTF8Encoding(true);
                File.WriteAllText(massiveImportPath, massiveSclFile.ToString(), finalUtf8Bom);

                await Task.Delay(25); // Cedemos el hilo antes de la transacción gigante

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Actualizando masivamente dependencias SCL (ZC ALM TOOLS)..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Iniciando actualizacion para {blocksToProcess.Count} bloques."))
                    {
                        var extSources = _currentPlc.ExternalSourceGroup.ExternalSources;
                        PlcExternalSource source = extSources.CreateFromFile("UpdateMasivo_Temp", massiveImportPath);

                        await Task.Delay(25); // Último respiro antes de la importación pesada
                        // AQUÍ SÍ VA EL OVERWRITE. Al importar a TIA Portal, machacamos los bloques existentes.
                        source.GenerateBlocksFromSource(GenerateBlockOption.None);

                        // Limpieza en TIA Portal
                        source.Delete();

                        transaction.CommitOnDispose();
                    }
                }

                _statusService.Set($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Proceso completado con éxito.", StatusType.Ok);
                return true;
            }
            catch (Exception ex)
            {
                _statusService.Set($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Error al importar el codigo. Revisa el log.", StatusType.Ok);
                _logService.Write($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Error crítico: {ex.Message}", true);
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
                if (_currentPlc == null) return false;

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
                    var rootBlockGroup = _currentPlc.BlockGroup;

                    foreach (var xmlPath in sortedKeys)
                    {
                        if (File.Exists(xmlPath))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(xmlPath);
                            string targetGroupPath = xmlPathsWithGroups[xmlPath];

                            _statusService.Set($"[TIA-PLC-SERVICE] Importando ({counter}/{sortedKeys.Count}): {fileName}...", StatusType.Warning);
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
                                _logService.Write($"[TIA-PLC-SERVICE] Error fatal en '{fileName}': {blockEx.Message}", true);
                                allOk = false;
                                break;
                            }
                            counter++;
                        }
                    }

                    // --- LÓGICA DE ROLLBACK TOTAL ---
                    if (!allOk)
                    {
                        _statusService.Set("[TIA-PLC-SERVICE] Error detectado. Ejecutando Rollback...", StatusType.Error);

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
                                var targetFolder = GetOrCreateTagTableGroup(_currentPlc.TagTableGroup.Groups, tableGroupPathToRollback);
                                var tagTableComposition = targetFolder != null ? targetFolder.TagTables : _currentPlc.TagTableGroup.TagTables;

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
                            CleanupEmptyTagTableGroups(_currentPlc.TagTableGroup.Groups, tableGroupPathToRollback);
                        }
                    }
                }

                BuildBlockCache();
                return allOk;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] Error en importación masiva: {ex.Message}", true);
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



        // ==================================================================================================================
        /// <summary>
        /// Crea una nueva tabla de variables en TIA Portal (Fase 2.5)
        /// </summary>
        public async Task<bool> CreateTagTableAsync(string tableName)
        {
            try
            {
                if (_currentPlc == null) return false;
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Creando Tabla de Variables..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Crear Tabla {tableName}"))
                    {
                        if (FindTagTableByName(tableName) == null)
                        {
                            _currentPlc.TagTableGroup.TagTables.Create(tableName);
                        }
                        transaction.CommitOnDispose();
                    }
                }
                BuildBlockCache();
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [CreateTagTableAsync] Error creando tabla de variables '{tableName}': {ex.Message}", true);
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
                if (_currentPlc == null || !File.Exists(xmlPath)) return false;

                string fileName = Path.GetFileNameWithoutExtension(xmlPath);
                _statusService.Set($"[TIA-PLC-SERVICE] [ImportTagTableAsync] Importando Tabla de Variables: {fileName}...", StatusType.Warning);
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Importando Tabla de Variables..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Importación Tabla {fileName}"))
                    {
                        var rootTagTableGroup = _currentPlc.TagTableGroup;

                        // Buscamos o creamos la carpeta destino para la tabla
                        var targetFolder = GetOrCreateTagTableGroup(rootTagTableGroup.Groups, groupPath);
                        var targetTagTableComposition = targetFolder != null ? targetFolder.TagTables : rootTagTableGroup.TagTables;

                        // Importamos en la carpeta correcta
                        targetTagTableComposition.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        transaction.CommitOnDispose();
                    }
                }

                _logService.Write($"[TIA-PLC-SERVICE] [ImportTagTableAsync] Tabla de variables importada con éxito: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [ImportTagTableAsync] Error al importar la tabla de variables: {ex.Message}", true);
                return false;
            }
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
        /// Lee el XML de Siemens y extrae todas las referencias cruzadas con el formato estándar de Siemens LGF
        /// </summary>
        private string ExtractDependenciesFromXml(string xmlPath)
        {
            try
            {
                XDocument doc = XDocument.Load(xmlPath);

                // 1. FUERZA BRUTA: Sacamos TODOS los atributos "Name" de cualquier nodo del XML
                var allNamesInXml = doc.Descendants()
                    .Where(e => e.Attribute("Name") != null)
                    .Select(e => e.Attribute("Name").Value)
                    .Distinct()
                    .ToList();

                // 2. Separamos FBs y FCs explícitamente para el formato de Siemens
                var fcCalls = _plcCache
                    .Where(b => b.SimpleType == "FC" && allNamesInXml.Contains(b.Name))
                    .Select(b => b.Name)
                    .OrderBy(name => name)
                    .ToList();

                var fbCalls = _plcCache
                    .Where(b => b.SimpleType == "FB" && allNamesInXml.Contains(b.Name))
                    .Select(b => b.Name)
                    .OrderBy(name => name)
                    .ToList();

                // 3. Cruzamos todos esos nombres con nuestra caché de DBs
                var dbCalls = _plcCache
                    .Where(b => b.SimpleType == "DB" && allNamesInXml.Contains(b.Name))
                    .Select(b => b.Name)
                    .OrderBy(name => name)
                    .ToList();

                // 4. LOS UDTs: Los cazamos del Interface y cruzamos con la caché en RAM
                var quotedTypes = doc.Descendants()
                    .Where(e => e.Name.LocalName == "Member" && e.Attribute("Datatype") != null)
                    .Select(e => e.Attribute("Datatype").Value)
                    .Where(dt => dt.Contains("\""))
                    .SelectMany(dt =>
                    {
                        var matches = Regex.Matches(dt, "\"([^\"]+)\"");
                        return matches.Cast<Match>().Select(m => m.Groups[1].Value);
                    })
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToList();

                var interfaceTypes = _typeCache
                    .Where(t => quotedTypes.Contains(t.Name))
                    .Select(t => t.Name)
                    .OrderBy(name => name)
                    .ToList();

                // 5. CONSTRUCCIÓN DEL STRING FORMATO SIEMENS
                if (!fcCalls.Any() && !fbCalls.Any() && !dbCalls.Any() && !interfaceTypes.Any())
                {
                    return "--"; // Estándar Siemens cuando no hay datos
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(); // Salto de línea inicial para empezar debajo de "Requirements:"

                if (fcCalls.Any()) sb.AppendLine($"    //   - FC:  {string.Join(", ", fcCalls)}");
                if (fbCalls.Any()) sb.AppendLine($"    //   - FB:  {string.Join(", ", fbCalls)}");
                if (dbCalls.Any()) sb.AppendLine($"    //   - DB:  {string.Join(", ", dbCalls)}");
                if (interfaceTypes.Any()) sb.AppendLine($"    //   - UDT: {string.Join(", ", interfaceTypes)}");

                return sb.ToString().TrimEnd(); // Evitamos un salto de línea extra al final
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] Error extrayendo XML: {ex.Message}", true);
                return "--";
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo principal para calcular los bloques proyectados a partir de una plantilla y un proceso, aplicando el calculo
        /// a números y rutas de carpeta, y devolviendo una lista de ProjectedBlock con toda la información necesaria para la generación posterior.
        /// </summary>
        public List<ProjectedBlock> CalculateProjectedBlocks(string templateRootPath, string selectedTemplate, string processIdStr, string processCode)
        {
            var projectedBlocks = new List<ProjectedBlock>();

            if (!int.TryParse(processIdStr, out int processId)) return projectedBlocks;

            string templateIdStr = selectedTemplate.Split('_')[0];
            if (!int.TryParse(templateIdStr, out int templateId)) return projectedBlocks;

            // --- EL CÁLCULO MÁGICO (DELTA) ---
            int templateBase = 50000 + templateId; // Ej: 50100
            int delta = processId - templateBase;  // Ej: Proceso 500 - 50100 = -49600

            string fullTemplatePath = Path.Combine(templateRootPath, selectedTemplate);
            string blocksPath = Path.Combine(fullTemplatePath, "Bloques");
            string tablePath = Path.Combine(fullTemplatePath, "Tabla");

            if (!Directory.Exists(blocksPath))
                throw new DirectoryNotFoundException($"Carpeta de bloques no encontrada: {blocksPath}");

            string[] xmlFiles = Directory.GetFiles(blocksPath, "*.xml", SearchOption.AllDirectories);

            Regex blockRegex = new Regex(@"^(FC|FB|DB)(\d+)", RegexOptions.IgnoreCase);
            Regex numberRegex = new Regex(@"5\d{4}");

            // 1. PROCESAR BLOQUES LÓGICOS
            foreach (var filePath in xmlFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                Match match = blockRegex.Match(fileName);

                if (match.Success)
                {
                    string blockType = match.Groups[1].Value.ToUpper();
                    int originalNumber = int.Parse(match.Groups[2].Value);

                    // Aplicamos el salto exacto al número del bloque
                    int projectedNumber = originalNumber >= 50000 ? originalNumber + delta : originalNumber;

                    // --- CÁLCULO DE LA CARPETA ---
                    string relativeFolderPath = Path.GetDirectoryName(filePath)
                                                    .Replace(blocksPath, "")
                                                    .TrimStart(Path.DirectorySeparatorChar);

                    // Aplicamos el mismo salto a los números de la carpeta
                    relativeFolderPath = numberRegex.Replace(relativeFolderPath, m =>
                    {
                        int num = int.Parse(m.Value);
                        return (num + delta).ToString(); // Ej: 53100 -> 3500
                    });

                    // Reemplazamos el texto genérico de la plantilla
                    relativeFolderPath = Regex.Replace(relativeFolderPath, "Compacto", processCode, RegexOptions.IgnoreCase);

                    // --- CÁLCULO DEL NOMBRE DEL ARCHIVO ---
                    string[] nameParts = fileName.Split('_');
                    if (nameParts.Length >= 2)
                    {
                        nameParts[0] = $"{blockType}{projectedNumber}"; // Ej: DB3501
                        nameParts[1] = processCode;                     // Ej: PINT
                    }

                    projectedBlocks.Add(new ProjectedBlock
                    {
                        Type = blockType,
                        OriginalNumber = originalNumber,
                        OriginalName = fileName,
                        AbsoluteSourcePath = filePath,
                        ProjectedNumber = projectedNumber,
                        ProjectedName = string.Join("_", nameParts),
                        SourceFile = fileName + ".xml",
                        PlcGroupPath = relativeFolderPath,
                        Status = SynchronizationStatus.Pending,
                        Message = "Pendiente de comprobar..."
                    });
                }
            }

            // 2. PROCESAR TABLA DE VARIABLES
            if (Directory.Exists(tablePath))
            {
                string[] tableFiles = Directory.GetFiles(tablePath, "*.xml", SearchOption.AllDirectories);

                foreach (var tableFilePath in tableFiles)
                {
                    string originalTableName = Path.GetFileNameWithoutExtension(tableFilePath);

                    Match tableMatch = Regex.Match(originalTableName, @"5\d{4}");
                    int originalTableNum = tableMatch.Success ? int.Parse(tableMatch.Value) : templateBase;

                    // Aplicamos el delta
                    int projectedTableNum = tableMatch.Success ? (originalTableNum + delta) : processId;

                    // --- CÁLCULO DE LA CARPETA ---
                    string relativeFolderPath = Path.GetDirectoryName(tableFilePath)
                                                    .Replace(tablePath, "")
                                                    .TrimStart(Path.DirectorySeparatorChar);

                    // Reemplazamos los números aplicando el Delta (Ej: 53100 -> 3500)
                    relativeFolderPath = numberRegex.Replace(relativeFolderPath, m =>
                    {
                        int num = int.Parse(m.Value);
                        return (num + delta).ToString();
                    });

                    // Reemplazamos el texto genérico
                    relativeFolderPath = Regex.Replace(relativeFolderPath, "Compacto", processCode, RegexOptions.IgnoreCase);

                    projectedBlocks.Add(new ProjectedBlock
                    {
                        Type = "Tabla",
                        OriginalNumber = originalTableNum,
                        OriginalName = originalTableName,
                        AbsoluteSourcePath = tableFilePath,
                        ProjectedNumber = projectedTableNum,
                        ProjectedName = $"{projectedTableNum}_{processCode}",
                        SourceFile = originalTableName + ".xml",
                        PlcGroupPath = relativeFolderPath, // ¡Guardamos la carpeta calculada!
                        Status = SynchronizationStatus.Pending,
                        Message = "Pendiente de comprobar..."
                    });
                }
            }

            return projectedBlocks;
        }




        // ==================================================================================================================
        /// <summary>
        /// Inyecta las dependencias en la cabecera estándar de Siemens (Requirements)
        /// </summary>
        private string InjectRequiresIntoScl(string originalScl, string dependenciesText)
        {
            if (string.IsNullOrWhiteSpace(dependenciesText))
                dependenciesText = "--";

            // El bloque exacto que queremos escribir
            string newRequirementsBlock = $"// Requirements: {dependenciesText}";

            // Expresión regular: Busca "// Requirements:" y todo lo que haya debajo 
            // hasta chocar con el salto de línea previo a los guiones del separador (//---)
            string pattern = @"//\s*Requirements:.*?(?=\r?\n\s*//-{5,})";

            if (Regex.IsMatch(originalScl, pattern, RegexOptions.Singleline))
            {
                // Reemplazamos el bloque existente por el nuevo actualizado
                return Regex.Replace(originalScl, pattern, newRequirementsBlock, RegexOptions.Singleline);
            }
            else
            {
                // FALLBACK DE SEGURIDAD: 
                // Si el programador borró la línea "Requirements:" por error, la inyectamos 
                // nosotros mismos justo encima de los guiones que separan el Changelog.
                string fallbackPattern = @"(\r?\n\s*//-{5,}\r?\n\s*//\s*Change log table:)";
                if (Regex.IsMatch(originalScl, fallbackPattern))
                {
                    return Regex.Replace(originalScl, fallbackPattern, $"\r\n    {newRequirementsBlock}$1");
                }
            }

            return originalScl; // Si el archivo SCL está totalmente roto o vacío, no hacemos nada
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo principal para preparar los bloques para su importación masiva a TIA Portal. Aplica el cálculo de números y rutas, 
        /// y además hace una cirugía quirúrgica de reemplazo de nombres dentro del XML para asegurar que todas las referencias internas estén actualizadas.
        /// </summary>
        public Dictionary<string, string> PrepareBlocksForImport(
            List<ProjectedBlock> blocksToProcess,
            string tempDirectory,
            string targetProcessCode)
        {
            var processedFiles = new Dictionary<string, string>();
            var symbolDictionary = new Dictionary<string, string>(); // ¡El diccionario mágico!

            // 1. Averiguar automáticamente el Delta y el código original a partir de los bloques
            var sampleBlock = blocksToProcess.FirstOrDefault(b => b.OriginalNumber >= 50000);
            int delta = sampleBlock != null ? (sampleBlock.ProjectedNumber - sampleBlock.OriginalNumber) : 0;

            var sampleNameBlock = blocksToProcess.FirstOrDefault(b => b.OriginalName.Contains("_"));
            string originalProcessCode = sampleNameBlock != null ? sampleNameBlock.OriginalName.Split('_').ElementAtOrDefault(1) : "CPR";

            // 2. LLENAR DICCIONARIO CON LOS BLOQUES
            foreach (var block in blocksToProcess)
            {
                if (!string.IsNullOrEmpty(block.OriginalName) && block.OriginalName != block.ProjectedName)
                {
                    symbolDictionary[block.OriginalName] = block.ProjectedName;
                }
            }

            // 3. LLENAR DICCIONARIO CON VARIABLES Y CONSTANTES DE LA TABLA
            var tableBlock = blocksToProcess.FirstOrDefault(b => b.Type == "Tabla");
            if (tableBlock != null && File.Exists(tableBlock.AbsoluteSourcePath))
            {
                string tableXml = File.ReadAllText(tableBlock.AbsoluteSourcePath);

                // Extraemos todos los nombres de variables y constantes buscando la etiqueta <Name>
                MatchCollection nameMatches = Regex.Matches(tableXml, @"<Name>([^<]+)</Name>");

                foreach (Match match in nameMatches)
                {
                    string originalName = match.Groups[1].Value;

                    // Calculamos el nombre proyectado para esta variable:
                    // a) Aplicamos el Delta a los números de la familia 50.000
                    string projectedName = Regex.Replace(originalName, @"5\d{4}", m => {
                        if (int.TryParse(m.Value, out int num)) return (num + delta).ToString();
                        return m.Value;
                    });

                    // b) Reemplazamos el código del proceso (Ej: "_CPR_" por "_PINT_")
                    if (!string.IsNullOrEmpty(originalProcessCode) && !string.IsNullOrEmpty(targetProcessCode))
                    {
                        projectedName = projectedName.Replace($"_{originalProcessCode}_", $"_{targetProcessCode}_");
                    }

                    // Si el nombre ha cambiado, lo añadimos al diccionario de reemplazos
                    if (originalName != projectedName && !symbolDictionary.ContainsKey(originalName))
                    {
                        symbolDictionary[originalName] = projectedName;
                    }
                }
            }

            // 4. ORDENAR DE MÁS LARGO A MÁS CORTO
            // Truco pro: Evita reemplazar partes de palabras (Ej: DB10 antes que DB100)
            var sortedSymbols = symbolDictionary.Keys.OrderByDescending(k => k.Length).ToList();

            // 5. APLICAR LA CIRUGÍA QUIRÚRGICA A TODOS LOS XML
            foreach (var block in blocksToProcess)
            {
                if (!File.Exists(block.AbsoluteSourcePath)) continue;

                string xmlContent = File.ReadAllText(block.AbsoluteSourcePath);

                // a) Reemplazo del número interno estructural del bloque
                if (block.OriginalNumber > 0 && block.OriginalNumber != block.ProjectedNumber)
                {
                    xmlContent = xmlContent.Replace($"<Number>{block.OriginalNumber}</Number>", $"<Number>{block.ProjectedNumber}</Number>");
                }

                // b) Reemplazo exacto usando nuestro diccionario de símbolos
                foreach (var originalSymbol in sortedSymbols)
                {
                    xmlContent = xmlContent.Replace(originalSymbol, symbolDictionary[originalSymbol]);
                }

                // Guardamos el XML modificado
                string newFilePath = Path.Combine(tempDirectory, $"{block.ProjectedName}.xml");
                File.WriteAllText(newFilePath, xmlContent);
                processedFiles.Add(newFilePath, block.PlcGroupPath);
            }

            return processedFiles;
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
                var table = FindTagTableByName(tableName);
                if (table == null) return false;

                await Task.Delay(25); // Cede el hilo a WPF

                table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                return true;
            }
            catch { return false; }
        }



        // ==================================================================================================================
        /// <summary>
        /// Sincroniza el valor de una constante global de dimensionado de forma asíncrona
        /// </summary>
        public async Task<bool> SyncGlobalConstantAsync(string tableName, string constantName, int newValue)
        {
            try
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstantAsync] Verificando constante: {constantName}...");
                var table = FindTagTableByName(tableName);
                if (table == null) throw new Exception($"No se encontró la tabla '{tableName}'");

                var constant = table.UserConstants.Find(constantName);
                if (constant == null) throw new Exception($"No existe la constante '{constantName}'");

                if (int.TryParse(constant.Value, out int currentValue))
                {
                    if (currentValue != newValue)
                    {
                        _logService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstantAsync] Modificando {constantName}: {currentValue} -> {newValue}");
                        _statusService.Set($"{constantName} actualizado a {newValue}.", StatusType.Ok);

                        await Task.Delay(25);

                        using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Actualizando constante..."))
                        {
                            using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Cambiar constante {constantName}"))
                            {
                                constant.Value = newValue.ToString();
                                transaction.CommitOnDispose();
                            }
                        }
                        return true;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstantAsync] Fallo en Sync Global: {ex.Message}", true);
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
                _logService.Write($"[TIA-PLC-SERVICE] [CompileBlockAsync] Buscando bloque '{blockName}' para compilar...");
                var block = FindBlockByName(blockName);

                if (block == null)
                {
                    _logService.Write($"[TIA-PLC-SERVICE] [CompileBlockAsync] No se encontró el bloque '{blockName}'", true);
                    return false;
                }

                ICompilable compileService = block.GetService<ICompilable>();
                if (compileService != null)
                {
                    _logService.Write($"[TIA-PLC-SERVICE] [CompileBlockAsync] Compilando: {blockName}...");

                    await Task.Delay(25); // Cede el hilo a WPF antes de la compilación pesada

                    CompilerResult result = compileService.Compile();
                    _logService.Write($"[TIA-PLC-SERVICE] [CompileBlockAsync] Resultado Compilación: {result.State} (Errores: {result.ErrorCount})");
                    return result.State != CompilerResultState.Error;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [CompileBlockAsync] Fallo al compilar: {ex.Message}", true);
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
                if (_currentPlc == null) return false;

                _statusService.Set("[TIA-PLC-SERVICE] Compilando todo el software del PLC...", StatusType.Warning);
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Compilando Software (ZC ALM TOOLS)..."))
                {
                    // Obtenemos el servicio de compilación directamente del grupo de bloques
                    var compileService = _currentPlc.BlockGroup.GetService<ICompilable>();
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
                _logService.Write($"[TIA-PLC-SERVICE] Error en compilación de software: {ex.Message}", true);
                return false;
            }
        }




        // ==================================================================================================================
        /// <summary>
        /// Sincroniza la lista de constantes de dispositivos desde el Excel
        /// </summary>
        public async Task<bool> SyncDispUserConstants(string tableName, List<IDevice> excelDevices)
        {
            try
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Sincronzando tabla de variables: {tableName}");

                // Buscamos la tabla de variables
                var table = FindTagTableByName(tableName);
                if (table == null) throw new Exception($"La tabla '{tableName}' no existe.");

                if (_currentProject == null) throw new Exception("No hay un proyecto activo asignado.");

                var validExcelDevices = excelDevices.Where(d => d.Estado != "Eliminar").ToList();
                var excelDict = validExcelDevices.ToDictionary(d => d.Numero.ToString());

                _statusService.Set("[TIA-PLC-SERVICE] [SyncDispUserConstants] Leyendo constantes actuales en el PLC...", StatusType.Ok);
                var existingConstants = table.UserConstants.ToList();

                var toDelete = new List<PlcUserConstant>();
                var toRename = new List<PlcUserConstant>();

                foreach (var c in existingConstants)
                {
                    if (!excelDict.ContainsKey(c.Value)) toDelete.Add(c);
                    else if (c.Name != excelDict[c.Value].CPTag) toRename.Add(c);
                }

                // Cedemos hilo a WPF antes de bloquear con la transacción
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Sincronizando constantes (ZC ALM TOOLS)..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Sincronizar Constantes {tableName}"))
                    {
                        if (toDelete.Any())
                        {
                            _statusService.Set($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Borrando {toDelete.Count} variables obsoletas en TIA Portal...", StatusType.Warning);
                            await Task.Delay(25); // Pequeño respiro visual
                            foreach (var c in toDelete)
                            {
                                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Borrando ID {c.Value}: {c.Name}");
                                c.Delete();
                            }
                        }

                        if (toRename.Any())
                        {
                            _statusService.Set($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Renombrando {toRename.Count} variables en TIA Portal...", StatusType.Warning);
                            await Task.Delay(25); // Pequeño respiro visual
                            foreach (var c in toRename)
                            {
                                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] ID {c.Value}: {c.Name} -> {excelDict[c.Value].CPTag}");
                                c.Name = excelDict[c.Value].CPTag;
                            }
                        }

                        string xmlPath = Path.Combine(AppConfigService.TempExportPathXml, $"{tableName}.xml");
                        if (File.Exists(xmlPath)) File.Delete(xmlPath);

                        _statusService.Set("[TIA-PLC-SERVICE] [SyncDispUserConstants] Exportando tabla para añadir nuevas variables...", StatusType.Ok);
                        await Task.Delay(25);
                        table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);

                        _statusService.Set("[TIA-PLC-SERVICE] [SyncDispUserConstants] Añadiendo variables y comentarios en el XML...", StatusType.Ok);
                        var tagEditor = new XmlTagTableEditorService(xmlPath);
                        tagEditor.ClearConstants();
                        foreach (var dev in validExcelDevices)
                        {
                            tagEditor.AddConstant(dev.CPTag, dev.Numero, dev.CPComentario);
                        }
                        tagEditor.Save();

                        _logService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Re-importando tabla '{tableName}' en TIA Portal (Override)...");
                        _statusService.Set("[TIA-PLC-SERVICE] [SyncDispUserConstants] Importando tabla modificada a TIA Portal...", StatusType.Ok);
                        await Task.Delay(25);

                        var parent = table.Parent;
                        if (parent is PlcTagTableUserGroup folder)
                            folder.TagTables.Import(new FileInfo(xmlPath), ImportOptions.Override);
                        else if (parent is PlcTagTableGroup root)
                            root.TagTables.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        PlcTagTable newTable = (parent is PlcTagTableUserGroup f) ? f.TagTables.Find(tableName) : ((PlcTagTableGroup)parent).TagTables.Find(tableName);
                        var cachedItem = _tagTableCache.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                        if (cachedItem != null && newTable != null) cachedItem.Table = newTable;

                        transaction.CommitOnDispose();
                    }
                }

                _statusService.Set("[TIA-PLC-SERVICE] [SyncDispUserConstants] Sincronización de constantes finalizada.", StatusType.Ok);
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Error en actualizacion de constantes: {ex.Message}", true);
                _statusService.Set("[TIA-PLC-SERVICE] [SyncDispUserConstants] Error en la sincronización.", StatusType.Error);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Añade comentarios en el DB de dispositivos mediante manipulación de XML
        /// </summary>
        public async Task<bool> SyncDispDbComments(string dbName, string arrayName, List<IDevice> devices)
        {
            try
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Buscando bloque '{dbName}' para añadir comentarios...");
                var genericBlock = FindBlockByName(dbName);
                var db = genericBlock as GlobalDB;

                if (db == null)
                {
                    _logService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] ERROR: No se pudo encontrar el bloque '{dbName}'.", true);
                    return false;
                }

                string xmlPath = Path.Combine(AppConfigService.TempExportPathXml, $"{dbName}.xml");
                if (File.Exists(xmlPath)) File.Delete(xmlPath);

                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Exportando bloque para edición: {xmlPath}");

                // Cedemos a WPF antes de bloquear exportando
                await Task.Delay(25);
                db.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);

                var dbEditor = new XmlDataBlockEditorService(xmlPath);
                bool isModified = false;

                foreach (var dev in devices)
                {
                    string comment = $"{dev.Tag} - {dev.Descripcion}";
                    if (dbEditor.SetComment(arrayName, dev.Numero, comment, false))
                    {
                        isModified = true;
                    }
                }

                if (!isModified)
                {
                    _logService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] No había textos que actualizar en {dbName}.");
                    return true;
                }
                dbEditor.Save();

                // Cedemos a WPF antes del bloqueo de la transacción de importación
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Sincronizando Comentarios de DB (ZC ALM TOOLS)..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Sincronizar DB {dbName}"))
                    {
                        _logService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Re-importando bloque '{dbName}' en TIA Portal...");
                        var parent = genericBlock.Parent;

                        if (parent is PlcBlockUserGroup folder)
                            folder.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                        else if (parent is PlcBlockGroup root)
                            root.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(dbName) : ((PlcBlockGroup)parent).Blocks.Find(dbName);
                        var cachedItem = _plcCache.FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                        if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                        transaction.CommitOnDispose();
                    }
                }

                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Bloque {dbName} actualizado correctamente.");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Error en modificacion de DB: {ex.Message}", true);
                return false;
            }
        }




        // ==================================================================================================================
        /// <summary>
        /// Añade los textos en los Arrays principales y de Visibilidad de un DB de Parámetros/Alarmas
        /// </summary>
        public async Task<bool> SyncParamsAlarmsDbCommentsAsync<T>(string dbName, string arrayName, IEnumerable<T> items, Func<T, int> getId, Func<T, string> getComment, bool hasVisArray = false)
        {
            try
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Buscando bloque '{dbName}' para añadir comentarios...");

                var block = FindBlockByName(dbName);
                if (block == null) throw new Exception($"Bloque '{dbName}' no encontrado.");

                string tempPath = Path.Combine(AppConfigService.TempExportPathXml, $"{dbName}.xml");
                if (File.Exists(tempPath)) File.Delete(tempPath);

                _logService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Exportando DB a XML temporal...");

                // Cedemos hilo a WPF
                await Task.Delay(25);
                block.Export(new FileInfo(tempPath), ExportOptions.WithDefaults);

                var dbEditor = new XmlDataBlockEditorService(tempPath);
                bool isModified = false;

                foreach (var item in items)
                {
                    int id = getId(item);
                    string expectedComment = getComment(item) ?? "";

                    if (dbEditor.SetComment(arrayName, id, expectedComment, hasVisArray))
                    {
                        isModified = true;
                    }
                }

                if (isModified)
                {
                    dbEditor.Save();
                    _logService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Re-importando bloque '{dbName}' en TIA Portal (Override)...");

                    // Cedemos hilo antes de la transaccion
                    await Task.Delay(25);

                    using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Sincronizando Comentarios de DB (ZC ALM TOOLS)..."))
                    {
                        using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Sincronizar DB {dbName}"))
                        {
                            var parent = block.Parent;

                            if (parent is PlcBlockUserGroup folder)
                                folder.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);
                            else if (parent is PlcBlockGroup root)
                                root.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);

                            PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(dbName) : ((PlcBlockGroup)parent).Blocks.Find(dbName);
                            var cachedItem = _plcCache.FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                            if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                            transaction.CommitOnDispose();
                        }
                    }
                    _logService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Bloque {dbName} actualizado correctamente.");
                    return true;
                }

                _logService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] No había textos que actualizar en {dbName}.");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Error en la modificacion de DB: {ex.Message}", true);
                return false;
            }
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



    }


}