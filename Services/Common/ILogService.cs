namespace ZC_ALM_TOOLS.Services.Common
{
    /// <summary>
    /// Interfaz del servicio de log de la aplicación. Proporciona métodos para escribir mensajes de log y limpiar el archivo de log.
    /// </summary>
    public interface ILogService
    {
        void Write(string message, bool isError = false);
        void Clear();
    }
}