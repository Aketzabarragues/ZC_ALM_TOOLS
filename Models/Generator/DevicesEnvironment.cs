using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
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
            TiaPlcCacheService cacheService,
            bool validatePlc = true)
        {
            if (category == null || settings == null || cache == null) return;
                        
            // Buscamos en la caché la lista de límites y filtramos por la clase del modelo (ej. "Disp_ED")
            if (cache.TryGetValue(settings.Name, out var limits))
            {
                var limitEntry = limits.Cast<Disp_Config>().FirstOrDefault(x => x.Nombre == category.ModelClass);
                ExcelNMax = limitEntry?.Valor ?? 0;
            }

            // 2. Si solo es consulta para UI (sin PLC), marcamos válido y salimos
            if (!validatePlc || cacheService == null)
            {
                IsValid = true;
                return;
            }

            // 3. Validación contra TIA Portal (Usa los nombres definidos en el JSON)
            App.ServiceProvider?.GetService<ILogService>()?.Write($"[DEVICES-ENVIRONMENT] [DevicesEnvironment] Validando entorno PLC para '{category.Name}'...");

            // Validar Tabla de Constantes (Configuración Global)
            if (cacheService.FindTagTableByName(settings.TiaTable) == null)
            {
                App.ServiceProvider?.GetService<IStatusService>()?.Set($"[DEVICES-ENVIRONMENT] [DevicesEnvironment] Error: No existe la tabla de constantes '{settings.TiaTable}'.", StatusType.Error);
                return;
            }

            // Validar Tabla de Variables (Específica del Dispositivo)
            if (cacheService.FindTagTableByName(category.TiaTable) == null)
            {
                App.ServiceProvider?.GetService<IStatusService>()?.Set($"[DEVICES-ENVIRONMENT] [DevicesEnvironment] Error: No existe la tabla de variables '{category.TiaTable}'.", StatusType.Error);
                return;
            }

            // Validar Bloque de Datos (DB de Instancia/Global)
            if (cacheService.FindBlockByName(category.TiaDbName) == null)
            {
                App.ServiceProvider?.GetService<IStatusService>()?.Set($"[DEVICES-ENVIRONMENT] [DevicesEnvironment] Error: No existe el bloque '{category.TiaDbName}'.", StatusType.Error);
                return;
            }

            IsValid = true;
        }
    }
}