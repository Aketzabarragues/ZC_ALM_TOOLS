using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{

    public class ConfigGlobalSettings
    {
        public string ExtractorExePath { get; set; }

        public static ConfigGlobalSettings FromXml(XElement x) => new ConfigGlobalSettings
        {
            ExtractorExePath = DataHelper.GetXmlVal(x, "ExtractorExePath")           
        };
    }

}
