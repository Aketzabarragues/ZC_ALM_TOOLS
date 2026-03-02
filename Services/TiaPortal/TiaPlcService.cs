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
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.Core
{

    

    // ==================================================================================================================
    // Servicio para comunicación directa con Siemens Openness
    public class TiaPlcService
    {
        private PlcSoftware _currentPlc;




        // ==================================================================================================================
        // Constructor
        public TiaPlcService()
        {

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
                var table = FindTagTableRecursively(tableName);
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
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] === INICIANDO SINCRONIZACION DE COMENTARIOS: {dbName} ===");

                // 1. Localizar el bloque
                var genericBlock = FindBlockRecursively(_currentPlc.BlockGroup, dbName);
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
                var table = FindTagTableRecursively(tableName);
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
                    var group = block.Parent as PlcBlockGroup;
                    group.Blocks.Import(new FileInfo(tempPath), ImportOptions.Override);
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
            _currentPlc = plcSoftware;
        }



        // ==================================================================================================================
        // Lee el valor de una constante global (ej. N_MAX)
        public int ReadGlobalConstant(string tableName, string constantName)
        {
            try
            {
                var table = FindTagTableRecursively(tableName);
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
                var table = FindTagTableRecursively(tableName);
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
                var block = FindBlockRecursively(_currentPlc.BlockGroup, blockName);

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
        // Buscar tabla de variables recursivamente por nombre (Sirve para todo)
        public PlcTagTable FindTagTableRecursively(string tableName)
        {
            if (_currentPlc == null) return null;
            return FindTagTableRecursive(_currentPlc.TagTableGroup, tableName);
        }

        private PlcTagTable FindTagTableRecursive(PlcTagTableGroup group, string tableName)
        {
            if (group == null) return null;

            var table = group.TagTables.Find(tableName);
            if (table != null) return table;

            foreach (var subGroup in group.Groups)
            {
                var found = FindTagTableRecursive(subGroup, tableName);
                if (found != null) return found;
            }
            return null;
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
            // Le pasamos la carpeta raíz del PLC y el nombre que queremos buscar
            return FindBlockRecursively(_currentPlc.BlockGroup, blockName);
        }




        // ==================================================================================================================
        // Buscar bloque recursivamente por nombre
        private PlcBlock FindBlockRecursively(PlcBlockGroup group, string name)
        {
            var block = group.Blocks.Find(name);
            if (block != null) return block;

            foreach (var subFolder in group.Groups)
            {
                var found = FindBlockRecursively(subFolder, name);
                if (found != null) return found;
            }
            return null;
        }




        // ==================================================================================================================
        // Metodo publico para buscar bloque por numero
        public PlcBlock FindBlockByNumber(int number, string blockType)
        {
            if (_currentPlc == null) return null;
            return FindBlockByNumberRecursively(_currentPlc.BlockGroup, number, blockType.ToUpper());
        }




        // ==================================================================================================================
        // Buscar bloque recursivamente por numero
        private PlcBlock FindBlockByNumberRecursively(PlcBlockGroup group, int number, string blockType)
        {
            // Recorremos todos los bloques de la carpeta actual
            foreach (var block in group.Blocks)
            {
                // TIA Portal guarda el número del bloque en la propiedad 'Number'
                if (block.Number == number)
                {
                    // Si el número coincide, verificamos que sea del tipo correcto (FC, FB, DB)
                    // Openness usa clases específicas para cada tipo de bloque
                    if (blockType == "DB" && (block is GlobalDB || block is InstanceDB || block is ArrayDB)) return block;
                    if (blockType == "FC" && block is FC) return block;
                    if (blockType == "FB" && block is FB) return block;
                    if (blockType == "OB" && block is OB) return block;
                }
            }

            // Si no está en esta carpeta, buscamos en las subcarpetas de forma recursiva
            foreach (var subFolder in group.Groups)
            {
                var found = FindBlockByNumberRecursively(subFolder, number, blockType);
                if (found != null) return found;
            }

            // Si terminamos de buscar en todas partes y no está, devolvemos null (Vía libre)
            return null;
        }


    }
}