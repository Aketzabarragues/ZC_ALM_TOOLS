using System;
using System.Windows.Input;

namespace ZC_ALM_TOOLS.Core
{



    // ==================================================================================================================
    /// <summary>
    /// Clase para gestionar los comandos de los botones desde el ViewModel
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
        /// Metodo para verificar si el comando puede ejecutarse en este momento
        /// </summary>
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();




        // ==================================================================================================================
        /// <summary>
        /// Metodo para ejecutar la accion vinculada al comando
        /// </summary>
        public void Execute(object parameter) => _execute();




        // ==================================================================================================================
        /// <summary>
        /// Metodo que se dispara cuando cambian las condiciones que afectan a si el comando puede ejecutarse
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}