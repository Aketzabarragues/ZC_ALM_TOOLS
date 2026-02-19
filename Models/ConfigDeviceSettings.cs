using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class ConfigDeviceSettings
    {
        public string ConfigTableName { get; set; }
        public string DeviceDataConfigXml { get; set; }
        public string Disp_N_Max { get; set; }
        public static ConfigDeviceSettings FromXml(XElement x) => new ConfigDeviceSettings
        {
            ConfigTableName = DataHelper.GetXmlVal(x, "ConfigTableName"),
            DeviceDataConfigXml = DataHelper.GetXmlVal(x, "DeviceDataConfigXml"),
            Disp_N_Max = x.Element("DeviceDataConfigXml")?.Attribute("Name")?.Value ?? "Disp_N_Max"
        };
    }
}