using System;
using System.Text.RegularExpressions;
using System.Windows;

namespace ZC_ALM_TOOLS.Services.Common
{
    /// <summary>
    /// Implementación inyectable del Status. Conecta dinámicamente sus eventos a los 
    /// eventos estáticos antiguos para que toda la app reaccione unificada.
    /// </summary>
    public class AppStatusService : IStatusService
    {
        private readonly ILogService _logService;

        public event Action<string, StatusType> OnStatusChanged;
        public event Action<bool> OnBusyChanged;

        // Inyectamos el log aquí para cuando writeToLog sea true
        public AppStatusService(ILogService logService)
        {
            _logService = logService;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para actualizar el estado de la aplicación, con un mensaje y un tipo (Ok, Warning, Error). Opcionalmente, también puede escribir el mensaje completo en el log.
        /// </summary>
        public void Set(string message, StatusType type, bool writeToLog = true)
        {
            // 1. Guardamos el mensaje COMPLETO en el log usando el servicio inyectado
            if (writeToLog)
            {
                bool isError = type == StatusType.Error;
                _logService.Write(message, isError);
            }

            // 2. Limpiamos el mensaje para la UI (Elimina cualquier prefijo tipo "[Texto] ")
            string cleanUiMessage = Regex.Replace(message, @"^(\[.*?\]\s*)+", "").Trim();

            // 3. Disparamos el evento forzando el hilo principal (UI Thread)
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnStatusChanged?.Invoke(cleanUiMessage, type)));
            }
            else
            {
                OnStatusChanged?.Invoke(cleanUiMessage, type);
            }
        }




        // ==================================================================================================================
        /// <summary>
        /// Metodo para indicar que la aplicación está ocupada o no, lo que puede ser útil para mostrar un spinner o deshabilitar controles durante operaciones largas.
        /// </summary>
        public void SetBusy(bool busy)
        {
            // Forzamos también el hilo principal para el IsBusy (recomendado en WPF)
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnBusyChanged?.Invoke(busy)));
            }
            else
            {
                OnBusyChanged?.Invoke(busy);
            }
        }


    }
}