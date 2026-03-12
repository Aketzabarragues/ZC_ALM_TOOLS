using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZC_ALM_TOOLS.Core
{
    // ==================================================================================================================
    /// <summary>
    /// Clase base fundamental para el patrón MVVM. Implementa la interfaz INotifyPropertyChanged 
    /// para alertar automáticamente a la interfaz gráfica (WPF) cuando el valor de una propiedad 
    /// en los modelos o ViewModels ha sido modificado.
    /// </summary>
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        // ==================================================================================================================
        /// <summary>
        /// Dispara el evento PropertyChanged. Gracias al atributo [CallerMemberName], infiere 
        /// automáticamente el nombre de la propiedad que invoca este método, eliminando la 
        /// necesidad de pasar el nombre como una cadena de texto manual ("magic string").
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


    }
}