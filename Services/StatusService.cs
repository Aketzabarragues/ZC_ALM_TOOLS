using System;

public static class StatusService
{



    // ==================================================================================================================
    // Evento para que el MainViewModel sepa que el mensaje ha cambiado
    public static event Action<string, bool> OnStatusChanged;
    // Evento para que el MainViewModel sepa que esta Busy
    public static event Action<bool> OnBusyChanged;
    
    public static void Set(string message, bool isError = false)
    {
        OnStatusChanged?.Invoke(message, isError);
    }

    public static void SetBusy(bool busy)
    {
        OnBusyChanged?.Invoke(busy);
    }

}