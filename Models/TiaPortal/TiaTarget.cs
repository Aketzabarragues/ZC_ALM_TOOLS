using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    public enum TargetType { PLC, HMI, SCADA }

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa un dispositivo objetivo (PLC, HMI o SCADA) encontrado en el proyecto.
    /// Encapsula la referencia nativa de Siemens (SoftwareObject) y permite su enlace dinámico (Binding) 
    /// con los controles de selección de la interfaz gráfica.
    /// </summary>
    public class TiaTarget : INotifyPropertyChanged
    {
        private bool _isChecked;
        public string Name { get; set; }
        public TargetType Type { get; set; }

        // La referencia real al objeto de software de Siemens
        public object SoftwareObject { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}