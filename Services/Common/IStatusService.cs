using System;

namespace ZC_ALM_TOOLS.Services.Common
{
    /// <summary>
    /// Enumeración para los tipos de estado que se pueden mostrar en la aplicación. Incluye Ok, Warning y Error.
    /// </summary>
    public enum StatusType { Ok, Warning, Error }

    /// <summary>
    /// Interfaz del servicio de estado de la aplicación. Proporciona eventos para notificar cambios en el estado y métodos para actualizar el mensaje de estado y el indicador de ocupado.
    /// </summary>
    public interface IStatusService
    {
        event Action<string, StatusType> OnStatusChanged;
        event Action<bool> OnBusyChanged;

        void Set(string message, StatusType type, bool writeToLog = true);
        void SetBusy(bool busy);
    }
}