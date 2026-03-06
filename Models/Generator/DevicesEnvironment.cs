using System.Collections.Generic;
using System.Linq;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Services;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.Models.Generator
{
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

            // 1. Extraer el valor de N_MAX del Excel (Nos ahorramos repetirlo 3 veces en el ViewModel)
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

            // 2. Validación temprana contra TIA Portal (Early Return)
            LogService.Write($"[DEVICES-ENVIRONMENT] Validando entorno PLC para categoría '{category.Name}'...");

            if (tiaPlcService.FindTagTableRecursively(settings.ConfigTableName) == null)
            {
                StatusService.Set($"Error: No se encuentra la tabla de constantes '{settings.ConfigTableName}'.", StatusType.Error);
                return;
            }

            if (tiaPlcService.FindTagTableRecursively(category.TiaTable) == null)
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