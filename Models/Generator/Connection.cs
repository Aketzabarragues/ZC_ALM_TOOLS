using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una conexión entre equipos (PLC - HMI, PLC - PLC, etc.)
    /// </summary>
    public class Connection : ObservableObject
    {

        // ==================================================================================================================
        // Propiedades extraídas del Excel / XML
        public string Equipo_Origen { get; set; }
        public string Equipo_Destino { get; set; }
        public string Protocolo { get; set; }
        public string Nombre_Conexion_TIA { get; set; }
        public string ID_Local { get; set; }
        public string Puerto_Local { get; set; }

        // ==================================================================================================================
        /// <summary>
        /// Instancia una Conexion leyendo directamente de un nodo <Conexion> del XML generado por Python
        /// </summary>
        public static Connection FromXml(XElement x) => new Connection
        {
            Equipo_Origen = DataHelper.GetXmlVal(x, "Equipo_Origen"),
            Equipo_Destino = DataHelper.GetXmlVal(x, "Equipo_Destino"),
            Protocolo = DataHelper.GetXmlVal(x, "Protocolo"),
            Nombre_Conexion_TIA = DataHelper.GetXmlVal(x, "Nombre_Conexion_TIA"),
            ID_Local = DataHelper.GetXmlVal(x, "ID_Local"),
            Puerto_Local = DataHelper.GetXmlVal(x, "Puerto_Local")
        };
    }
}