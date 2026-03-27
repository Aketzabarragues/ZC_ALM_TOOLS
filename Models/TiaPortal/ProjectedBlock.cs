using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    public class ProjectedBlock : ObservableObject
    {
        public string Tipo { get; set; }
        public int NumeroProyectado { get; set; }
        public string NombreProyectado { get; set; }
        public string ArchivoOrigen { get; set; }

        private SynchronizationStatus _estado = SynchronizationStatus.Pending;
        public SynchronizationStatus Estado
        {
            get => _estado;
            set { _estado = value; OnPropertyChanged(); }
        }

        private string _mensaje = "Esperando comprobación...";
        public string Mensaje
        {
            get => _mensaje;
            set { _mensaje = value; OnPropertyChanged(); }
        }
    }
}
