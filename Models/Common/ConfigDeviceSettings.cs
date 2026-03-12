using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo de datos que almacena la configuración global para el módulo de generación de dispositivos.
    /// Define parámetros generales extraídos del app_config.xml, como el nombre de la tabla de constantes 
    /// maestra y las referencias a los archivos de configuración base.
    /// </summary>
    public class ConfigDeviceSettings
    {
        public string ConfigTableName { get; set; }
        public string DeviceDataConfigXml { get; set; }
        public string Disp_N_Max { get; set; }

        // ==================================================================================================================
        /// <summary>
        /// Método estático que construye e inicializa una instancia de ConfigDeviceSettings 
        /// a partir de un nodo XML (XElement) extraído del archivo de configuración de la aplicación (app_config.xml).
        /// </summary>
        public static ConfigDeviceSettings FromXml(XElement x) => new ConfigDeviceSettings
        {
            ConfigTableName = DataHelper.GetXmlVal(x, "ConfigTableName"),
            DeviceDataConfigXml = DataHelper.GetXmlVal(x, "DeviceDataConfigXml"),
            Disp_N_Max = x.Element("DeviceDataConfigXml")?.Attribute("Name")?.Value ?? "Disp_N_Max"
        };
    }
}