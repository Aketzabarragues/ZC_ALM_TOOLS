using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Siemens.Engineering;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{

    // ==================================================================================================================
    /// <summary>
    /// Servicio encargado de gestionar la conexión con Tia Portal, permitiendo buscar procesos abiertos y conectar con uno de ellos para acceder a su proyecto
    /// </summary>
    public static class TiaManager
    {
        public static Siemens.Engineering.TiaPortal Process { get; private set; }
        public static Project CurrentProject { get; private set; }


        // Método auxiliar para loguear antes o después de que exista la inyección
        private static void SafeLog(string message, bool isError = false)
        {
            var logger = App.ServiceProvider?.GetService<ILogService>();
            if (logger != null) logger.Write(message, isError);
            else System.Diagnostics.Debug.WriteLine((isError ? "[ERROR] " : "[INFO] ") + message);
        }


        // ==================================================================================================================
        /// <summary>
        /// Metodo para obtener la lista de procesos de Tia Portal abiertos en el sistema, se utiliza para mostrar esta lista al usuario y que pueda seleccionar a cual conectarse
        /// </summary>
        public static IList<TiaPortalProcess> GetAvailableProcesses()
        {
            try
            {
                return Siemens.Engineering.TiaPortal.GetProcesses().ToList();
            }
            catch (Exception ex)
            {
                SafeLog($"[TiaManager] [GetAvailableProcesses] Error buscando procesos de TIA Portal: {ex.Message}", true);
                return new List<TiaPortalProcess>();
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para conectar con una instancia de Tia Portal seleccionada por el usuario, se obtiene el proceso de Tia Portal y se accede al proyecto abierto en esa instancia
        /// </summary>
        public static bool Attach(TiaPortalProcess process)
        {
            try
            {
                Process = process.Attach();
                CurrentProject = Process.Projects.FirstOrDefault();
                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"[TiaManager] [Attach] Error al conectar (Attach) al proceso {process?.Id}: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metod7o para desconectar y liberar la conexión con Tia Portal, se llama al cerrar la aplicación para asegurarse de que no quedan conexiones abiertas ni recursos sin liberar
        /// </summary>
        public static void Dispose()
        {
            if (Process != null)
            {
                Process.Dispose();
                Process = null;
                SafeLog("[TiaManager] [Dispose] Conexión con TIA Portal cerrada y liberada.");
            }
        }

    }



}
