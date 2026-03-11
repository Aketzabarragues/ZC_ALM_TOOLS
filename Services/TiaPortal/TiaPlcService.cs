using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
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



        // Diccionarios de caché en RAM
        private List<CachedPlcBlock> _plcCache;
        private List<CachedPlcTagTable> _tagTableCache;
        private bool _isCacheBuilt = false;



        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public TiaPlcService()
        {
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

                LogService.Write("[TIA-PLC-SERVICE] [BuildBlockCache] Indexando todos los bloques del PLC en memoria...");

                PopulateCacheRecursively(_currentPlc.BlockGroup, "Root");
                PopulateTagTableCacheRecursively(_currentPlc.TagTableGroup, "Variables de PLC");

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
                    FolderPath = currentPath
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
                    writer.WriteLine($"Total Bloques: {_plcCache.Count}");
                    writer.WriteLine("=========================================================\n");

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
                    LogService.Write($"[TIA-SERVICE] [ExportBlockToXml] No se encontró el bloque '{blockName}'.", true);
                    return false;
                }

                // Borramos el archivo si existe
                if (File.Exists(destinationPath)) File.Delete(destinationPath);

                // Exportar el bloque
                block.Export(new FileInfo(destinationPath), ExportOptions.WithDefaults);

                LogService.Write($"[TIA-SERVICE] [ExportBlockToXml] Bloque '{blockName}' exportado correctamente a {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-SERVICE] [ExportBlockToXml] Error exportando bloque '{blockName}': {ex.Message}", true);
                return false;
            }
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
                        constant.Value = newValue.ToString();
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

                // Buscamos la tabla de variabkles
                var table = FindTagTableByName(tableName);
                if (table == null) throw new Exception($"La tabla '{tableName}' no existe.");

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
                string xmlPath = Path.Combine(AppConfigService.TempPath, $"{tableName}.xml");
                if (File.Exists(xmlPath)) File.Delete(xmlPath);

                // Exportamos la tabla de variables
                StatusService.Set("Exportando tabla para añadir nuevas variables...", StatusType.Ok);
                table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                await Task.Delay(50);

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

                StatusService.Set("Sincronización de constantes finalizada.", StatusType.Ok);
                return true;

            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDispUserConstants] Error en actualizacion de constantes: {ex.Message}", true);
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
                string xmlPath = Path.Combine(AppConfigService.TempPath, $"{dbName}.xml");
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
                string tempPath = Path.Combine(Path.GetTempPath(), $"{dbName}.xml");
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
                    var parent = block.Parent;

                    if (parent is PlcBlockUserGroup folder)
                        folder.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);
                    else if (parent is PlcBlockGroup root)
                        root.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);

                    // Actualizar caché
                    PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(dbName) : ((PlcBlockGroup)parent).Blocks.Find(dbName);
                    var cachedItem = _plcCache.FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                    if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

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