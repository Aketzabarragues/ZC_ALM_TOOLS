using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Vci
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo de datos observable que representa una fila individual en la tabla de mapeo del VCI.
    /// Encapsula la información de un bloque de TIA Portal, su estado de vinculación con el espacio de trabajo 
    /// (Workspace) en disco, y gestiona la lógica visual (iconos y permisos de selección) para la interfaz gráfica.
    /// </summary>
    public class VciMappingAction : ObservableObject
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(); // Aquí usamos tu método real
            }
        }

        public string BlockName { get; set; }
        public string BlockType { get; set; }
        public VciMatchState State { get; set; }
        public string DiskPath { get; set; }


        /// <summary>
        /// Propiedad calculada que devuelve un icono visual (emoji) basado en el estado actual de coincidencia, 
        /// facilitando la lectura rápida en la interfaz de usuario.
        /// </summary>
        public string StateIcon
        {
            get
            {
                switch (State)
                {
                    case VciMatchState.YaEnlazado: return "🔵";
                    case VciMatchState.ListoParaEnlazar: return "🟢";
                    case VciMatchState.FaltaExportar: return "🟡";
                    case VciMatchState.Conflicto: return "🔴";
                    default: return "❓";
                }
            }
        }

        public bool IsSelectable => State == VciMatchState.ListoParaEnlazar;
    }
}