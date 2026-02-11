using ZC_ALM_TOOLS.Core; // Necesario para ObservableObject

namespace ZC_ALM_TOOLS.Models
{
    public class DispositivoModel : ObservableObject
    {
        // --- DATOS FIJOS (Vienen del JSON/Excel) ---
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Descripcion { get; set; }
        public string Proceso { get; set; } // Opcional, por si agrupas por zonas

        // --- DATOS DINÁMICOS (Pueden cambiar durante la sincro) ---

        private string _tag;
        public string Tag // El Tag deseado (Excel)
        {
            get { return _tag; }
            set { _tag = value; OnPropertyChanged(); }
        }

        private string _tagTiaPortal;
        public string TagTiaPortal // El Tag que existe actualmente en el PLC
        {
            get { return _tagTiaPortal; }
            set { _tagTiaPortal = value; OnPropertyChanged(); }
        }

        private string _comentarioTiaPortal;
        public string ComentarioTiaPortal
        {
            get { return _comentarioTiaPortal; }
            set { _comentarioTiaPortal = value; OnPropertyChanged(); }
        }

        // --- ESTADO DE SINCRONIZACIÓN ---
        // Ejemplo: "OK", "Renombrar", "Nuevo", "Conflicto"
        private string _estado;
        public string Estado
        {
            get { return _estado; }
            set { _estado = value; OnPropertyChanged(); }
        }

        // Constructor para inicializar valores por defecto y evitar nulos
        public DispositivoModel()
        {
            Estado = "Pendiente";
            Tag = "";
            TagTiaPortal = "";
            Descripcion = "";
        }
    }
}