using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Communication;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using ZC_ALM_TOOLS.Core; // Asegúrate de tener acceso a LogService
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{
    public class TiaHmiService
    {
        private HmiTarget _currentHmi;
        private Project _currentProject;
        private Siemens.Engineering.TiaPortal _tiaApp;

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;


        // ==================================================================================================================
        /// <summary>
        /// Constructor del servicio de HMI. Recibe las instancias de TIA Portal y el proyecto actual, así como los servicios de logging y status para reportar información durante la ejecución.
        /// </summary>
        public TiaHmiService(Siemens.Engineering.TiaPortal tiaApp, Project project,
                             ILogService logService, IStatusService statusService)
        {
            _tiaApp = tiaApp;
            _currentProject = project;

            _logService = logService;
            _statusService = statusService;
        }







        public void RunXmlHmiPoC(HmiTarget hmiTarget, GlobalLibrary library, string newBaseName, string connectionName)
        {
            _logService.Write("[TIA-HMI-SERVICE] [RunXmlHmiPoC] Iniciando RunXmlHmiPoC...");
            if (hmiTarget == null || library == null)
            {
                _logService.Write("[TIA-HMI-SERVICE] hmiTarget o library son nulos.", true);
                return;
            }

            try
            {
                _logService.Write("[TIA-HMI-SERVICE] Navegando por MasterCopyFolder...");
                var rootFolder = library.MasterCopyFolder;
                if (rootFolder == null) throw new Exception("MasterCopyFolder de la librería es nulo.");

                var ufFolder = rootFolder.Folders.Find("UF");
                if (ufFolder == null) throw new Exception("Carpeta 'UF' no encontrada en la librería.");

                var varsFolder = ufFolder.Folders.Find("Variables");
                var imgsFolder = ufFolder.Folders.Find("Imagenes");

                if (varsFolder == null) throw new Exception("Carpeta 'Variables' no encontrada en 'UF'.");
                if (imgsFolder == null) throw new Exception("Carpeta 'Imagenes' no encontrada en 'UF'.");

                _logService.Write("[TIA-HMI-SERVICE] Buscando plantillas UF_Alm y UF_Sinoptico...");
                var tagTableTemplate = varsFolder.MasterCopies.Find("UF_Alm");
                var screenTemplate = imgsFolder.MasterCopies.Find("UF_Sinoptico");

                if (tagTableTemplate == null) throw new Exception("Plantilla 'UF_Alm' no encontrada.");
                if (screenTemplate == null) throw new Exception("Plantilla 'UF_Sinoptico' no encontrada.");

                string tempDir = Path.Combine(AppConfigService.TempExportPathXml, "HmiExport");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                // =========================================================================
                // FASE 1: TABLA DE VARIABLES
                // =========================================================================
                _logService.Write("[TIA-HMI-SERVICE] Creando Tabla de variables temporal...");
                _statusService.Set("Instanciando y exportando Tabla de Variables...", StatusType.Warning);
                var tempTable = hmiTarget.TagFolder.TagTables.CreateFrom(tagTableTemplate);

                if (tempTable == null) throw new Exception("CreateFrom devolvió nulo para la tabla de variables.");

                string tableXmlPath = Path.Combine(tempDir, $"{tempTable.Name}.xml");
                if (File.Exists(tableXmlPath)) File.Delete(tableXmlPath);

                _logService.Write($"[TIA-HMI-SERVICE] Exportando tabla a XML: {tableXmlPath}");
                tempTable.Export(new FileInfo(tableXmlPath), ExportOptions.WithDefaults);

                // --- Borrar INMEDIATAMENTE tras exportar ---
                _logService.Write("[TIA-HMI-SERVICE] Borrando tabla temporal original (pre-import)...");
                tempTable.Delete();

                _logService.Write("[TIA-HMI-SERVICE] Modificando XML de la tabla...");
                XDocument tableDoc = XDocument.Load(tableXmlPath);

                var tableNameNode = tableDoc.Descendants("Hmi.Tag.TagTable").Elements("AttributeList").Elements("Name").FirstOrDefault();
                if (tableNameNode != null) tableNameNode.Value = $"Tabla_{newBaseName}";

                foreach (var tagNode in tableDoc.Descendants("Hmi.Tag.Tag"))
                {
                    var nameNode = tagNode.Elements("AttributeList").Elements("Name").FirstOrDefault();
                    if (nameNode != null) nameNode.Value = $"{nameNode.Value}_{newBaseName}";

                    var connectionNode = tagNode.Elements("LinkList").Elements("Connection").Elements("Name").FirstOrDefault();
                    if (connectionNode != null) connectionNode.Value = connectionName;
                }
                tableDoc.Save(tableXmlPath);

                _logService.Write("[TIA-HMI-SERVICE] Importando XML de la tabla modificado...");
                hmiTarget.TagFolder.TagTables.Import(new FileInfo(tableXmlPath), ImportOptions.None);


                // =========================================================================
                // FASE 2: PANTALLA
                // =========================================================================
                _logService.Write("[TIA-HMI-SERVICE] Creando Pantalla temporal...");
                _statusService.Set("Instanciando y exportando Pantalla...", StatusType.Warning);
                var tempScreen = hmiTarget.ScreenFolder.Screens.CreateFrom(screenTemplate);

                if (tempScreen == null) throw new Exception("CreateFrom devolvió nulo para la pantalla.");

                string screenXmlPath = Path.Combine(tempDir, $"{tempScreen.Name}.xml");
                if (File.Exists(screenXmlPath)) File.Delete(screenXmlPath);

                _logService.Write($"[TIA-HMI-SERVICE] Exportando pantalla a XML: {screenXmlPath}");
                tempScreen.Export(new FileInfo(screenXmlPath), ExportOptions.WithDefaults);

                // --- Borrar INMEDIATAMENTE tras exportar ---
                _logService.Write("[TIA-HMI-SERVICE] Borrando pantalla temporal original (pre-import)...");
                tempScreen.Delete();

                _logService.Write("[TIA-HMI-SERVICE] Modificando XML de la pantalla y relinkando variables...");
                XDocument screenDoc = XDocument.Load(screenXmlPath);

                // 2.1 Renombrar Pantalla
                var screenNameNode = screenDoc.Descendants("Hmi.Screen.Screen").Elements("AttributeList").Elements("Name").FirstOrDefault();
                if (screenNameNode != null) screenNameNode.Value = $"Pantalla_{newBaseName}";

                // 2.2 Relinkar variables conectadas a objetos (Botones, IO Fields, etc.)
                var openLinkTags = screenDoc.Descendants("Tag")
                                            .Where(e => (string)e.Attribute("TargetID") == "@OpenLink")
                                            .Elements("Name");

                int relinkCount = 0;
                foreach (var nameNode in openLinkTags)
                {
                    nameNode.Value = $"{nameNode.Value}_{newBaseName}";
                    relinkCount++;
                }
                _logService.Write($"[TIA-HMI-SERVICE] Se relinkaron {relinkCount} referencias a variables en la pantalla.");

                screenDoc.Save(screenXmlPath);

                _logService.Write("[TIA-HMI-SERVICE] Importando XML de la pantalla modificada...");
                hmiTarget.ScreenFolder.Screens.Import(new FileInfo(screenXmlPath), ImportOptions.None);

                _logService.Write($"[TIA-HMI-SERVICE] PoC XML Finalizado con éxito para '{newBaseName}'.");
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-HMI-SERVICE] Error en XML PoC: {ex.Message}\nStack: {ex.StackTrace}", true);
            }
        }











































        public bool SyncHmiVariables(object hmiSoftwareObj, string plcName, ConfigDeviceCategory category, List<IDevice> devices)
        {
            try
            {
                // 1. Casteamos el objeto al tipo de HMI de Openness
                HmiTarget hmiTarget = hmiSoftwareObj as HmiTarget;
                if (hmiTarget == null)
                {
                    _logService.Write("[TIA-HMI-SERVICE] [SyncHmiVariables] Error: El objeto destino no es un HmiTarget válido.", true);
                    return false;
                }

                string tableNameToFind = $"002_{plcName}_{category.TiaTable}";

                _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] INICIANDO EXPLORACIÓN: {hmiTarget.Name} ");
                _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Buscando vinculación con PLC: {plcName}");
                _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Tabla objetivo: {tableNameToFind}");






                // =========================================================================
                // FASE A: EXPLORAR CONEXIONES
                // =========================================================================
                string connectionName = "";
                _logService.Write($"[TIA-HMI-SERVICE] Analizando Conexiones (Total reportadas: {hmiTarget.Connections.Count})");

                foreach (Siemens.Engineering.Hmi.Communication.Connection connection in hmiTarget.Connections)
                {
                    _logService.Write($"[TIA-HMI-SERVICE] -> Conexión encontrada: '{connection.Name}'");
                    if (connection.Name.IndexOf(plcName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        connectionName = connection.Name;
                    }

                    _logService.Write($"[TIA-HMI-SERVICE] Analizando Conexiones (Total reportadas: {connection.Name})");
                    _logService.Write($"[TIA-HMI-SERVICE] Analizando Conexiones (Total reportadas: {connection.Parent})");
                }

                if (!string.IsNullOrEmpty(connectionName))
                    _logService.Write($"[TIA-HMI-SERVICE] [OK] Se usará la conexión escaneada: {connectionName}");
                else if (hmiTarget.Connections.Count > 0)
                {
                    connectionName = hmiTarget.Connections[0].Name;
                    _logService.Write($"[TIA-HMI-SERVICE] [AVISO] No hay coincidencia exacta. Usando primera detectada: {connectionName}");
                }
                else
                {
                    connectionName = "HMI_PST_PLC_PST";
                    _logService.Write($"[TIA-HMI-SERVICE] [AVISO] 0 conexiones. Forzando nombre por defecto: {connectionName}");
                }


                // =========================================================================
                // FASE B: BUSCAR LA TABLA DE VARIABLES (RECURSIVO)
                // =========================================================================
                // CORREGIMOS EL NOMBRE PARA QUE NO SE DUPLIQUE EL 002_
                string prefijoHmi = $"002_{plcName}_";
                // Si category.TiaTable es "002_Disp_V", le quitamos el "002_" para que quede "Disp_V"
                string nombreTablaLimpio = category.TiaTable.Replace("002_", "");
                tableNameToFind = prefijoHmi + nombreTablaLimpio;

                _logService.Write($"[TIA-HMI-SERVICE] Buscando Tabla de Variables: {tableNameToFind}");



                _logService.Write("[TIA-HMI-SERVICE] [SyncHmiVariables] Buscando Tabla de Variables");

                // Empezamos la búsqueda desde la raíz de grupos de variables del HMI
                TagTable foundTable = FindTagTableRecursively(hmiTarget.TagFolder, tableNameToFind, "/Raíz/");

                if (foundTable != null)
                {
                    _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] La tabla '{tableNameToFind}' YA EXISTE en este HMI.");
                    _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Aquí exportaríamos a XML y haríamos la cirugía de variables.");
                }
                else
                {
                    _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] La tabla '{tableNameToFind}' NO EXISTE.");
                    _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Aquí crearíamos una tabla vacía y la exportaríamos a XML.");
                }

                _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] EXPLORACIÓN FINALIZADA");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Excepción crítica: {ex.Message}", true);
                return false;
            }
        }

        // =========================================================================
        // MÉTODOS AUXILIARES
        // =========================================================================
        private TagTable FindTagTableRecursively(TagFolder group, string tableNameToFind, string currentPath)
        {
            // 1. Buscamos en las tablas que cuelgan directamente de esta carpeta
            foreach (TagTable table in group.TagTables)
            {
                if (table.Name.Equals(tableNameToFind, StringComparison.OrdinalIgnoreCase))
                {
                    _logService.Write($"[TIA-HMI-SERVICE] Tabla localizada en la ruta: {currentPath}{table.Name}");
                    return table;
                }
            }

            // 2. Si no está, entramos recursivamente en cada subcarpeta
            foreach (TagFolder subGroup in group.Folders)
            {
                TagTable found = FindTagTableRecursively(subGroup, tableNameToFind, currentPath + subGroup.Name + "/");
                if (found != null)
                {
                    return found;
                }
            }

            // Si llegamos aquí, no está en esta rama
            return null;
        }

        // Dejamos los otros métodos preparados pero sin tocar aún
        // ==================================================================================================================
        public bool SyncHmiTextLists(object hmiTarget, ConfigDeviceCategory category, List<IDevice> devices)
        {
            _logService.Write("[TIA-HMI-SERVICE] SyncHmiTextLists: NO IMPLEMENTADO.", true);
            _statusService.Set("Sincronización HMI de listas de texto: pendiente de implementar.", StatusType.Warning);
            return false;
        }

        // ==================================================================================================================
        public bool SyncHmiAlarms(object hmiTarget, ConfigDeviceCategory category, List<IDevice> devices)
        {
            _logService.Write("[TIA-HMI-SERVICE] SyncHmiAlarms: NO IMPLEMENTADO.", true);
            _statusService.Set("Sincronización HMI de alarmas: pendiente de implementar.", StatusType.Warning);
            return false;
        }
    }
}