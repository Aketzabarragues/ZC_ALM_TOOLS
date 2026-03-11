using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una la configuracion de dispositivos
    /// </summary>
    public class ConfigDeviceSettings
    {
        public string ConfigTableName { get; set; }
        public string DeviceDataConfigXml { get; set; }
        public string Disp_N_Max { get; set; }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer la categoria de configuracion de dispositivo desde XML
        /// </summary>
        public static ConfigDeviceSettings FromXml(XElement x) => new ConfigDeviceSettings
        {
            ConfigTableName = DataHelper.GetXmlVal(x, "ConfigTableName"),
            DeviceDataConfigXml = DataHelper.GetXmlVal(x, "DeviceDataConfigXml"),
            Disp_N_Max = x.Element("DeviceDataConfigXml")?.Attribute("Name")?.Value ?? "Disp_N_Max"
        };
    }
}