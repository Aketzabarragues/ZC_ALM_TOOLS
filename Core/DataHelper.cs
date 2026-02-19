using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Core
{
    public static class DataHelper
    {



        // ==================================================================================================================
        // Obtiene valor de un nodo XML
        public static string GetXmlVal(XElement el, string name, string def = "")
        {
            if (el == null) return def;
            var node = el.Element(name);
            return (node == null || string.IsNullOrEmpty(node.Value)) ? def : node.Value.Trim();
        }



        // ==================================================================================================================
        // Obtiene entero de un nodo XML, limpiando decimales de Pandas (.0)
        public static int GetXmlInt(XElement el, string name, int def = 0)
        {
            string val = GetXmlVal(el, name);
            if (string.IsNullOrEmpty(val)) return def;

            if (val.Contains("."))
            {
                val = val.Split('.')[0];
            }

            return int.TryParse(val, out int result) ? result : def;
        }
    }
}