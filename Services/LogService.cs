using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ZC_ALM_TOOLS.Services
{
    public static class LogService
    {

        public static ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();


        public static void Write(string mensaje, bool esError = false)
        {
            string prefijo = esError ? "[ERROR]" : "[INFO] ";
            string linea = $"{DateTime.Now:HH:mm:ss} {prefijo} {mensaje}";

            try
            {    
                // Escribe y añade una línea al archivo
                File.AppendAllText(AppConfigManager.LogFile, linea + Environment.NewLine);
            }
            catch
            {
                // Si falla el log, no queremos que pete la app
            }


            // 2. Actualización de la UI con seguridad total
            // TIA Portal Add-Ins a veces no tienen Application.Current inicializado
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            if (dispatcher != null)
            {
                if (dispatcher.CheckAccess())
                {
                    AddEntry(linea);
                }
                else
                {
                    dispatcher.BeginInvoke(new Action(() => AddEntry(linea)));
                }
            }

        }

        private static void AddEntry(string linea)
        {
            LogEntries.Add(linea);
            if (LogEntries.Count > 300) LogEntries.RemoveAt(0);
        }

        public static void Clear()
        {

            // Limpiamos la lista en el hilo correcto
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            dispatcher?.Invoke(() => LogEntries.Clear());

            if (File.Exists(AppConfigManager.LogFile))
                File.Delete(AppConfigManager.LogFile);

            Write("=== INICIO DE SESIÓN ===");
        }
    }
}