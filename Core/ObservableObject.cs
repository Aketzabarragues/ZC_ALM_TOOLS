using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZC_ALM_TOOLS.Core
{
    // Clase base que implementa la notificación de cambios para la UI
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // Notifica a la interfaz que una propiedad ha cambiado su valor
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}