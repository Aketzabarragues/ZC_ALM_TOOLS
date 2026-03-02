using System.Xml.Linq;
using Siemens.Engineering.Safety;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{

    public class ConfigGlobalSettings
    {
        public string ExtractorExePath { get; set; }
        public string ProcessTemplatePath { get; set; }
        public static ConfigGlobalSettings FromXml(XElement x) => new ConfigGlobalSettings
        {
            ExtractorExePath = DataHelper.GetXmlVal(x, "ExtractorExePath"),
            ProcessTemplatePath = DataHelper.GetXmlVal(x, "ProcessTemplatePath")
        };
    }

}
