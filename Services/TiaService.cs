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
    // Servicio para comunicación directa con Siemens Openness
    public class TiaService
    {
        private readonly PlcSoftware _plcSoftware;

        // Evento unificado para notificar cambios de estado a la UI
        public Action<string, bool> StatusChanged { get; set; }

        public TiaService(PlcSoftware plcSoftware)
        {
            _plcSoftware = plcSoftware;
        }

        #region 1. GESTIÓN DE CONSTANTES (GLOBALES Y USUARIO)

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

        // Sincroniza el valor de una constante global de dimensionado
        public bool SyncGlobalConstant(string tableName, string constantName, int newValue)
        {
            try
            {
                LogService.Write($"[TIA] Verificando constante: {constantName}...");
                var table = FindTagTable(tableName);
                if (table == null) throw new Exception($"No se encontró la tabla '{tableName}'");

                var constant = table.UserConstants.Find(constantName);
                if (constant == null) throw new Exception($"No existe la constante '{constantName}'");

                if (int.TryParse(constant.Value, out int currentValue))
                {
                    if (currentValue != newValue)
                    {
                        LogService.Write($"[TIA-CONFIG] Modificando {constantName}: {currentValue} -> {newValue}");
                        constant.Value = newValue.ToString();
                        Report($"{constantName} actualizado a {newValue}.");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Fallo en Sync Global: {ex.Message}", true);
                return false;
            }
        }

        // Sincroniza la lista de IDs (Constantes) desde el Excel
        public bool SyncUserConstants(string folderName, string tableName, List<IDevice> excelDevices)
        {
            try
            {
                LogService.Write($"[TIA] === SINCRONIZANDO IDs: {tableName} ===");
                var table = FindTableInFolder(folderName, tableName);
                if (table == null) throw new Exception($"La tabla '{tableName}' no existe.");

                // Eliminar las que sobran en TIA
                var excelIds = new HashSet<int>(excelDevices.Select(d => d.Numero));
                var constantsToDelete = table.UserConstants.Where(c => int.TryParse(c.Value, out int id) && !excelIds.Contains(id)).ToList();

                foreach (var c in constantsToDelete)
                {
                    LogService.Write($"[TIA-DELETE] Borrando ID {c.Value}: {c.Name}");
                    c.Delete();
                }

                // Crear o Renombrar según Excel
                foreach (var dev in excelDevices)
                {
                    var tiaConst = table.UserConstants.FirstOrDefault(c => c.Value == dev.Numero.ToString());

                    if (tiaConst == null)
                    {
                        LogService.Write($"[TIA-CREATE] Creando ID {dev.Numero}: {dev.CPTag}");
                        tiaConst = table.UserConstants.Create(dev.CPTag, "Int", dev.Numero.ToString());
                    }

                    if (tiaConst.Name != dev.CPTag)
                    {
                        LogService.Write($"[TIA-RENAME] ID {dev.Numero}: {tiaConst.Name} -> {dev.CPTag}");
                        tiaConst.Name = dev.CPTag;
                    }

                    UpdatePlcComment(tiaConst, dev.CPComentario);
                }
                Report("Sincronización de constantes finalizada.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-FATAL] Error en Sync Constants: {ex.Message}", true);
                return false;
            }
        }

        #endregion

        #region 2. COMPILACIÓN Y BLOQUES

        // Compila un bloque específico (necesario antes de la cirugía XML)
        public bool CompileBlock(string blockName)
        {
            try
            {
                LogService.Write($"[TIA] Buscando bloque '{blockName}' para compilar...");
                var block = FindBlockRecursively(_plcSoftware.BlockGroup, blockName);

                if (block == null)
                {
                    LogService.Write($"[TIA-ERROR] No se encontró el bloque '{blockName}'", true);
                    return false;
                }

                ICompilable compileService = block.GetService<ICompilable>();
                if (compileService != null)
                {
                    LogService.Write($"[TIA] Compilando: {blockName}...");
                    CompilerResult result = compileService.Compile();
                    return result.State != CompilerResultState.Error;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Fallo al compilar: {ex.Message}", true);
                return false;
            }
        }

        // Inyecta comentarios en el DB mediante manipulación de XML
        public bool SyncDbComments(string dbName, string arrayName, List<IDevice> devices)
        {
            try
            {
                var genericBlock = FindBlockRecursively(_plcSoftware.BlockGroup, dbName);
                var db = genericBlock as GlobalDB;

                if (db == null) throw new Exception($"No se pudo encontrar el DB: {dbName}");

                string xmlPath = Path.Combine(AppConfigManager.TempPath, $"{dbName}.xml");
                if (File.Exists(xmlPath)) File.Delete(xmlPath);

                db.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);

                XDocument doc = XDocument.Load(xmlPath);
                XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

                var staticSection = doc.Descendants(ns + "Section").FirstOrDefault(s => s.Attribute("Name")?.Value == "Static");
                var arrayMember = staticSection?.Elements(ns + "Member").FirstOrDefault(m => m.Attribute("Name")?.Value == arrayName);

                if (arrayMember != null)
                {
                    foreach (var dev in devices)
                    {
                        var subelement = arrayMember.Elements(ns + "Subelement").FirstOrDefault(s => s.Attribute("Path")?.Value == dev.Numero.ToString());

                        if (subelement == null)
                        {
                            subelement = new XElement(ns + "Subelement", new XAttribute("Path", dev.Numero.ToString()));
                            arrayMember.Add(subelement);
                        }

                        subelement.Elements(ns + "Comment").Remove();
                        subelement.Add(new XElement(ns + "Comment",
                            new XElement(ns + "MultiLanguageText",
                                new XAttribute("Lang", "es-ES"),
                                dev.Tag + " - " + dev.Descripcion)));
                    }
                    doc.Save(xmlPath);
                }

                // Re-importar el bloque modificado
                var parent = genericBlock.Parent;
                if (parent is PlcBlockUserGroup folder) folder.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                else if (parent is PlcBlockGroup root) root.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);

                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-XML] Error en cirugía XML: {ex.Message}", true);
                return false;
            }
        }

        #endregion

        #region 3. HELPERS DE EXPORTACIÓN Y BÚSQUEDA

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

        private PlcTagTable FindTagTable(string name)
        {
            var rootTable = _plcSoftware.TagTableGroup.TagTables.Find(name);
            if (rootTable != null) return rootTable;
            return _plcSoftware.TagTableGroup.Groups.SelectMany(g => g.TagTables).FirstOrDefault(t => t.Name == name);
        }

        private PlcTagTable FindTableInFolder(string folder, string table)
        {
            var group = _plcSoftware.TagTableGroup.Groups.Find(folder);
            return group?.TagTables.Find(table);
        }

        private void UpdatePlcComment(PlcUserConstant constant, string comment)
        {
            foreach (var item in constant.Comment.Items)
            {
                try { item.Text = comment; }
                catch { item.SetAttribute("Text", comment); }
            }
        }

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

        private void Report(string msg, bool error = false)
        {
            StatusChanged?.Invoke(msg, error);
        }

        #endregion
    }
}