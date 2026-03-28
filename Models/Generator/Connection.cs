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

    }
}