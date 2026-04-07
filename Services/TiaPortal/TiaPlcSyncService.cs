using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{

    /// <summary>
    /// Servicio dedicado a la sincronización de datos entre el proyecto de TIA Portal y las fuentes externas (como Excel), 
    /// proporcionando métodos para leer y actualizar constantes globales, así como para gestionar comentarios en bloques y tablas de variables de manera segura y eficiente.
    /// </summary>
    public class TiaPlcSyncService
    {


        private readonly Siemens.Engineering.TiaPortal _tiaApp;
        private readonly Project _currentProject;
        private readonly TiaPlcCacheService _cacheService;

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;

        public TiaPlcSyncService(
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
        /// Lee el valor de una constante global
        /// </summary>
        public int ReadGlobalConstant(string tableName, string constantName)
        {
            try
            {
                var table = _cacheService.FindTagTableByName(tableName);
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
        /// Sincroniza el valor de una constante global de dimensionado de forma asíncrona
        /// </summary>
        public async Task<bool> SyncGlobalConstantAsync(string tableName, string constantName, int newValue)
        {
            try
            {
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncGlobalConstantAsync] Verificando constante: {constantName}...");
                var table = _cacheService.FindTagTableByName(tableName);
                if (table == null) throw new Exception($"No se encontró la tabla '{tableName}'");

                var constant = table.UserConstants.Find(constantName);
                if (constant == null) throw new Exception($"No existe la constante '{constantName}'");

                if (int.TryParse(constant.Value, out int currentValue))
                {
                    if (currentValue != newValue)
                    {
                        _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncGlobalConstantAsync] Modificando {constantName}: {currentValue} -> {newValue}");
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
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncGlobalConstantAsync] Fallo en Sync Global: {ex.Message}", true);
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
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Sincronzando tabla de variables: {tableName}");

                // Buscamos la tabla de variables
                var table = _cacheService.FindTagTableByName(tableName);
                if (table == null) throw new Exception($"La tabla '{tableName}' no existe.");

                if (_currentProject == null) throw new Exception("No hay un proyecto activo asignado.");

                var validExcelDevices = excelDevices.Where(d => d.Estado != "Eliminar").ToList();
                var excelDict = validExcelDevices.ToDictionary(d => d.Numero.ToString());

                _statusService.Set("[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Leyendo constantes actuales en el PLC...", StatusType.Ok);
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
                            _statusService.Set($"[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Borrando {toDelete.Count} variables obsoletas en TIA Portal...", StatusType.Warning);
                            await Task.Delay(25); // Pequeño respiro visual
                            foreach (var c in toDelete)
                            {
                                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Borrando ID {c.Value}: {c.Name}");
                                c.Delete();
                            }
                        }

                        if (toRename.Any())
                        {
                            _statusService.Set($"[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Renombrando {toRename.Count} variables en TIA Portal...", StatusType.Warning);
                            await Task.Delay(25); // Pequeño respiro visual
                            foreach (var c in toRename)
                            {
                                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] ID {c.Value}: {c.Name} -> {excelDict[c.Value].CPTag}");
                                c.Name = excelDict[c.Value].CPTag;
                            }
                        }

                        string xmlPath = Path.Combine(AppConfigService.TempExportPathXml, $"{tableName}.xml");
                        if (File.Exists(xmlPath)) File.Delete(xmlPath);

                        _statusService.Set("[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Exportando tabla para añadir nuevas variables...", StatusType.Ok);
                        await Task.Delay(25);
                        table.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);

                        _statusService.Set("[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Añadiendo variables y comentarios en el XML...", StatusType.Ok);
                        var tagEditor = new XmlTagTableEditorService(xmlPath);
                        tagEditor.ClearConstants();
                        foreach (var dev in validExcelDevices)
                        {
                            tagEditor.AddConstant(dev.CPTag, dev.Numero, dev.CPComentario);
                        }
                        tagEditor.Save();

                        _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Re-importando tabla '{tableName}' en TIA Portal (Override)...");
                        _statusService.Set("[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Importando tabla modificada a TIA Portal...", StatusType.Ok);
                        await Task.Delay(25);

                        var parent = table.Parent;
                        if (parent is PlcTagTableUserGroup folder)
                            folder.TagTables.Import(new FileInfo(xmlPath), ImportOptions.Override);
                        else if (parent is PlcTagTableGroup root)
                            root.TagTables.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        PlcTagTable newTable = (parent is PlcTagTableUserGroup f) ? f.TagTables.Find(tableName) : ((PlcTagTableGroup)parent).TagTables.Find(tableName);
                        var cachedItem = _cacheService.GetAllTagTables().FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                        if (cachedItem != null && newTable != null) cachedItem.Table = newTable;

                        transaction.CommitOnDispose();
                    }
                }

                _statusService.Set("[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Sincronización de constantes finalizada.", StatusType.Ok);
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Error en actualizacion de constantes: {ex.Message}", true);
                _statusService.Set("[TIA-PLC-SYNC-SERVICE] [SyncDispUserConstants] Error en la sincronización.", StatusType.Error);
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
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispDbComments] Buscando bloque '{dbName}' para añadir comentarios...");
                var genericBlock = _cacheService.FindBlockByName(dbName);
                var db = genericBlock as GlobalDB;

                if (db == null)
                {
                    _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispDbComments] ERROR: No se pudo encontrar el bloque '{dbName}'.", true);
                    return false;
                }

                string xmlPath = Path.Combine(AppConfigService.TempExportPathXml, $"{dbName}.xml");
                if (File.Exists(xmlPath)) File.Delete(xmlPath);

                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispDbComments] Exportando bloque para edición: {xmlPath}");

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
                    _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispDbComments] No había textos que actualizar en {dbName}.");
                    return true;
                }
                dbEditor.Save();

                // Cedemos a WPF antes del bloqueo de la transacción de importación
                await Task.Delay(25);

                using (ExclusiveAccess exclusiveAccess = _tiaApp.ExclusiveAccess("Sincronizando Comentarios de DB (ZC ALM TOOLS)..."))
                {
                    using (Transaction transaction = exclusiveAccess.Transaction(_currentProject, $"Sincronizar DB {dbName}"))
                    {
                        _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispDbComments] Re-importando bloque '{dbName}' en TIA Portal...");
                        var parent = genericBlock.Parent;

                        if (parent is PlcBlockUserGroup folder)
                            folder.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                        else if (parent is PlcBlockGroup root)
                            root.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);

                        PlcBlock newBlock = (parent is PlcBlockUserGroup f) ? f.Blocks.Find(dbName) : ((PlcBlockGroup)parent).Blocks.Find(dbName);
                        var cachedItem = _cacheService.GetAllBlocks().FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                        if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                        transaction.CommitOnDispose();
                    }
                }

                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispDbComments] Bloque {dbName} actualizado correctamente.");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncDispDbComments] Error en modificacion de DB: {ex.Message}", true);
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
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Buscando bloque '{dbName}' para añadir comentarios...");

                var block = _cacheService.FindBlockByName(dbName);
                if (block == null) throw new Exception($"Bloque '{dbName}' no encontrado.");

                string tempPath = Path.Combine(AppConfigService.TempExportPathXml, $"{dbName}.xml");
                if (File.Exists(tempPath)) File.Delete(tempPath);

                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Exportando DB a XML temporal...");

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
                    _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Re-importando bloque '{dbName}' en TIA Portal (Override)...");

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
                            var cachedItem = _cacheService.GetAllBlocks().FirstOrDefault(b => b.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
                            if (cachedItem != null && newBlock != null) cachedItem.Block = newBlock;

                            transaction.CommitOnDispose();
                        }
                    }
                    _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Bloque {dbName} actualizado correctamente.");
                    return true;
                }

                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] No había textos que actualizar en {dbName}.");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-SYNC-SERVICE] [SyncParamsAlarmsDbCommentsAsync] Error en la modificacion de DB: {ex.Message}", true);
                return false;
            }
        }



    }
}
