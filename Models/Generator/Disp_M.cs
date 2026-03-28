using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa un motor
    /// </summary>
    public class Disp_M : ObservableObject, IDevice
    {
        // ==================================================================================================================
        // Propiedades de Excel  
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Tag { get; set; }
        public string Descripcion { get; set; }
        public string FAT { get; set; }
        public string SByte { get; set; }
        public string SBit { get; set; }
        public string RTByte { get; set; }
        public string RTBit { get; set; }
        public string RMByte { get; set; }
        public string RMBit { get; set; }
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