using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que encapsula un Workspace VCI de TIA Portal para mostrarlo en la interfaz gráfica.
    /// </summary>
    public class VciWorkspaceModel : ObservableObject
    {
        public string Name { get; set; }
        public string Path { get; set; }

        // Referencia nativa al objeto VccWorkspace de Siemens
        public object SoftwareWorkspace { get; set; }

        public string DisplayText => $"{Name} ({Path})";

        public override string ToString() => Name;
    }
}