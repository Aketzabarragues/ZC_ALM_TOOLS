using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Xml.Linq;
using ZC_ALM_TOOLS.Models;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace ZC_ALM_TOOLS.Services
{
    public static class AppConfigManager
    {



        // ==================================================================================================================
        // Rutas base centralizadas
        public static string BasePath => Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");
        public static string LogFile => Path.Combine(BasePath, "app_debug.log");
        public static string ConfigPath => Path.Combine(BasePath, "Config");
        public static string ExportPath => Path.Combine(BasePath, "Export");
        public static string TempPath => Path.Combine(BasePath, "Temp");



        // ==================================================================================================================
        // Archivo de configuración
        public static string AppConfigFile => Path.Combine(ConfigPath, "app_config.xml");



        // ==================================================================================================================
        // Prepara el entorno de carpetas y archivos base
        public static void InitializeEnvironment()
        {
            // Crear directorios si no existen
            if (!Directory.Exists(ConfigPath)) Directory.CreateDirectory(ConfigPath);
            if (!Directory.Exists(ExportPath)) Directory.CreateDirectory(ExportPath);
            if (!Directory.Exists(TempPath)) Directory.CreateDirectory(TempPath);

            // Crear configuración general por defecto si no existe en la carpeta
            if (!File.Exists(AppConfigFile))
            {
                CreateAppConfigFile(AppConfigFile);
            }
        }



        // ==================================================================================================================
        // Crear el archivo de configuracion si no existe
        private static void CreateAppConfigFile(string targetPath)
        {
            // El nombre del recurso suele ser: NombreProyecto.NombreArchivo.xml
            // Puedes ver el nombre exacto usando: Assembly.GetExecutingAssembly().GetManifestResourceNames()
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = "ZC_ALM_TOOLS.Resources.app_config.xml";

            var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            foreach (var name in names) { LogService.Write("Recurso encontrado: " + name); }

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    LogService.Write("ERROR: No se encontró el recurso embebido " + resourceName, true);
                    return;
                }

                using (FileStream fileStream = new FileStream(targetPath, FileMode.Create))
                {
                    stream.CopyTo(fileStream);
                }
            }
            LogService.Write("Configuración maestra extraída correctamente de los recursos del Add-In.");
        }



        // ==================================================================================================================
        // Lectura de ajustes globales
        public static ConfigGlobalSettings GetGlobalSettings()
        {
            try
            {
                if (!File.Exists(AppConfigFile)) return new ConfigGlobalSettings();
                var doc = XDocument.Load(AppConfigFile);
                return ConfigGlobalSettings.FromXml(doc.Root?.Element("GlobalSettings"));
            }
            catch (Exception ex)
            {
                LogService.Write($"[CONFIG] Error en GetGlobalSettings: {ex.Message}", true);
                return new ConfigGlobalSettings(); // Devuelve objeto vacío para evitar nulos
            }
        }



        // ==================================================================================================================
        // Lectura de Ajuste de dispositivos
        public static ConfigDeviceSettings GetDeviceSettings()
        {
            try
            {
                if (!File.Exists(AppConfigFile)) return new ConfigDeviceSettings();
                var doc = XDocument.Load(AppConfigFile);
                return ConfigDeviceSettings.FromXml(doc.Root?.Element("DeviceSettings"));
            }
            catch (Exception ex)
            {
                LogService.Write($"[CONFIG] Error en GetDeviceSettings: {ex.Message}", true);
                return new ConfigDeviceSettings();
            }
        }



        // ==================================================================================================================
        // Lectura de caterogia de dispositvos (lista de tipos de dispositivos)
        public static List<ConfigDeviceCategory> GetDeviceCategories()
        {
            if (!File.Exists(AppConfigFile)) return new List<ConfigDeviceCategory>();

            try
            {
                var doc = XDocument.Load(AppConfigFile);

                // Buscamos todos los nodos <DeviceCategory> y dejamos que el modelo haga el trabajo
                return doc.Descendants("DeviceCategory")
                          .Select(x => ConfigDeviceCategory.FromXml(x))
                          .ToList();
            }
            catch (Exception ex)
            {
                LogService.Write($"[CONFIG] Error cargando categorías de dispositivos: {ex.Message}", true);
                return new List<ConfigDeviceCategory>();
            }
        }



        // ==================================================================================================================
        // Lectura de configuracion de procesos
        public static ConfigProcessSettings GetProcessConfig()
        {
            try
            {
                if (!File.Exists(AppConfigFile)) return new ConfigProcessSettings();
                var doc = XDocument.Load(AppConfigFile);
                return ConfigProcessSettings.FromXml(doc.Root?.Element("ProcessSettings"));
            }
            catch (Exception ex)
            {
                LogService.Write($"[CONFIG] Error en GetProcessConfig: {ex.Message}", true);
                return new ConfigProcessSettings();
            }
        }


    }
}