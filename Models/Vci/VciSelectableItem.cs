using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa un elemento seleccionable de TIA Portal (Bloque o UDT) 
    /// para la exportación y generación de documentación web.
    /// </summary>
    public class VciSelectableItem : ObservableObject
    {

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // Referencia genérica al elemento original de la caché (CachedPlcBlock o CachedPlcType)
        public object OriginalItem { get; set; }
        public string Name { get; set; }
        public string SimpleType { get; set; }
        public string FolderPath { get; set; }

        private int _number;
        public int Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); }
        }

    }
}
