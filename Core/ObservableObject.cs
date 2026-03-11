using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZC_ALM_TOOLS.Core
{

    // ==================================================================================================================
    /// <summary>
    /// Clase base que implementa la notificación de cambios para la UI
    /// </summary>
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        // ==================================================================================================================
        /// <summary>
        /// Notifica a la interfaz que una propiedad ha cambiado su valor
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


    }
}