using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa etapa de un proceso
    /// </summary>
    public class ProcessStage : ObservableObject
    {

        // ==================================================================================================================
        // Propiedades de identificación y Excel    
        public string Uid { get; set; }
        public int ProcessUid { get; set; }
        public int Numero { get; set; }
        public string Proceso { get; set; }
        public int ValorEtapa { get; set; }
        public string Descripcion { get; set; }
        public string NombreVariable { get; set; }
        public string CpTag { get; set; }
        public string CpComentario { get; set; }


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
