using System;
using System.Text.RegularExpressions;
using System.Windows;

namespace ZC_ALM_TOOLS.Services.Common
{

    public enum StatusType { Ok, Warning, Error }


    // ==================================================================================================================
    /// <summary>
    /// Servicio encargado de gestionar el estado del statusbar de la ventana principal
    /// </summary>
    public static class StatusService
    {

        // Evento para que el MainViewModel sepa que el mensaje ha cambiado
        public static event Action<string, StatusType> OnStatusChanged;
        // Evento para que el MainViewModel sepa que esta Busy
        public static event Action<bool> OnBusyChanged;



        // ==================================================================================================================
        /// <summary>
        /// Muestra un mensaje limpio en el statusbar y guarda el formato crudo (con tags) en el log
        /// </summary>
        public static void Set(string message, StatusType type, bool writeToLog = true)
        {
            // 1. Guardamos el mensaje COMPLETO en el log (ej: "[ViewModel] [Metodo] Error X")
            if (writeToLog)
            {
                bool isError = type == StatusType.Error;
                //LogService.Write(message, isError);
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
        /// Activa o desactiva la barra de progreso de forma segura (Thread-Safe)
        /// </summary>
        public static void SetBusy(bool busy)
        {
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