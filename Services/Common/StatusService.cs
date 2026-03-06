using System;





namespace ZC_ALM_TOOLS.Services.Common
{

    public enum StatusType { Ok, Warning, Error }


    public static class StatusService
    {



        // ==================================================================================================================
        // Evento para que el MainViewModel sepa que el mensaje ha cambiado
        public static event Action<string, StatusType> OnStatusChanged;
        // Evento para que el MainViewModel sepa que esta Busy
        public static event Action<bool> OnBusyChanged;

        public static void Set(string message, StatusType type)
        {
            OnStatusChanged?.Invoke(message, type);
        }

        public static void SetBusy(bool busy)
        {
            OnBusyChanged?.Invoke(busy);
        }

    }

}