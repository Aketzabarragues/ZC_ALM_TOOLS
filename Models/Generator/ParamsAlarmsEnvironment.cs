using System.Collections.Generic;
using System.Linq;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.Models
{
    public class ParamsAlarmsEnvironment
    {
        public bool IsValid { get; private set; } = false;

        public string TableName { get; private set; }
        public string ConstReal { get; private set; }
        public string ConstInt { get; private set; }
        public string ConstAlm { get; private set; }
        public string ConstAlmHmi { get; private set; }

        public int DbNumReal { get; private set; }
        public int DbNumInt { get; private set; }
        public int DbNumAlm { get; private set; }

        public string DbNameReal { get; private set; }
        public string DbNameInt { get; private set; }
        public string DbNameAlm { get; private set; }

        // El constructor recibe todo lo necesario y se auto-configura
        public ParamsAlarmsEnvironment(
            Process process,
            ConfigProcessSettings settings,
            IEnumerable<Parameter> reales,
            IEnumerable<Parameter> enteros,
            IEnumerable<Alarms> alarmas,
            TiaPlcService tiaPlcService,
            bool forSync = false,
            bool checkReales = true,
            bool checkEnteros = true,
            bool checkAlarmas = true)
        {
            if (process == null || tiaPlcService == null) return;

            // 1. Calcular Nombres
            TableName = $"{process.Id}_{process.Nombre}";
            ConstReal = $"{process.Id}{settings.SuffixConstReal}";
            ConstInt = $"{process.Id}{settings.SuffixConstInt}";
            ConstAlm = $"{process.Id}{settings.SuffixConstAlm}";
            ConstAlmHmi = $"{process.Id}{settings.SuffixConstAlmHmi}";

            DbNumReal = reales.FirstOrDefault()?.DbNumber ?? -1;
            DbNumInt = enteros.FirstOrDefault()?.DbNumber ?? -1;
            DbNumAlm = alarmas.FirstOrDefault()?.DbNumber ?? -1;

            DbNameReal = $"DB{DbNumReal}{settings.SuffixDbReal}";
            DbNameInt = $"DB{DbNumInt}{settings.SuffixDbInt}";
            DbNameAlm = $"DB{DbNumAlm}{settings.SuffixDbAlm}";

            // 2. Validación directa contra TIA Portal
            LogService.Write($"[PARAMS-ALARMS-ENVIRONMENT] Buscando tabla '{TableName}' y DBs asociados...");

            if (tiaPlcService.FindTagTableRecursively(TableName) == null)
            {
                StatusService.Set($"Error: No se encuentra la tabla '{TableName}' en el PLC.", StatusType.Error);
                return; // Se queda IsValid = false
            }

            bool doCheckReal = !forSync || checkReales;
            bool doCheckInt = !forSync || checkEnteros;
            bool doCheckAlm = !forSync || checkAlarmas;

            if (doCheckReal && DbNumReal != -1 && tiaPlcService.FindBlockByName(DbNameReal) == null)
            {
                StatusService.Set($"Error: No se encuentra el bloque '{DbNameReal}'.", StatusType.Error);
                return;
            }
            if (doCheckInt && DbNumInt != -1 && tiaPlcService.FindBlockByName(DbNameInt) == null)
            {
                StatusService.Set($"Error: No se encuentra el bloque '{DbNameInt}'.", StatusType.Error);
                return;
            }
            if (doCheckAlm && DbNumAlm != -1 && tiaPlcService.FindBlockByName(DbNameAlm) == null)
            {
                StatusService.Set($"Error: No se encuentra el bloque '{DbNameAlm}'.", StatusType.Error);
                return;
            }

            // Si sobrevive a los checks, es válido
            IsValid = true;
        }
    }
}