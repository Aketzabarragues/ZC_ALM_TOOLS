using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una salida analogica
    /// </summary>
    public class Disp_SA : ObservableObject, IDevice
    {
        // ==================================================================================================================
        // Propiedades de Excel  
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Tag { get; set; }
        public string Descripcion { get; set; }
        public string FAT { get; set; }
        public string SByte { get; set; }
        public string Unidades { get; set; }
        public string RII { get; set; }
        public string RSI { get; set; }
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