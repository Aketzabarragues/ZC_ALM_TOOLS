using System.Collections.Generic;
using System.Linq;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.Models.Generator
{

    // ==================================================================================================================
    /// <summary>
    /// Clase de contexto que encapsula la preparación y validación del entorno necesario 
    /// (existencia de tablas, bloques de datos y lectura de límites N_MAX) antes de ejecutar 
    /// operaciones de comparación o sincronización de dispositivos con TIA Portal.
    /// </summary>
    public class DevicesEnvironment
    {
        public bool IsValid { get; private set; } = false;

        // Datos extraídos listos para usar
        public int ExcelNMax { get; private set; }

        public DevicesEnvironment(
            ConfigDeviceCategory category,
            ConfigDeviceSettings settings,
            Dictionary<string, List<object>> cache,
            TiaPlcService tiaPlcService,
            bool validatePlc = true)
        {
            if (category == null || settings == null || cache == null) return;

            // Extraer el valor de N_MAX del Excel
            if (cache.TryGetValue(settings.Disp_N_Max, out var limits))
            {
                var limitItem = limits.Cast<Disp_Config>().FirstOrDefault(x => x.Nombre == category.GlobalConfigKey);
                ExcelNMax = limitItem?.Valor ?? 0;
            }

            // Si solo queríamos extraer el dato de Excel (ej. para la UI), paramos aquí
            if (!validatePlc || tiaPlcService == null)
            {
                IsValid = true;
                return;
            }

            // Validación contra TIA Portal
            LogService.Write($"[DEVICES-ENVIRONMENT] Validando entorno PLC para categoría '{category.Name}'...");

            if (tiaPlcService.FindTagTableByName(settings.ConfigTableName) == null)
            {
                StatusService.Set($"Error: No se encuentra la tabla de constantes '{settings.ConfigTableName}'.", StatusType.Error);
                return;
            }

            if (tiaPlcService.FindTagTableByName(category.TiaTable) == null)
            {
                StatusService.Set($"Error: No se encuentra la tabla de variables '{category.TiaTable}'.", StatusType.Error);
                return;
            }

            if (tiaPlcService.FindBlockByName(category.TiaDbName) == null)
            {
                StatusService.Set($"Error: No se encuentra el bloque de datos '{category.TiaDbName}'.", StatusType.Error);
                return;
            }

            // Todo existe y está listo para ser sincronizado/comparado
            IsValid = true;
        }
    }
}