using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Core
{
    // ==================================================================================================================
    /// <summary>
    /// Implementación de ICommand para operaciones asíncronas.
    /// Bloquea automáticamente la ejecución concurrente y gestiona el estado global de IsBusy.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;



        // ==================================================================================================================
        /// <summary>
        /// Constructor que recibe la función asíncrona a ejecutar y una función opcional para determinar si el comando puede ejecutarse.
        /// </summary>
        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }




        // ==================================================================================================================
        /// <summary>
        /// Metodo de la interfaz ICommand que determina si el comando puede ejecutarse.
        /// </summary>
        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute == null || _canExecute());
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo de la interfaz ICommand que ejecuta la función asíncrona encapsulada por el comando.
        /// </summary>
        public async void Execute(object parameter)
        {
            await ExecuteAsync();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo asíncrono que ejecuta la función encapsulada por el comando, gestionando el estado de ejecución y el estado global de IsBusy.
        /// </summary>
        public async Task ExecuteAsync()
        {
            if (CanExecute(null))
            {
                try
                {
                    _isExecuting = true;
                    NotifyCanExecuteChanged();
                    App.ServiceProvider?.GetService<IStatusService>()?.SetBusy(true);

                    await _execute();
                }
                finally
                {
                    _isExecuting = false;
                    App.ServiceProvider?.GetService<IStatusService>()?.SetBusy(false);
                    NotifyCanExecuteChanged();
                }
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para notificar a la interfaz gráfica que las condiciones de ejecución han cambiado, forzando una reevaluación del estado de los controles asociados.
        /// </summary>
        public void NotifyCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }



        // ==================================================================================================================
        /// <summary>
        /// Evento que notifica a la interfaz gráfica que las condiciones de ejecución han cambiado.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}