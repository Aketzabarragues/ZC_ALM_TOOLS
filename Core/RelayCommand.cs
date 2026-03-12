using System;
using System.Windows.Input;

namespace ZC_ALM_TOOLS.Core
{



    // ==================================================================================================================
    /// <summary>
    /// Implementación estándar de la interfaz ICommand para el patrón MVVM. 
    /// Permite enlazar (bind) acciones de la vista (WPF) directamente a métodos 
    /// definidos en el ViewModel, delegando en este la lógica de ejecución y validación.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }



        // ==================================================================================================================
        /// <summary>
        /// Determina si el comando puede ejecutarse en el estado actual. 
        /// WPF evalúa este método automáticamente para habilitar o deshabilitar controles visuales (ej. botones).
        /// </summary>
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();




        // ==================================================================================================================
        /// <summary>
        /// Ejecuta la acción (Action) principal encapsulada por el comando cuando el usuario interactúa con la interfaz.
        /// </summary>
        public void Execute(object parameter) => _execute();




        // ==================================================================================================================
        /// <summary>
        /// Evento que notifica a la interfaz gráfica que las condiciones de ejecución han cambiado.
        /// Se apoya en el CommandManager de WPF para reevaluar automáticamente el estado de los controles asociados.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}