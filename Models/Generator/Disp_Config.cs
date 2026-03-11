using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa la configuracion de N_MAX de dispositivos
    /// </summary>
    public class Disp_Config
    {
        public string Nombre { get; set; }
        public int Valor { get; set; }


        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer N_MAX de dispositivos desde XML
        /// </summary>
        public static Disp_Config FromXml(XElement x) => new Disp_Config
        {
            Nombre = DataHelper.GetXmlVal(x, "Name"),
            Valor = DataHelper.GetXmlInt(x, "Value")
        };
    }
}