using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var table = FindTagTable(tableName);
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
        // Sincroniza el valor de una constante global de dimensionado
        public bool SyncGlobalConstant(string tableName, string constantName, int newValue)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstant] Verificando constante: {constantName}...");
                var table = FindTagTable(tableName);
                if (table == null) throw new Exception($"No se encontró la tabla '{tableName}'");

                var constant = table.UserConstants.Find(constantName);
                if (constant == null) throw new Exception($"No existe la constante '{constantName}'");

                if (int.TryParse(constant.Value, out int currentValue))
                {
                    if (currentValue != newValue)
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncGlobalConstant] Modificando {constantName}: {currentValue} -> {newValue}");
                        constant.Value = newValue.ToString();
                        Report($"{constantName} actualizado a {newValue}.");
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
        // Sincroniza la lista de IDs (Constantes) desde el Excel
        public bool SyncUserConstants(string folderName, string tableName, List<IDevice> excelDevices)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants]  === SINCRONIZANDO IDs: {tableName} ===");
                var table = FindTableInFolder(folderName, tableName);
                if (table == null) throw new Exception($"La tabla '{tableName}' no existe.");

                // Eliminar las que sobran en TIA
                var excelIds = new HashSet<int>(excelDevices.Select(d => d.Numero));
                var constantsToDelete = table.UserConstants.Where(c => int.TryParse(c.Value, out int id) && !excelIds.Contains(id)).ToList();

                foreach (var c in constantsToDelete)
                {
                    LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] Borrando ID {c.Value}: {c.Name}");
                    c.Delete();
                }

                // Crear o Renombrar según Excel
                foreach (var dev in excelDevices)
                {
                    var tiaConst = table.UserConstants.FirstOrDefault(c => c.Value == dev.Numero.ToString());

                    if (tiaConst == null)
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] Creando ID {dev.Numero}: {dev.CPTag}");
                        tiaConst = table.UserConstants.Create(dev.CPTag, "Int", dev.Numero.ToString());
                    }

                    if (tiaConst.Name != dev.CPTag)
                    {
                        LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] ID {dev.Numero}: {tiaConst.Name} -> {dev.CPTag}");
                        tiaConst.Name = dev.CPTag;
                    }

                    UpdatePlcComment(tiaConst, dev.CPComentario);
                }
                Report("Sincronización de constantes finalizada.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncUserConstants] Error en Sync Constants: {ex.Message}", true);
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
        // Inyecta comentarios en el DB mediante manipulación de XML
        public bool SyncDbComments(string dbName, string arrayName, List<IDevice> devices)
        {
            try
            {
                LogService.Write($"[TIA-PLC-SERVICE] [SyncDbComments] === INICIANDO CIRUGÍA XML: {dbName} ===");

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
        // Exportar tabla de variables
        public bool ExportTagTable(string folderName, string tableName, string xmlPath)
        {
            try
            {
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
                var table = FindTableInFolder(folderName, tableName);
                if (table == null) return false;

                table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
                return true;
            }
            catch { return false; }
        }




        // ==================================================================================================================
        // Encontrar tag en una tabla de variables
        private PlcTagTable FindTagTable(string name)
        {
            var rootTable = _currentPlc.TagTableGroup.TagTables.Find(name);
            if (rootTable != null) return rootTable;
            return _currentPlc.TagTableGroup.Groups.SelectMany(g => g.TagTables).FirstOrDefault(t => t.Name == name);
        }




        // ==================================================================================================================
        // Buscar tabla dentro de una carpeta
        private PlcTagTable FindTableInFolder(string folder, string table)
        {
            var group = _currentPlc.TagTableGroup.Groups.Find(folder);
            return group?.TagTables.Find(table);
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
        // Buscar bloque recursivamente
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
        private void Report(string msg, bool error = false)
        {
            StatusService.Set(msg, error);
        }

    }
}