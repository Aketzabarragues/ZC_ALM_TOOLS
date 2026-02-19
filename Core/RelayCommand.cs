using System;
using System.Windows.Input;

namespace ZC_ALM_TOOLS.Core
{



    // ==================================================================================================================
    // Clase para gestionar los comandos de los botones desde el ViewModel
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
        // Verifica si el comando puede ejecutarse en este momento
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();




        // ==================================================================================================================
        // Ejecuta la acción vinculada al comando
        public void Execute(object parameter) => _execute();




        // ==================================================================================================================
        // Se dispara cuando cambian las condiciones que afectan a si el comando puede ejecutarse
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}