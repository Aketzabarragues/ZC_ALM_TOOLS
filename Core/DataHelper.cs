using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Core
{
    public static class DataHelper
    {
        public static string GetXmlVal(XElement el, string name, string def = "")
        {
            if (el == null) return def;
            var node = el.Element(name);
            // Si el nodo no existe o está vacío, devolvemos el valor por defecto
            return (node == null || string.IsNullOrEmpty(node.Value)) ? def : node.Value.Trim();
        }

        public static int GetXmlInt(XElement el, string name, int def = 0)
        {
            string val = GetXmlVal(el, name);
            if (string.IsNullOrEmpty(val)) return def;

            // Limpieza para Pandas: Si viene "30.0", quitamos el ".0" para que Parse no falle
            if (val.Contains("."))
            {
                val = val.Split('.')[0];
            }

            return int.TryParse(val, out int result) ? result : def;
        }

        // --- MÉTODOS ANTIGUOS (Para no romper nada mientras migras) ---
        public static string GetVal(string[] c, int idx) => (c != null && idx < c.Length) ? c[idx] : "";
        public static int ParseInt(string val) => int.TryParse(val, out int r) ? r : 0;
    }
}