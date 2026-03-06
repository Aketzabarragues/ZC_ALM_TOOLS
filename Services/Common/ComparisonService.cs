using System;
using System.Collections.Generic;
using System.Linq;

namespace ZC_ALM_TOOLS.Services.Common
{
    // Esta clase guardará el resumen de la comparación y los fantasmas encontrados
    public class ComparisonResult<T>
    {
        public int MatchCount { get; set; } = 0;
        public int MismatchCount { get; set; } = 0;
        public int NewCount { get; set; } = 0;
        public int GhostCount { get; set; } = 0;
        public bool AllMatch { get; set; } = true;
        public List<T> Ghosts { get; set; } = new List<T>();
    }

    public static class ComparisonService
    {
        // Método genérico capaz de comparar Dispositivos, Parámetros o Alarmas
        public static ComparisonResult<T> Compare<T>(
            IEnumerable<T> excelItems,
            Dictionary<int, string> plcDict,
            Func<T, int> getId,                // Cómo obtener el ID
            Func<T, string> getExpectedText,   // Cómo obtener el texto esperado
            Func<T, string> getState,          // Cómo leer el estado actual
            Action<T, string> setState,        // Cómo escribir el nuevo estado
            Func<int, string, T> createGhost)  // Cómo crear un objeto "Fantasma"
        {
            var result = new ComparisonResult<T>();

            foreach (var item in excelItems)
            {
                int id = getId(item);
                string expectedText = getExpectedText(item);

                if (plcDict.TryGetValue(id, out string plcComment))
                {
                    if (plcComment == expectedText)
                    {
                        setState(item, "Sincronizado");
                        result.MatchCount++;
                    }
                    else
                    {
                        setState(item, $"{plcComment} -> {expectedText}");
                        result.AllMatch = false;
                        result.MismatchCount++;
                    }
                    plcDict.Remove(id);
                }
                else
                {
                    if (getState(item) != "Eliminar")
                    {
                        setState(item, "Nuevo");
                        result.AllMatch = false;
                        result.NewCount++;
                    }
                }
            }

            // Los sobrantes en plcDict son fantasmas
            if (plcDict.Count > 0)
            {
                result.AllMatch = false;
                result.GhostCount = plcDict.Count;
                foreach (var extra in plcDict)
                {
                    // Usamos la "fábrica" de fantasmas que nos pasen desde el ViewModel
                    var ghost = createGhost(extra.Key, extra.Value);
                    result.Ghosts.Add(ghost);
                }
            }

            return result;
        }
    }
}