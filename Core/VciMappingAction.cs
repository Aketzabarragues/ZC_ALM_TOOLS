using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Vci
{
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

        // Propiedad calculada con Switch compatible con C# 7.3
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