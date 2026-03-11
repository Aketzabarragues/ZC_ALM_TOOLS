using System;

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
        /// Metodo para mostrar un mensaje en el statusbar 
        /// </summary>
        public static void Set(string message, StatusType type)
        {
            OnStatusChanged?.Invoke(message, type);
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para activar o desactivar la barra de progreso 
        /// </summary>
        public static void SetBusy(bool busy)
        {
            OnBusyChanged?.Invoke(busy);
        }

    }

}