using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una la configuracion global de la aplicacion
    /// </summary>
    public class ConfigGlobalSettings
    {
        public string ExtractorExePath { get; set; }
        public string ProcessTemplatePath { get; set; }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer la configuracion de la aplicacion desde XML
        /// </summary>
        public static ConfigGlobalSettings FromXml(XElement x) => new ConfigGlobalSettings
        {
            ExtractorExePath = DataHelper.GetXmlVal(x, "ExtractorExePath"),
            ProcessTemplatePath = DataHelper.GetXmlVal(x, "ProcessTemplatePath")
        };
    }

}
