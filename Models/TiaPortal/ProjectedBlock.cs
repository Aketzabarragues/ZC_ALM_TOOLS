using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa un bloque proyectado en TIA Portal, con información relevante para la sincronización y comparación con el bloque original.
    /// </summary>
    public class ProjectedBlock : ObservableObject
    {
        public string Type { get; set; }
        public int ProjectedNumber { get; set; }
        public string ProjectedName { get; set; }
        public string PlcGroupPath { get; set; }
        public string SourceFile { get; set; }
        public int OriginalNumber { get; set; }
        public string OriginalName { get; set; }
        public string AbsoluteSourcePath { get; set; }

        private SynchronizationStatus _status = SynchronizationStatus.Pending;
        public SynchronizationStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private string _message = "Pendiente de comprobar...";
        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }
    }
}
