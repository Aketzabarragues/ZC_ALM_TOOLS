using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{

    /// <summary>
    /// Servicio dedicado a la sincronización de datos entre el proyecto de TIA Portal y las fuentes externas (como Excel), 
    /// proporcionando métodos para leer y actualizar constantes globales, así como para gestionar comentarios en bloques y tablas de variables de manera segura y eficiente.
    /// </summary>
    public class TiaPlcGeneratorService
    {

        private readonly Siemens.Engineering.TiaPortal _tiaApp;
        private readonly Project _currentProject;
        private readonly TiaPlcCacheService _cacheService;
        private readonly TiaPlcImportExportService _importExportService;

        private readonly IAppConfigService _appConfigService;
        private readonly ILogService _logService;
        private readonly IStatusService _statusService;

        public TiaPlcGeneratorService(
            Siemens.Engineering.TiaPortal tiaApp,
            Project project,
            TiaPlcCacheService cacheService, 
            TiaPlcImportExportService importExportService,
            IAppConfigService appConfigService,
            ILogService logService,
            IStatusService statusService)
        {

            _tiaApp = tiaApp;
            _currentProject = project;
            _cacheService = cacheService;
            _importExportService = importExportService;

            _appConfigService = appConfigService;
            _logService = logService;
            _statusService = statusService;

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
        /// Proceso maestro para actualizar y reimportar masivamente dependencias SCL
        /// </summary>
        public async Task<bool> UpdateMassiveSclDependencies(List<CachedPlcBlock> blocksToProcess)
        {
            try
            {
                _statusService.Set($"[TIA-PLC-GEN-SERVICE] [UpdateMassiveSclDependencies] Iniciando proceso para {blocksToProcess.Count} bloques.", StatusType.Ok);

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
                    _statusService.Set($"[TIA-PLC-GEN-SERVICE] [UpdateMassiveSclDependencies] Analizando e inyectando {cachedBlock.Name} ({counter}/{blocksToProcess.Count})...", StatusType.Ok);

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
                        _statusService.Set($"[TIA-PLC-GEN-SERVICE] [UpdateMassiveSclDependencies] El bloque {cachedBlock.Name} requiere compilación previa...", StatusType.Warning);

                        // Usamos el método CompileBlockAsync (que ya tiene su propio Delay interno)
                        if (await _importExportService.CompileBlockAsync(cachedBlock.Name))
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

                    _cacheService.CurrentPlc.ExternalSourceGroup.GenerateSource(listForExport, new FileInfo(sclTempPath), GenerateOptions.None);

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

                _statusService.Set($"[TIA-PLC-GEN-SERVICE] [UpdateMassiveSclDependencies] Importando masivamente a TIA Portal. Por favor, espera...", StatusType.Warning);

                // 5. IMPORTACIÓN MASIVA a TIA PORTAL
                string massiveImportPath = Path.Combine(tempDir, "MassiveImport.scl");
                var finalUtf8Bom = new System.Text.UTF8Encoding(true);
                File.WriteAllText(massiveImportPath, massiveSclFile.ToString(), finalUtf8Bom);

                await Task.Delay(25); // Cedemos el hilo antes de la transacción gigante

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Actualizando masivamente dependencias SCL (ZC ALM TOOLS)..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Iniciando actualizacion para {blocksToProcess.Count} bloques."))
                    {
                        var extSources = _cacheService.CurrentPlc.ExternalSourceGroup.ExternalSources;
                        PlcExternalSource source = extSources.CreateFromFile("UpdateMasivo_Temp", massiveImportPath);

                        await Task.Delay(25); // Último respiro antes de la importación pesada
                        // AQUÍ SÍ VA EL OVERWRITE. Al importar a TIA Portal, machacamos los bloques existentes.
                        source.GenerateBlocksFromSource(GenerateBlockOption.None);

                        // Limpieza en TIA Portal
                        source.Delete();

                        transaction.CommitOnDispose();
                    }
                }

                _statusService.Set($"[TIA-PLC-GEN-SERVICE] [UpdateMassiveSclDependencies] Proceso completado con éxito.", StatusType.Ok);
                return true;
            }
            catch (Exception ex)
            {
                _statusService.Set($"[TIA-PLC-GEN-SERVICE] [UpdateMassiveSclDependencies] Error al importar el codigo. Revisa el log.", StatusType.Ok);
                _logService.Write($"[TIA-PLC-GEN-SERVICE] [UpdateMassiveSclDependencies] Error crítico: {ex.Message}", true);
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
                var fcCalls = _cacheService.GetAllBlocks()
                    .Where(b => b.SimpleType == "FC" && allNamesInXml.Contains(b.Name))
                    .Select(b => b.Name)
                    .OrderBy(name => name)
                    .ToList();

                var fbCalls = _cacheService.GetAllBlocks()
                    .Where(b => b.SimpleType == "FB" && allNamesInXml.Contains(b.Name))
                    .Select(b => b.Name)
                    .OrderBy(name => name)
                    .ToList();

                // 3. Cruzamos todos esos nombres con nuestra caché de DBs
                var dbCalls = _cacheService.GetAllBlocks()
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

                var interfaceTypes = _cacheService.GetAllTypes()
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
                _logService.Write($"[TIA-PLC-GEN-SERVICE] Error extrayendo XML: {ex.Message}", true);
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




    }
}
