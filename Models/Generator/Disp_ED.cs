using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una entrada digital
    /// </summary>
    public class Disp_ED : ObservableObject, IDevice
    {
        // ==================================================================================================================
        // Propiedades de Excel   
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Tag { get; set; }
        public string Descripcion { get; set; }
        public string FAT { get; set; }
        public string EByte { get; set; }
        public string EBit { get; set; }
        public string GrAlarma { get; set; }
        public string Cuadro { get; set; }
        public string Observaciones { get; set; }
        public string CPTag { get; set; }
        public string CPComentario { get; set; }

        // ==================================================================================================================
        // Propiedades de estado
        private string _Estado = "Sin comprobar";
        public string Estado { get => _Estado; set { _Estado = value; OnPropertyChanged(); } }

        
    }

}