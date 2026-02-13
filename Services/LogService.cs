using System;
using System.IO;

namespace ZC_ALM_TOOLS.Services
{
    public static class LogService
    {
        public static void Write(string mensaje, bool esError = false)
        {
            try
            {
                string prefijo = esError ? "[ERROR]" : "[INFO] ";
                string linea = $"{DateTime.Now:HH:mm:ss} {prefijo} {mensaje}";

                // Escribe y añade una línea al archivo
                File.AppendAllText(AppConfigManager.LogFile, linea + Environment.NewLine);
            }
            catch
            {
                // Si falla el log, no queremos que pete la app
            }
        }

        public static void Clear()
        {
            if (File.Exists(AppConfigManager.LogFile))
                File.Delete(AppConfigManager.LogFile);

            Write("=== INICIO DE SESIÓN ===");
        }
    }
}