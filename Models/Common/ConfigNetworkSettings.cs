using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Common
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una la configuracion de conexion
    /// </summary>
    public class ConfigNetworkSettings
    {
        public string ConnectionsXml { get; set; }
        public string ConnectionsName { get; set; }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer la configuracion de la aplicacion desde XML
        /// </summary>
        public static ConfigNetworkSettings FromXml(XElement x) => new ConfigNetworkSettings
        {
            ConnectionsXml = DataHelper.GetXmlVal(x, "ConnectionsXml"),
            ConnectionsName = x.Element("ConnectionsXml")?.Attribute("Name")?.Value ?? "Conexiones"
        };

    }
}
