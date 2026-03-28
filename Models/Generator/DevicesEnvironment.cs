using System.Collections.Generic;
using System.Linq;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.Models.Generator
{
    /// <summary>
    /// Valida que el entorno (Excel + TIA Portal) esté listo para operar con una categoría de dispositivos.
    /// </summary>
    public class DevicesEnvironment
    {
        public bool IsValid { get; private set; } = false;
        public int ExcelNMax { get; private set; }

        public DevicesEnvironment(
            ConfigDeviceCategory category,
            ConfigDeviceSettings settings,
            Dictionary<string, List<object>> cache,
            TiaPlcService tiaPlcService,
            bool validatePlc = true)
        {
            if (category == null || settings == null || cache == null) return;

            // 1. Extraer N_MAX de la caché (Cargado previamente por DataService.GetNMaxConfigsAsync)
            // Buscamos en la caché la lista de límites y filtramos por la clase del modelo (ej. "Disp_ED")
            // 1. Extraer N_MAX de la caché (Cargado previamente por DataService)
            if (cache.TryGetValue("CONFIG_LIMITS", out var limits))
            {
                var limitEntry = limits.Cast<Disp_Config>().FirstOrDefault(x => x.Nombre == category.ModelClass);
                ExcelNMax = limitEntry?.Valor ?? 0;
            }

            // 2. Si solo es consulta para UI (sin PLC), marcamos válido y salimos
            if (!validatePlc || tiaPlcService == null)
            {
                IsValid = true;
                return;
            }

            // 3. Validación contra TIA Portal (Usa los nombres definidos en el JSON)
            LogService.Write($"[DEVICES-ENVIRONMENT] [DevicesEnvironment] Validando entorno PLC para '{category.Name}'...");

            // Validar Tabla de Constantes (Configuración Global)
            if (tiaPlcService.FindTagTableByName(settings.TiaTable) == null)
            {
                StatusService.Set($"Error: No existe la tabla de constantes '{settings.TiaTable}'.", StatusType.Error);
                return;
            }

            // Validar Tabla de Variables (Específica del Dispositivo)
            if (tiaPlcService.FindTagTableByName(category.TiaTable) == null)
            {
                StatusService.Set($"Error: No existe la tabla de variables '{category.TiaTable}'.", StatusType.Error);
                return;
            }

            // Validar Bloque de Datos (DB de Instancia/Global)
            if (tiaPlcService.FindBlockByName(category.TiaDbName) == null)
            {
                StatusService.Set($"Error: No existe el bloque '{category.TiaDbName}'.", StatusType.Error);
                return;
            }

            IsValid = true;
        }
    }
}