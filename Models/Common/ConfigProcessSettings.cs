using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una la configuracion de procesos
    /// </summary>
    public class ConfigProcessSettings
    {
        public string ProcessXml { get; set; }
        public string ProcessName { get; set; }
        public string PRealXml { get; set; }
        public string PRealName { get; set; }
        public string PIntXml { get; set; }
        public string PIntName { get; set; }
        public string AlarmXml { get; set; }
        public string AlarmName { get; set; }
        public string StageXml { get; set; }
        public string StageName { get; set; }


        public string SuffixConstReal { get; set; }
        public string SuffixConstInt { get; set; }
        public string SuffixConstAlm { get; set; }
        public string SuffixConstAlmHmi { get; set; }

        public string SuffixDbReal { get; set; }
        public string SuffixDbInt { get; set; }
        public string SuffixDbAlm { get; set; }


        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer la configuracion de procesos desde XML
        /// </summary>
        public static ConfigProcessSettings FromXml(XElement x) => new ConfigProcessSettings
        {
            ProcessXml = DataHelper.GetXmlVal(x, "ProcessXml"),
            PRealXml = DataHelper.GetXmlVal(x, "PRealXml"),
            PIntXml = DataHelper.GetXmlVal(x, "PIntXml"),
            AlarmXml = DataHelper.GetXmlVal(x, "AlarmXml"),
            StageXml = DataHelper.GetXmlVal(x, "StageXml"),

            ProcessName = x.Element("ProcessXml")?.Attribute("Name")?.Value ?? "Procesos",
            PRealName = x.Element("PRealXml")?.Attribute("Name")?.Value ?? "P_Real",
            PIntName = x.Element("PIntXml")?.Attribute("Name")?.Value ?? "P_Int",
            AlarmName = x.Element("AlarmXml")?.Attribute("Name")?.Value ?? "Alarmas",
            StageName = x.Element("StageXml")?.Attribute("Name")?.Value ?? "Etapas",

            SuffixConstReal = DataHelper.GetXmlVal(x, "SuffixConstReal"),
            SuffixConstInt = DataHelper.GetXmlVal(x, "SuffixConstInt"),
            SuffixConstAlm = DataHelper.GetXmlVal(x, "SuffixConstAlm"),
            SuffixConstAlmHmi = DataHelper.GetXmlVal(x, "SuffixConstAlmHmi"),

            SuffixDbReal = DataHelper.GetXmlVal(x, "SuffixDbReal"),
            SuffixDbInt = DataHelper.GetXmlVal(x, "SuffixDbInt"),
            SuffixDbAlm = DataHelper.GetXmlVal(x, "SuffixDbAlm")
        };
    }
}