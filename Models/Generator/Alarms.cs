using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa alarmas
    /// </summary>
    public class Alarms : ObservableObject
    {

        // ==================================================================================================================
        // Propiedades de identificación y Excel   
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Proceso { get; set; }
        public int DbNumber { get; set; }
        public string Descripcion { get; set; }
        public string ComentarioDB { get; set; }


        // ==================================================================================================================
        // Propiedades de estado
        private string _estado = "Pendiente";
        public string Estado
        {
            get => _estado;
            set { _estado = value; OnPropertyChanged(); }
        }
                

    }
}
