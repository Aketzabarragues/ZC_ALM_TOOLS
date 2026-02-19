using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class ConfigProcessSettings
    {
        public string ProcessXml { get; set; }
        public string PRealXml { get; set; }
        public string PIntXml { get; set; }
        public string AlarmXml { get; set; }


        public string ProcessName { get; set; }
        public string PRealName { get; set; }
        public string PIntName { get; set; }
        public string AlarmName { get; set; }

        public static ConfigProcessSettings FromXml(XElement x) => new ConfigProcessSettings
        {
            ProcessXml = DataHelper.GetXmlVal(x, "ProcessXml"),
            PRealXml = DataHelper.GetXmlVal(x, "PRealXml"),
            PIntXml = DataHelper.GetXmlVal(x, "PIntXml"),
            AlarmXml = DataHelper.GetXmlVal(x, "AlarmXml"),

            ProcessName = x.Element("ProcessXml")?.Attribute("Name")?.Value ?? "Procesos",
            PRealName = x.Element("PRealXml")?.Attribute("Name")?.Value ?? "P_Real",
            PIntName = x.Element("PIntXml")?.Attribute("Name")?.Value ?? "P_Int",
            AlarmName = x.Element("AlarmXml")?.Attribute("Name")?.Value ?? "Alarmas"
        };
    }
}