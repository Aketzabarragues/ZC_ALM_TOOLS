using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo de datos observable que representa una fila individual en la tabla de mapeo del VCI.
    /// Encapsula la información de un bloque de TIA Portal y gestiona la lógica visual para la interfaz gráfica.
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
                OnPropertyChanged();
            }
        }

        public string BlockName { get; set; }
        public string BlockType { get; set; }
        public VciMatchState State { get; set; }
        public string DiskPath { get; set; }

        // ==================================================================================================================
        // Propiedades visuales calculadas

        /// <summary>
        /// Devuelve el texto descriptivo indicando dónde existe el bloque actualmente.
        /// </summary>
        public string StateText
        {
            get
            {
                switch (State)
                {
                    case VciMatchState.YaEnlazado: return "En ambos (Enlazado)";
                    case VciMatchState.ListoParaEnlazar: return "En ambos (Falta enlazar)";
                    case VciMatchState.FaltaExportar: return "Solo en PLC";
                    case VciMatchState.Conflicto: return "Solo en VCI (Disco)";
                    default: return "Desconocido";
                }
            }
        }

        /// <summary>
        /// Devuelve el color hexadecimal asociado al estado (Rojo, Amarillo, Verde, Azul).
        /// </summary>
        public string StateColor
        {
            get
            {
                switch (State)
                {
                    case VciMatchState.YaEnlazado: return "#007ACC";        // Azul (OK)
                    case VciMatchState.ListoParaEnlazar: return "#28A745";  // Verde (Acción requerida)
                    case VciMatchState.FaltaExportar: return "#FFC107";     // Amarillo
                    case VciMatchState.Conflicto: return "#DC3545";         // Rojo
                    default: return "#6C757D";                              // Gris
                }
            }
        }

        public bool IsSelectable => State == VciMatchState.ListoParaEnlazar || State == VciMatchState.YaEnlazado;
    }
}