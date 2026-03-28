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
using Siemens.Engineering.Library;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
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



        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public TiaPlcService()
        {
        }








        // ==================================================================================================================
        // GESTIÓN DE TIA PORTAL Y LIBRERÍAS
        // ==================================================================================================================

       

        /// <summary>
        /// Asigna la instancia principal de TIA Portal al servicio. 
        /// (Debe llamarse desde el AddIn.cs o donde inicialices la conexión principal)
        /// </summary>
        public void SetTiaPortalInstance(Siemens.Engineering.TiaPortal tiaApp, Project project)
        {
            _tiaApp = tiaApp;
            _currentProject = project;
        }

        /// <summary>
        /// Busca una librería global por nombre y, si no está abierta, la abre desde la ruta especificada.
        /// </summary>
        public GlobalLibrary GetOrOpenGlobalLibrary(string libraryPath)
        {
            if (_tiaApp == null)
            {
                LogService.Write("[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] ERROR: Instancia de TIA Portal no asignada al servicio.", true);
                return null;
            }

            if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath))
            {
                LogService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] La ruta es inválida o el archivo no existe: {libraryPath}", true);
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
                    LogService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] Librería global '{libraryName}' ya se encuentra abierta.");
                    return openedLibrary;
                }

                // 2. Si no está abierta, pedir a Openness que la abra
                StatusService.Set($"Abriendo librería global '{libraryName}'...", StatusType.Warning);

                // OpenMode.ReadOnly es crucial para evitar bloqueos si la librería está en uso por otro proceso
                var newOpenedLibrary = _tiaApp.GlobalLibraries.Open(libFile, OpenMode.ReadOnly);

                LogService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] Librería '{libraryName}' abierta correctamente.");
                return newOpenedLibrary;
            }
            catch (EngineeringSecurityException)
            {
                // Se relanza para que el ViewModel lo capture y avise al usuario en la UI
                throw;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [GetOrOpenGlobalLibrary] Excepción al abrir la librería: {ex.Message}", true);
                return null;
            }
        }



        // ==================================================================================================================
        // METODOS GENERALES
        // ==================================================================================================================

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
                LogService.Write("[TIA-PLC-SERVICE] [UpdatePlc] PLC modificado. Caché invalidada.");
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

                LogService.Write("[TIA-PLC-SERVICE] [BuildBlockCache] Indexando todos los bloques del PLC en memoria...");

                PopulateCacheRecursively(_currentPlc.BlockGroup, "Root");
                PopulateTagTableCacheRecursively(_currentPlc.TagTableGroup, "Variables de PLC");
                PopulateTypeCacheRecursively(_currentPlc.TypeGroup, "Tipos de datos PLC");

                _isCacheBuilt = true;
                LogService.Write($"[TIA-PLC-SERVICE] [BuildBlockCache] Indexación completa: {_plcCache.Count} bloques guardados en caché.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [BuildBlockCache] Error construyendo la caché: {ex.Message}", true);
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
                LogService.Write($"[TIA-PLC-SERVICE] [DumpCache] Caché exportada exitosamente a: {filePath}");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [DumpCache] Error exportando la caché: {ex.Message}", true);
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
        /// Exportar un bloque a XML
        /// </summary>
        public bool ExportBlockToXml(string blockName, string destinationPath)
        {
            try
            {
                var block = FindBlockByName(blockName);
                if (block == null)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [ExportBlockToXml] No se encontró el bloque '{blockName}'.", true);
                    return false;
                }

                // Borramos el archivo si existe
                if (File.Exists(destinationPath)) File.Delete(destinationPath);

                // Exportar el bloque
                block.Export(new FileInfo(destinationPath), ExportOptions.WithDefaults);

                LogService.Write($"[TIA-PLC-SERVICE] [ExportBlockToXml] Bloque '{blockName}' exportado correctamente a {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [ExportBlockToXml] Error exportando bloque '{blockName}': {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Exporta un bloque o UDT como archivo fuente de texto plano (.scl, .awl, .db, .udt)
        /// </summary>
        public bool ExportAsSource(object item, string destinationPath)
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
                LogService.Write($"[TIA-PLC-SERVICE] [ExportAsSource] Error exportando fuente a {destinationPath}: {ex.Message}", true);
                return false;
            }
        }












        // ==================================================================================================================
        /// <summary>
        /// Proceso maestro para actualizar y reimportar masivamente dependencias SCL
        /// </summary>
        public async Task<bool> UpdateMassiveSclDependencies(List<CachedPlcBlock> blocksToProcess)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Iniciando proceso para {blocksToProcess.Count} bloques.");

                string tempDir = AppConfigService.TempExportPathVci;
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                // Exportaremos los bloques finales aquí para dárselos a tu herramienta Python de documentación
                string pythonDocsDir = AppConfigService.GetGlobalSettings().DocExportSourcesPath;
                if (string.IsNullOrWhiteSpace(pythonDocsDir))
                {
                    pythonDocsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ZC_Exportaciones_SCL");
                }

                StringBuilder massiveSclFile = new StringBuilder();

                int counter = 1;
                foreach (var cachedBlock in blocksToProcess)
                {
                    StatusService.Set($"Analizando e inyectando {cachedBlock.Name} ({counter}/{blocksToProcess.Count})...", StatusType.Ok);
                    await Task.Delay(10);

                    string xmlTempPath = Path.Combine(tempDir, $"{cachedBlock.Name}.xml");
                    string sclTempPath = Path.Combine(pythonDocsDir, $"{cachedBlock.Name}.scl");

                    // 1. Exportar a XML para leer dependencias "seguras" (Con Auto-Compilación)
                    if (File.Exists(xmlTempPath)) File.Delete(xmlTempPath);

                    try
                    {
                        cachedBlock.Block.Export(new FileInfo(xmlTempPath), ExportOptions.WithDefaults);
                    }
                    catch (Exception ex) when (ex.Message.Contains("Inconsistent"))
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] El bloque {cachedBlock.Name} es inconsistente. Auto-compilando...");
                        StatusService.Set($"El bloque {cachedBlock.Name} requiere compilación previa...", StatusType.Warning);

                        // Usamos el método CompileBlock que ya tienes en tu clase
                        if (CompileBlock(cachedBlock.Name))
                        {
                            // Si compila bien, lo volvemos a intentar
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

                    // Aquí NO va overwrite. GenerateSource solo escupe el archivo. (Por eso lo borramos antes arriba)
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

                StatusService.Set($"Importando masivamente a TIA Portal. Por favor, espera...", StatusType.Warning);
                LogService.Write("[TIA-PLC-SERVICE] Importando archivo consolidado gigante a TIA Portal...");
                await Task.Delay(50);

                // 5. IMPORTACIÓN MASIVA a TIA PORTAL
                string massiveImportPath = Path.Combine(tempDir, "MassiveImport.scl");


                var finalUtf8Bom = new System.Text.UTF8Encoding(true);
                File.WriteAllText(massiveImportPath, massiveSclFile.ToString(), finalUtf8Bom);


                // Solicitamos acceso exclusivo a TIA Portal para evitar que el usuario interfiera durante la sincronización                
                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Actualizando masivamente dependencias SCL (ZC ALM TOOLS)..."))
                {
                    // Iniciamos la transaccion
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Iniciando actualizacion para {blocksToProcess.Count} bloques."))
                    {

                        var extSources = _currentPlc.ExternalSourceGroup.ExternalSources;
                        PlcExternalSource source = extSources.CreateFromFile("UpdateMasivo_Temp", massiveImportPath);

                        // AQUÍ SÍ VA EL OVERWRITE. Al importar a TIA Portal, machacamos los bloques existentes.
                        source.GenerateBlocksFromSource(GenerateBlockOption.None);

                        // Limpieza en TIA Portal
                        source.Delete();

                        transaction.CommitOnDispose();
                    }
                }
                LogService.Write("[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Proceso completado con éxito.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [UpdateMassiveSclDependencies] Error crítico: {ex.Message}", true);
                return false;
            }
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
                LogService.Write($"[TIA-PLC-SERVICE] Error extrayendo XML: {ex.Message}", true);
                return "--";
            }
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
        /// Exportar tabla de variables de dispositivos a XML
        /// </summary>
        public bool ExportDispTagTable(string tableName, string xmlPath)
        {
            try
            {
                // Borramos el archivo si existe
                if (File.Exists(xmlPath)) File.Delete(xmlPath);

                var table = FindTagTableByName(tableName);
                if (table == null) return false;

                // Exportamos la tabla de variables
                table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                return true;
            }
            catch { return false; }
        }



        // ==================================================================================================================
        /// <summary>
        /// Sincroniza el valor de una constante global de dimensionado
        /// </summary>
        public bool SyncGlobalConstant(string tableName, string constantName, int newValue)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstant] Verificando constante: {constantName}...");
                var table = FindTagTableByName(tableName);
                if (table == null) throw new Exception($"No se encontró la tabla '{tableName}'");

                var constant = table.UserConstants.Find(constantName);
                if (constant == null) throw new Exception($"No existe la constante '{constantName}'");

                if (int.TryParse(constant.Value, out int currentValue))
                {
                    if (currentValue != newValue)
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstant] Modificando {constantName}: {currentValue} -> {newValue}");
                        StatusService.Set($"{constantName} actualizado a {newValue}.", StatusType.Ok);

                        // Solicitamos acceso exclusivo a TIA Portal para evitar que el usuario interfiera durante la sincronización                
                        using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Actualizando constante..."))
                        {
                            // Iniciamos la transaccion
                            using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Cambiar constante {constantName}"))
                            {                                
                                constant.Value = newValue.ToString();
                                transaction.CommitOnDispose();
                            }
                        }
                        return true;
                    }
                    else
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstant] {constantName} ya tenía el valor correcto ({newValue}).");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstant] Fallo en Sync Global: {ex.Message}", true);
                return false;
            }
        }




        // ==================================================================================================================
        /// <summary>
        /// Compila un bloque específico
        /// </summary>
        public bool CompileBlock(string blockName)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] Buscando bloque '{blockName}' para compilar...");
                
                // Buscamos el bloque a compilar
                var block = FindBlockByName(blockName);

                if (block == null)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] No se encontró el bloque '{blockName}'", true);
                    return false;
                }

                // Compilamos el bloque
                ICompilable compileService = block.GetService<ICompilable>();
                if (compileService != null)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] Compilando: {blockName}...");
                    CompilerResult result = compileService.Compile();
                    LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] Resultado Compilación: {result.State} (Errores: {result.ErrorCount})");
                    return result.State != CompilerResultState.Error;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] Fallo al compilar: {ex.Message}", true);
                return false;
            }
        }




        // ==================================================================================================================
        // METODOS DE SINCRONIZACIÓN
        // ==================================================================================================================

        // ==================================================================================================================
        /// <summary>
        /// Sincroniza la lista de constantes de dispositivos desde el Excel
        /// </summary>
        public async Task<bool> SyncDispUserConstants(string tableName, List<IDevice> excelDevices)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants]  === SINCRONIZANDO IDs: {tableName} ===");

                // Buscamos la tabla de variables
                var table = FindTagTableByName(tableName);
                if (table == null) throw new Exception($"La tabla '{tableName}' no existe.");

                // Aseguramos tener el proyecto actual
                if (_currentProject == null) throw new Exception("No hay un proyecto activo asignado.");

                // Solicitamos acceso exclusivo a TIA Portal para evitar que el usuario interfiera durante la sincronización                
                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Sincronizando constantes (ZC ALM TOOLS)..."))
                {
                    // Iniciamos la transaccion
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject,$"Sincronizar Constantes {tableName}"))
                    {
                        var validExcelDevices = excelDevices.Where(d => d.Estado != "Eliminar").ToList();
                        var excelDict = validExcelDevices.ToDictionary(d => d.Numero.ToString());

                        StatusService.Set("Leyendo constantes actuales en el PLC...", StatusType.Ok);
                        var existingConstants = table.UserConstants.ToList();

                        // Actualizamos via OPENESS las variables para mantener las referencias cruzadas                
                        var toDelete = new List<PlcUserConstant>();
                        var toRename = new List<PlcUserConstant>();

                        // Creamos las listas con los datos a borrar y a modificar
                        foreach (var c in existingConstants)
                        {
                            if (!excelDict.ContainsKey(c.Value))
                            {
                                // Si no está en el excel, a la lista negra
                                toDelete.Add(c);
                            }
                            else if (c.Name != excelDict[c.Value].CPTag)
                            {
                                // Si está en el excel pero con otro nombre, a la lista de renombrar
                                toRename.Add(c);
                            }
                        }

                        // Primero borramos las variables
                        if (toDelete.Any())
                        {
                            StatusService.Set($"Borrando {toDelete.Count} variables obsoletas en TIA Portal...", StatusType.Warning);
                            foreach (var c in toDelete)
                            {
                                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Borrando ID {c.Value}: {c.Name}");
                                c.Delete();
                            }
                        }

                        // Actualizamos los nombres
                        if (toRename.Any())
                        {
                            StatusService.Set($"Renombrando {toRename.Count} variables en TIA Portal...", StatusType.Warning);
                            foreach (var c in toRename)
                            {
                                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] ID {c.Value}: {c.Name} -> {excelDict[c.Value].CPTag}");
                                c.Name = excelDict[c.Value].CPTag;
                            }
                        }

                        // Añadimos las nuevas variables directamente en el XML
                        string xmlPath = Path.Combine(AppConfigService.TempExportPathXml, $"{tableName}.xml");
                        if (File.Exists(xmlPath)) File.Delete(xmlPath);

                        // Exportamos la tabla de variables
                        StatusService.Set("Exportando tabla para añadir nuevas variables...", StatusType.Ok);

                        // Nota sobre await Task.Delay: Cuidado al mezclar llamadas asíncronas con transacciones en Openness.
                        // Openness es muy estricto con el hilo de ejecución (UI Thread). Si usas Task.Delay, 
                        // asegúrate de que no estás perdiendo el contexto del hilo COM subyacente.
                        table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                        // await Task.Delay(50); // Te recomiendo probar si puedes quitar este Delay estando dentro de la transacción.

                        StatusService.Set("Añadiendo variables y comentarios en el XML...", StatusType.Ok);

                        // Añadimos las variables nuevas en el xml
                        var tagEditor = new XmlTagTableEditorService(xmlPath);
                        tagEditor.ClearConstants();
                        foreach (var dev in validExcelDevices)
                        {
                            tagEditor.AddConstant(dev.CPTag, dev.Numero, dev.CPComentario);
                        }
                        tagEditor.Save();

                        // Importacion de xml modificado
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Re-importando tabla '{tableName}' en TIA Portal (Override)...");
                        StatusService.Set("Importando tabla modificada a TIA Portal...", StatusType.Ok);

                        var parent = table.Parent;
                        if (parent is PlcTagTableUserGroup folder)
                            folder.TagTables.Import(new FileInfo(xmlPath), ImportOptions.Override);
                        else if (parent is PlcTagTableGroup root)
                            root.TagTables.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        // Actualizar caché para que la aplicación vea la tabla nueva
                        PlcTagTable newTable = (parent is PlcTagTableUserGroup f) ? f.TagTables.Find(tableName) : ((PlcTagTableGroup)parent).TagTables.Find(tableName);
                        var cachedItem = _tagTableCache.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                        if (cachedItem != null && newTable != null) cachedItem.Table = newTable;

                        // 3. ¡VITAL! CONFIRMAR LA TRANSACCIÓN
                        // Si llegamos hasta aquí sin excepciones, le decimos a TIA Portal: "Aplica los cambios".
                        transaction.CommitOnDispose();

                    } // <-- Aquí se cierra la transacción (se hace Commit si llamaste a CommitOnDispose, o Rollback si no).
                } // <-- Aquí se libera el ExclusiveAccess y TIA Portal vuelve a ser clickeable por el usuario.

                StatusService.Set("Sincronización de constantes finalizada.", StatusType.Ok);
                return true;

            }
            catch (Exception ex)
            {
                // Si ocurre cualquier error, salta aquí.
                // Al salir del bloque 'using (transaction)' sin haber llamado a CommitOnDispose(),
                // TIA Portal hace un ROLLBACK automático de las variables borradas, renombradas y la tabla importada.
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Error en actualizacion de constantes: {ex.Message}", true);
                StatusService.Set("Error en la sincronización.", StatusType.Error);
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
                // Localizamos el DB a modificar
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Buscando bloque '{dbName}' para añadir comentarios...");
                var genericBlock = FindBlockByName(dbName);
                var db = genericBlock as GlobalDB;

                if (db == null)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] ERROR: No se pudo encontrar el bloque '{dbName}'.", true);
                    return false;
                }
                                              

                // Exportar a temporal
                string xmlPath = Path.Combine(AppConfigService.TempExportPathXml, $"{dbName}.xml");
                if (File.Exists(xmlPath)) File.Delete(xmlPath);

                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Exportando bloque para edición: {xmlPath}");
                db.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                await Task.Delay(50);

                // Llamamos a XmlDataBlockEditorService para edicion de DB
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
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] No había textos que actualizar en {dbName}.");
                    return true;
                }
                dbEditor.Save();

                // Solicitamos acceso exclusivo a TIA Portal para evitar que el usuario interfiera durante la sincronización                
                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Sincronizando Comentarios de DB (ZC ALM TOOLS)..."))
                {
                    // Iniciamos la transaccion
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Sincronizar DB {dbName}"))
                    {
                        // Re-importar el bloque a TIA Portal
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Re-importando bloque '{dbName}' en TIA Portal...");
                        var parent = genericBlock.Parent;

                        if (parent is PlcBlockUserGroup folder)
                            folder.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                        else if (parent is PlcBlockGroup root)
                            root.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        // Actualizar caché
                        PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(dbName) : ((PlcBlockGroup)parent).Blocks.Find(dbName);
                        var cachedItem = _plcCache.FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                        if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                        transaction.CommitOnDispose();
                    }
                }

                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Bloque {dbName} actualizado correctamente.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispDbComments] Error en modificacion de DB: {ex.Message}", true);
                return false;
            }
        }




        // ==================================================================================================================
        /// <summary>
        /// Añade los textos en los Arrays principales y de Visibilidad de un DB de Parámetros/Alarmas
        /// </summary>
        public bool SyncParamsAlarmsDbComments<T>(string dbName, string arrayName, IEnumerable<T> items, Func<T, int> getId, Func<T, string> getComment, bool hasVisArray = false)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Buscando bloque '{dbName}' para añadir comentarios...");

                // Buscamos el bloque
                var block = FindBlockByName(dbName);
                if (block == null) throw new Exception($"Bloque '{dbName}' no encontrado.");

                // Exportar a temporal
                string tempPath = Path.Combine(AppConfigService.TempExportPathXml, $"{dbName}.xml");
                if (File.Exists(tempPath)) File.Delete(tempPath);

                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Exportando DB a XML temporal...");
                block.Export(new FileInfo(tempPath), ExportOptions.WithDefaults);

                // Llamamos a XmlDataBlockEditorService para edicion de DB
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
                    // --------------------------------------------------

                    LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Re-importando bloque '{dbName}' en TIA Portal (Override)...");

                    // Solicitamos acceso exclusivo a TIA Portal para evitar que el usuario interfiera durante la sincronización                
                    using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Sincronizando Comentarios de DB (ZC ALM TOOLS)..."))
                    {
                        // Iniciamos la transaccion
                        using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Sincronizar DB {dbName}"))
                        {

                            var parent = block.Parent;

                            if (parent is PlcBlockUserGroup folder)
                                folder.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);
                            else if (parent is PlcBlockGroup root)
                                root.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);

                            // Actualizar caché
                            PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(dbName) : ((PlcBlockGroup)parent).Blocks.Find(dbName);
                            var cachedItem = _plcCache.FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                            if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                            transaction.CommitOnDispose();
                        }
                    }
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Bloque {dbName} actualizado correctamente.");
                    return true;
                }

                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] No había textos que actualizar en {dbName}.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Error en la modificacion de DB: {ex.Message}", true);
                return false;
            }

        }





    }


}