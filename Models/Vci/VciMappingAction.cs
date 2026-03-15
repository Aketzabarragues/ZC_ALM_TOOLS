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
        public string DiskPath { get; set; }

        // ==================================================================================================================
        // Propiedades visuales calculadas

        private VciMatchState _state;
        public VciMatchState State
        {
            get => _state;
            set
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(StateColor));
            }
        }


        /// <summary>
        /// Devuelve el texto descriptivo indicando dónde existe el bloque actualmente.
        /// </summary>
        public string StateText
        {
            get
            {
                switch (State)
                {
                    case VciMatchState.YaEnlazado: return "En ambos (Mapeado)";
                    case VciMatchState.ListoParaEnlazar: return "En ambos (Falta mapear)";
                    case VciMatchState.FaltaExportar: return "Solo en PLC";
                    case VciMatchState.Conflicto: return "Solo en VCI";
                    case VciMatchState.ErrorAlEnlazar: return "Error al mapear (Requiere compilar)";
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
                    case VciMatchState.YaEnlazado: return "#007ACC";        // Azul
                    case VciMatchState.ListoParaEnlazar: return "#28A745";  // Verde
                    case VciMatchState.FaltaExportar: return "#FFC107";     // Amarillo
                    case VciMatchState.Conflicto: return "#DC3545";         // Rojo
                    case VciMatchState.ErrorAlEnlazar: return "#FD7E14";    // Naranja
                    default: return "#6C757D";                              // Gris
                }
            }
        }

        public bool IsSelectable => State == VciMatchState.ListoParaEnlazar || State == VciMatchState.YaEnlazado || State == VciMatchState.ErrorAlEnlazar;
    }
}