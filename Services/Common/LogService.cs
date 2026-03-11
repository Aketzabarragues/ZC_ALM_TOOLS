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
                // Intentamos escribir en la ruta definida en el ConfigManager
                File.AppendAllText(AppConfigService.LogFile, line + Environment.NewLine);
            }
            catch
            {
                // Si falla la escritura no lanzamos excepción para no detener el flujo principal de la aplicación
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
                if (File.Exists(AppConfigService.LogFile))
                {
                    File.Delete(AppConfigService.LogFile);
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