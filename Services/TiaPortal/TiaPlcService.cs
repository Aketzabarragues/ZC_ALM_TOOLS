using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{

    

    // ==================================================================================================================
    // Servicio para comunicación directa con Siemens Openness
    public class TiaPlcService
    {
        private PlcSoftware _currentPlc;

        // Diccionarios de caché en RAM
        private List<CachedPlcBlock> _plcCache;
        private List<CachedPlcTagTable> _tagTableCache;
        private bool _isCacheBuilt = false;


        // ==================================================================================================================
        // Constructor
        public TiaPlcService()
        {

        }










        // ==================================================================================================================
        // Exportar el contenido de la caché a un archivo TXT para análisis
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
        // METODOS PARA DISPOSITIVOS
        // ==================================================================================================================



        // ==================================================================================================================
        // Sincroniza la lista de constantes de dispositivos desde el Excel
        public async Task<bool> SyncDispUserConstants(string tableName, List<IDevice> excelDevices)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants]  === SINCRONIZANDO IDs: {tableName} ===");
                var table = FindTagTableByName(tableName);
                if (table == null) throw new Exception($"La tabla '{tableName}' no existe.");

                // Eliminar las que sobran en TIA
                var excelIds = new HashSet<int>(excelDevices.Select(d => d.Numero));
                var constantsToDelete = table.UserConstants.Where(c => int.TryParse(c.Value, out int id) && !excelIds.Contains(id)).ToList();

                foreach (var c in constantsToDelete)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] Borrando ID {c.Value}: {c.Name}");
                    StatusService.Set($"Borrando ID {c.Value}: {c.Name}", StatusType.Ok);
                    c.Delete();
                    await Task.Delay(1);
                }

                // Crear o Renombrar según Excel
                foreach (var dev in excelDevices)
                {
                    var tiaConst = table.UserConstants.FirstOrDefault(c => c.Value == dev.Numero.ToString());

                    if (tiaConst == null)
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] Creando ID {dev.Numero}: {dev.CPTag}");
                        StatusService.Set($"Creando ID {dev.Numero}: {dev.CPTag}", StatusType.Ok);
                        tiaConst = table.UserConstants.Create(dev.CPTag, "Int", dev.Numero.ToString());
                    }

                    if (tiaConst.Name != dev.CPTag)
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] ID {dev.Numero}: {tiaConst.Name} -> {dev.CPTag}");
                        StatusService.Set($"ID {dev.Numero}: {tiaConst.Name} -> {dev.CPTag}", StatusType.Ok);
                        tiaConst.Name = dev.CPTag;
                    }

                    await Task.Delay(1);

                    UpdatePlcComment(tiaConst, dev.CPComentario);
                    
                }
                StatusService.Set("Sincronización de constantes finalizada.", StatusType.Ok);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] Error en Sync Constants: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        // Inyecta comentarios en el DB de dispositivos mediante manipulación de XML
        public async Task<bool> SyncDispDbComments(string dbName, string arrayName, List<IDevice> devices)
        {
            try
            {

                // 1. Localizar el bloque
                LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] Buscando bloque '{dbName}' para compilar...");
                var genericBlock = FindBlockByName(dbName);
                var db = genericBlock as GlobalDB;

                if (db == null)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] ERROR: No se pudo encontrar o castear el bloque '{dbName}'.", true);
                    return false;
                }

                // 2. Exportar a temporal
                string xmlPath = Path.Combine(AppConfigService.TempPath, $"{dbName}.xml");
                if (File.Exists(xmlPath)) File.Delete(xmlPath);

                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] Exportando bloque para edición: {xmlPath}");
                db.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);

                // 3. Cargar XML y buscar nodos
                XDocument doc = XDocument.Load(xmlPath);
                XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

                var staticSection = doc.Descendants(ns + "Section").FirstOrDefault(s => s.Attribute("Name")?.Value == "Static");
                if (staticSection == null)
                {
                    LogService.Write("[TIA-PLC-SERVICE] [SyncDbComments] ERROR: No se encontró la sección 'Static' en el XML del DB.", true);
                    return false;
                }

                var arrayMember = staticSection.Elements(ns + "Member").FirstOrDefault(m => m.Attribute("Name")?.Value == arrayName);
                if (arrayMember == null)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] ERROR: No se encontró el array '{arrayName}' dentro de la sección Static.", true);
                    return false;
                }

                // 4. Modificar comentarios
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] Actualizando comentarios para {devices.Count} dispositivos en el array '{arrayName}'...");
                int updatedCount = 0;

                foreach (var dev in devices)
                {
                    // Buscamos el subelemento por su índice (Path)
                    var subelement = arrayMember.Elements(ns + "Subelement").FirstOrDefault(s => s.Attribute("Path")?.Value == dev.Numero.ToString());

                    if (subelement == null)
                    {
                        // Si no existe el nodo de comentario para ese índice, lo creamos
                        subelement = new XElement(ns + "Subelement", new XAttribute("Path", dev.Numero.ToString()));
                        arrayMember.Add(subelement);
                    }

                    // Limpiar comentarios antiguos e inyectar el nuevo
                    subelement.Elements(ns + "Comment").Remove();
                    subelement.Add(new XElement(ns + "Comment",
                        new XElement(ns + "MultiLanguageText",
                            new XAttribute("Lang", "es-ES"),
                            $"{dev.Tag} - {dev.Descripcion}")));
                    updatedCount++;
                    await Task.Delay(1);
                }

                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] Modificación completada. Guardando archivo temporal...");
                doc.Save(xmlPath);

                // 5. Re-importar el bloque a TIA Portal
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] Re-importando bloque '{dbName}' en TIA Portal (Override)...");
                var parent = genericBlock.Parent;

                if (parent is PlcBlockUserGroup folder)
                    folder.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                else if (parent is PlcBlockGroup root)
                    root.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);


                PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(dbName) : ((PlcBlockGroup)parent).Blocks.Find(dbName);
                var cachedItem = _plcCache.FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] ¡ÉXITO! Bloque {dbName} actualizado correctamente.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] ERROR CRÍTICO en cirugía XML: {ex.Message}", true);
                if (ex.InnerException != null)
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] DETALLE: {ex.InnerException.Message}", true);

                return false;
            }
        }



        // ==================================================================================================================
        // Exportar tabla de variables de dispositivos
        public bool ExportDispTagTable(string tableName, string xmlPath)
        {
            try
            {
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
                var table = FindTagTableByName(tableName);
                if (table == null) return false;

                table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                return true;
            }
            catch { return false; }
        }



        // ==================================================================================================================
        // METODOS PARA PARAMETROS Y ALARMAS
        // ==================================================================================================================



        // ==================================================================================================================
        // Inyecta los textos en los Arrays principales y de Visibilidad de un DB de Parámetros
        // ==================================================================================================================
        // Método universal para inyectar textos en los DBs (Parámetros, Alarmas, etc.)
        public bool SyncParamsAlarmsDbComments<T>(string blockName, string arrayName, IEnumerable<T> items, Func<T, int> getId, Func<T, string> getComment, bool hasVisArray = false)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] === INYECTANDO TEXTOS: {blockName} ===");

                var block = FindBlockByName(blockName);
                if (block == null) throw new Exception($"Bloque '{blockName}' no encontrado.");

                string tempPath = Path.Combine(Path.GetTempPath(), $"{blockName}.xml");
                if (File.Exists(tempPath)) File.Delete(tempPath);

                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Exportando DB a XML temporal...");
                block.Export(new FileInfo(tempPath), ExportOptions.WithDefaults);

                XDocument doc = XDocument.Load(tempPath);
                XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

                var dataMember = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Member" && x.Attribute("Name")?.Value == arrayName);
                if (dataMember == null) throw new Exception($"No se encontró el array '{arrayName}' en el DB.");

                XElement visMember = null;
                if (hasVisArray)
                {
                    visMember = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Member" && x.Attribute("Name")?.Value == "Vis");
                    if (visMember == null) LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] ATENCIÓN: No se encontró el array 'Vis' en {blockName}.");
                }

                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Actualizando comentarios en {arrayName}{(hasVisArray ? " y Vis" : "")}...");
                bool isModified = false;

                foreach (var item in items)
                {
                    int id = getId(item);
                    string expectedComment = getComment(item) ?? "";

                    if (UpdateOrAddCommentNode(dataMember, id, expectedComment, ns)) isModified = true;

                    if (hasVisArray && visMember != null)
                    {
                        if (UpdateOrAddCommentNode(visMember, id, expectedComment, ns)) isModified = true;
                    }
                }

                if (isModified)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] XML modificado. Guardando e importando...");
                    doc.Save(tempPath);

                    var parent = block.Parent;
                    if (parent is PlcBlockUserGroup folder)
                        folder.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);
                    else if (parent is PlcBlockGroup root)
                        root.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);

                    PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(blockName) : ((PlcBlockGroup)parent).Blocks.Find(blockName);
                    var cachedItem = _plcCache.FirstOrDefault(b => b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
                    if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                    LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] ¡ÉXITO! Bloque {blockName} actualizado.");
                    return true;
                }
                else
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] No había textos que actualizar en {blockName}.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncParamsAlarmsDbComments] Fallo en inyección de XML en {blockName}: {ex.Message}", true);
                return false;
            }
        }


        // Método auxiliar privado para no repetir la lógica de inyección de nodos XML
        private bool UpdateOrAddCommentNode(XElement memberNode, int id, string text, XNamespace ns)
        {
            if (memberNode == null) return false;

            // Buscamos el subelemento con el índice exacto
            var subElement = memberNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Subelement" && x.Attribute("Path")?.Value == id.ToString());

            // Si TIA Portal no exportó el Subelement (porque está vacío), lo creamos nosotros
            if (subElement == null)
            {
                if (string.IsNullOrEmpty(text)) return false; // Si no hay texto y no existe, no hacemos nada

                subElement = new XElement(ns + "Subelement", new XAttribute("Path", id.ToString()));
                memberNode.Add(subElement);
            }

            var commentNode = subElement.Elements().FirstOrDefault(x => x.Name.LocalName == "Comment");

            // Si no existe la etiqueta <Comment>, se la creamos
            if (commentNode == null && !string.IsNullOrEmpty(text))
            {
                commentNode = new XElement(ns + "Comment");
                subElement.AddFirst(commentNode); // Lo ponemos al principio del Subelement
            }

            if (commentNode != null)
            {
                var multiLangNode = commentNode.Elements().FirstOrDefault(x => x.Name.LocalName == "MultiLanguageText" && x.Attribute("Lang")?.Value == "es-ES");

                if (multiLangNode != null)
                {
                    if (multiLangNode.Value != text)
                    {
                        multiLangNode.Value = text;
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    commentNode.Add(new XElement(ns + "MultiLanguageText", new XAttribute("Lang", "es-ES"), text));
                    return true;
                }
            }

            return false;
        }



        // ==================================================================================================================
        // METODOS GENERALES
        // ==================================================================================================================



        // ==================================================================================================================
        // Asignacion de PLC seleccionado
        public void UpdatePlc(PlcSoftware plcSoftware)
        {
            if (_currentPlc != plcSoftware)
            {
                _currentPlc = plcSoftware;

                // Si cambiamos de PLC, destruimos la caché antigua
                _isCacheBuilt = false;
                _plcCache?.Clear();
                _tagTableCache?.Clear();
                LogService.Write("[TIA-PLC-SERVICE] PLC modificado. Caché invalidada.");
            }
        }



        // ==================================================================================================================
        // Construye el índice completo del PLC en memoria RAM
        public void BuildBlockCache()
        {
            try
            {
                if (_currentPlc == null) return;

                _plcCache = new List<CachedPlcBlock>();
                _tagTableCache = new List<CachedPlcTagTable>();

                LogService.Write("[TIA-PLC-SERVICE] Indexando todos los bloques del PLC en memoria...");

                PopulateCacheRecursively(_currentPlc.BlockGroup, "Root");
                PopulateTagTableCacheRecursively(_currentPlc.TagTableGroup, "Variables de PLC");

                _isCacheBuilt = true;
                LogService.Write($"[TIA-PLC-SERVICE] Indexación completa: {_plcCache.Count} bloques guardados en caché.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] ERROR CRÍTICO construyendo la caché: {ex.Message}", true);
            }
            
        }


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
        // Lee el valor de una constante global (ej. N_MAX)
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
        // Exportar un bloque (DB, FC, FB) a XML
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

                // Asegurarnos de que no haya un archivo viejo "molestando"
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                // Exportar el bloque usando la API nativa de Openness
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
        // Sincroniza el valor de una constante global de dimensionado
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
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstant] Fallo en Sync Global: {ex.Message}", true);
                return false;
            }
        }




        // ==================================================================================================================
        // Compila un bloque específico (necesario antes de la cirugía XML)
        public bool CompileBlock(string blockName)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] Buscando bloque '{blockName}' para compilar...");
                var block = FindBlockByName(blockName);

                if (block == null)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [CompileBlock] No se encontró el bloque '{blockName}'", true);
                    return false;
                }

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
        // Buscar tabla de variables
        public PlcTagTable FindTagTableByName(string tableName)
        {
            if (_currentPlc == null) return null;
            if (!_isCacheBuilt) BuildBlockCache();

            return _tagTableCache?.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))?.Table;
        }




        // ==================================================================================================================
        // Actualizar comentario de PLC
        private void UpdatePlcComment(PlcUserConstant constant, string comment)
        {
            foreach (var item in constant.Comment.Items)
            {
                try { item.Text = comment; }
                catch { item.SetAttribute("Text", comment); }
            }
        }




        // ==================================================================================================================
        // Metodo publico para buscar bloque por nombre
        public PlcBlock FindBlockByName(string blockName)
        {
            if (_currentPlc == null) return null;
            if (!_isCacheBuilt) BuildBlockCache();

            return _plcCache?.FirstOrDefault(b => b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase))?.Block;
        }




        // ==================================================================================================================
        // Metodo publico para buscar bloque por numero
        public PlcBlock FindBlockByNumber(int number, string blockType)
        {
            if (_currentPlc == null) return null;
            if (!_isCacheBuilt) BuildBlockCache();

            return _plcCache?.FirstOrDefault(b => b.Number == number && b.SimpleType.Equals(blockType, StringComparison.OrdinalIgnoreCase))?.Block;
        }


    }
}