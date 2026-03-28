using System;
using System.IO;

namespace ZC_ALM_TOOLS.Services.Common
{
    // ==================================================================================================================
    /// <summary>
    /// Servicio encargado de gestionar el log de la aplicacion
    /// </summary>
    public static class LogService
    {

        // Lock para sincronizar el acceso al archivo de log y evitar colisiones en escenarios multihilo
        private static readonly object _fileLock = new object();



        // ==================================================================================================================
        /// <summary>
        /// Metodo para escribir una línea en el archivo de log físico
        /// </summary>
        public static void Write(string message, bool isError = false)
        {
            string prefix = isError ? "[ERROR]" : "[INFO] ";
            string line = $"{DateTime.Now:HH:mm:ss} {prefix} {message}";

            try
            {
                // Sincronizamos el acceso al archivo para evitar colisiones en escenarios multihilo
                lock (_fileLock)
                {
                    File.AppendAllText(AppConfigService.LogFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Error silencioso si el archivo está bloqueado o no se puede escribir
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para borrar el archivo actual para empezar uno nuevo
        /// </summary>
        public static void Clear()
        {
            try
            {
                // Sincronizamos el acceso al archivo para evitar colisiones en escenarios multihilo
                lock (_fileLock)
                {
                    if (File.Exists(AppConfigService.LogFile))
                    {
                        File.Delete(AppConfigService.LogFile);
                    }
                }
            }
            catch
            {
                // Error silencioso si el archivo está bloqueado
            }

            Write("=== INICIO DE SESIÓN ===");
        }
    }
}