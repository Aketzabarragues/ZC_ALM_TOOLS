using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Core
{
    // ==================================================================================================================
    /// <summary>
    /// Clase estática de utilidad (Helper) que proporciona métodos seguros para la extracción 
    /// y conversión de valores desde nodos XML (XElement), gestionando valores nulos o formatos no deseados.
    /// </summary>
    public static class DataHelper
    {



        /// <summary>
        /// Extrae el valor de texto de un nodo XML hijo especificado por su nombre.
        /// Retorna un valor por defecto si el nodo padre es nulo, si el nodo hijo no existe o si está vacío.
        /// </summary>
        public static string GetXmlVal(XElement el, string name, string def = "")
        {
            if (el == null) return def;
            var node = el.Element(name);
            return (node == null || string.IsNullOrEmpty(node.Value)) ? def : node.Value.Trim();
        }



        /// <summary>
        /// Extrae y convierte el valor de un nodo XML a un número entero.
        /// Incluye una limpieza de formato específica para ignorar decimales espurios (ej. ".0") 
        /// generados durante la exportación de DataFrames desde scripts de Pandas (Python).
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