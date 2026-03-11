using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Core
{
    // ==================================================================================================================
    /// <summary>
    /// Clase para lectura de valores de XML
    /// </summary>
    public static class DataHelper
    {



        // ==================================================================================================================
        /// <summary>
        /// Metodo para obtener el valor de un nodo XML
        /// </summary>
        public static string GetXmlVal(XElement el, string name, string def = "")
        {
            if (el == null) return def;
            var node = el.Element(name);
            return (node == null || string.IsNullOrEmpty(node.Value)) ? def : node.Value.Trim();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para obtener un entero de un nodo XML, limpiando decimales de Pandas (.0)
        /// </summary>
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