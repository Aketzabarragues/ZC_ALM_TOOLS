using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.Models.Generator
{
    public class ParamsAlarmsEnvironment
    {
        public bool IsValid { get; private set; } = false;

        // Propiedades de nombres calculados para TIA Portal
        public string TableName { get; private set; }
        public string ConstReal { get; private set; }
        public string ConstInt { get; private set; }
        public string ConstAlm { get; private set; }
        public string ConstAlmHmi { get; private set; }

        public string DbNameReal { get; private set; }
        public string DbNameInt { get; private set; }
        public string DbNameAlm { get; private set; }

        public int DbNumReal { get; private set; }
        public int DbNumInt { get; private set; }
        public int DbNumAlm { get; private set; }

        public ParamsAlarmsEnvironment(
            Process process,
            ConfigProcessSettings settings,
            IEnumerable<Parameter> reales,
            IEnumerable<Parameter> enteros,
            IEnumerable<Alarms> alarmas,
            TiaPlcCacheService cacheService,
            bool forSync = false,
            bool checkReales = true,
            bool checkEnteros = true,
            bool checkAlarmas = true)
        {
            // Validaciones iniciales de nulidad
            if (process == null || settings == null || cacheService == null) return;

            // 1. Construcción dinámica de nombres según estándar del JSON
            TableName = $"{process.Id}_{process.Nombre}";
            ConstReal = $"{process.Id}{settings.SuffixConstReal}";
            ConstInt = $"{process.Id}{settings.SuffixConstInt}";
            ConstAlm = $"{process.Id}{settings.SuffixConstAlm}";
            ConstAlmHmi = $"{process.Id}{settings.SuffixConstAlmHmi}";

            // Extraer números de DB (asumimos que todos los parámetros de una lista van al mismo DB)
            DbNumReal = reales.FirstOrDefault()?.DbNumber ?? -1;
            DbNumInt = enteros.FirstOrDefault()?.DbNumber ?? -1;
            DbNumAlm = alarmas.FirstOrDefault()?.DbNumber ?? -1;

            // Construcción de nombres de DBs (Ej: DB200_PREAL)
            DbNameReal = DbNumReal != -1 ? $"DB{DbNumReal}{settings.SuffixDbReal}" : null;
            DbNameInt = DbNumInt != -1 ? $"DB{DbNumInt}{settings.SuffixDbInt}" : null;
            DbNameAlm = DbNumAlm != -1 ? $"DB{DbNumAlm}{settings.SuffixDbAlm}" : null;

            // 2. Validación de existencia en el proyecto de TIA Portal
            App.ServiceProvider?.GetService<ILogService>()?.Write($"[PARAMS-ENVIRONMENT] Validando entorno para proceso: {process.Nombre}");

            // La tabla de variables es obligatoria
            if (cacheService.FindTagTableByName(TableName) == null)
            {
                App.ServiceProvider?.GetService<IStatusService>()?.Set($"Error: Tabla de variables '{TableName}' no encontrada.", StatusType.Error);
                return;
            }

            // Validación condicional de bloques (solo si hay datos y se requiere check)
            if ((!forSync || checkReales) && DbNameReal != null)
            {
                if (cacheService.FindBlockByName(DbNameReal) == null)
                {
                    App.ServiceProvider?.GetService<IStatusService>()?.Set($"Error: Bloque '{DbNameReal}' no encontrado.", StatusType.Error);
                    return;
                }
            }

            if ((!forSync || checkEnteros) && DbNameInt != null)
            {
                if (cacheService.FindBlockByName(DbNameInt) == null)
                {
                    App.ServiceProvider?.GetService<IStatusService>()?.Set($"Error: Bloque '{DbNameInt}' no encontrado.", StatusType.Error);
                    return;
                }
            }

            if ((!forSync || checkAlarmas) && DbNameAlm != null)
            {
                if (cacheService.FindBlockByName(DbNameAlm) == null)
                {
                    App.ServiceProvider?.GetService<IStatusService>()?.Set($"Error: Bloque '{DbNameAlm}' no encontrado.", StatusType.Error);
                    return;
                }
            }

            // Si llegamos aquí, el entorno PLC coincide con la definición del Excel
            IsValid = true;
        }
    }
}