using System.Collections.Generic;
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
                OnPropertyChanged(nameof(IsSelectable));
            }
        }


        /// <summary>
        /// Devuelve el texto descriptivo indicando dónde existe el bloque actualmente.
        /// </summary>
        private static readonly Dictionary<VciMatchState, (string Text, string Color)> _stateMap = new Dictionary<VciMatchState, (string, string)>
        {
            { VciMatchState.YaEnlazado,       ("En ambos (Mapeado)", "#007ACC") },
            { VciMatchState.ListoParaEnlazar, ("En ambos (Falta mapear)", "#28A745") },
            { VciMatchState.FaltaExportar,    ("Solo en PLC", "#FFC107") },
            { VciMatchState.Conflicto,        ("Solo en VCI", "#DC3545") },
            { VciMatchState.ErrorAlEnlazar,   ("Error al mapear (Requiere compilar)", "#FD7E14") }
        };

        public string StateText => _stateMap.ContainsKey(State) ? _stateMap[State].Text : "Desconocido";
        public string StateColor => _stateMap.ContainsKey(State) ? _stateMap[State].Color : "#6C757D";

        public bool IsSelectable => State == VciMatchState.ListoParaEnlazar || State == VciMatchState.YaEnlazado || State == VciMatchState.ErrorAlEnlazar;
    }
}