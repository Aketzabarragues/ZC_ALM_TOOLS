using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Communication;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.HW.Features;
using ZC_ALM_TOOLS.Core; // Asegúrate de tener acceso a LogService
using ZC_ALM_TOOLS.Models;

namespace ZC_ALM_TOOLS.Services
{
    public class TiaHmiService
    {
        public TiaHmiService() { }

        public bool SyncHmiVariables(object hmiSoftwareObj, string plcName, ConfigDeviceCategory category, List<IDevice> devices)
        {
            try
            {
                // 1. Casteamos el objeto al tipo de HMI de Openness
                HmiTarget hmiTarget = hmiSoftwareObj as HmiTarget;
                if (hmiTarget == null)
                {
                    LogService.Write("[TIA-HMI-SERVICE] [SyncHmiVariables] Error: El objeto destino no es un HmiTarget válido.", true);
                    return false;
                }

                string tableNameToFind = $"002_{plcName}_{category.TiaTable}";

                LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] INICIANDO EXPLORACIÓN: {hmiTarget.Name} ");
                LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Buscando vinculación con PLC: {plcName}");
                LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Tabla objetivo: {tableNameToFind}");






                // =========================================================================
                // FASE A: EXPLORAR CONEXIONES
                // =========================================================================
                string connectionName = "";
                LogService.Write($"[TIA-HMI-SERVICE] --- Analizando Conexiones (Total reportadas: {hmiTarget.Connections.Count}) ---");

                foreach (Connection connection in hmiTarget.Connections)
                {
                    LogService.Write($"[TIA-HMI-SERVICE] -> Conexión encontrada: '{connection.Name}'");
                    if (connection.Name.IndexOf(plcName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        connectionName = connection.Name;
                    }
                        
                    LogService.Write($"[TIA-HMI-SERVICE] --- Analizando Conexiones (Total reportadas: {connection.Name}) ---");
                    LogService.Write($"[TIA-HMI-SERVICE] --- Analizando Conexiones (Total reportadas: {connection.Parent}) ---");
                }

                if (!string.IsNullOrEmpty(connectionName))
                    LogService.Write($"[TIA-HMI-SERVICE] [OK] Se usará la conexión escaneada: {connectionName}");
                else if (hmiTarget.Connections.Count > 0)
                {
                    connectionName = hmiTarget.Connections[0].Name;
                    LogService.Write($"[TIA-HMI-SERVICE] [AVISO] No hay coincidencia exacta. Usando primera detectada: {connectionName}");
                }
                else
                {
                    connectionName = "HMI_PST_PLC_PST";
                    LogService.Write($"[TIA-HMI-SERVICE] [AVISO] 0 conexiones. Forzando nombre por defecto: {connectionName}");
                }


                // =========================================================================
                // FASE B: BUSCAR LA TABLA DE VARIABLES (RECURSIVO)
                // =========================================================================
                // CORREGIMOS EL NOMBRE PARA QUE NO SE DUPLIQUE EL 002_
                string prefijoHmi = $"002_{plcName}_";
                // Si category.TiaTable es "002_Disp_V", le quitamos el "002_" para que quede "Disp_V"
                string nombreTablaLimpio = category.TiaTable.Replace("002_", "");
                tableNameToFind = prefijoHmi + nombreTablaLimpio;

                LogService.Write($"[TIA-HMI-SERVICE] --- Buscando Tabla de Variables: {tableNameToFind} ---");



                LogService.Write("[TIA-HMI-SERVICE] [SyncHmiVariables] Buscando Tabla de Variables");

                // Empezamos la búsqueda desde la raíz de grupos de variables del HMI
                TagTable foundTable = FindTagTableRecursively(hmiTarget.TagFolder, tableNameToFind, "/Raíz/");

                if (foundTable != null)
                {
                    LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] La tabla '{tableNameToFind}' YA EXISTE en este HMI.");
                    LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Aquí exportaríamos a XML y haríamos la cirugía de variables.");
                }
                else
                {
                    LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] La tabla '{tableNameToFind}' NO EXISTE.");
                    LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Aquí crearíamos una tabla vacía y la exportaríamos a XML.");
                }

                LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] EXPLORACIÓN FINALIZADA");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-HMI-SERVICE] [SyncHmiVariables] Excepción crítica: {ex.Message}", true);
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
                    LogService.Write($"[TIA-HMI-SERVICE] Tabla localizada en la ruta: {currentPath}{table.Name}");
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
        public bool SyncHmiTextLists(object hmiTarget, ConfigDeviceCategory category, List<IDevice> devices)
        {
            return true;
        }

        public bool SyncHmiAlarms(object hmiTarget, ConfigDeviceCategory category, List<IDevice> devices)
        {
            return true;
        }
    }
}