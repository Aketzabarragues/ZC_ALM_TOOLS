using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Newtonsoft.Json;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Models.Common;

namespace ZC_ALM_TOOLS.Services.Common
{
    // ==================================================================================================================
    /// <summary>
    /// Servicio encargado de la configuracion de la aplicacion
    /// </summary>
    public class AppConfigService : IAppConfigService
    {



        private AppSettings _appConfigCache;

        // ==================================================================================================================
        // Rutas base centralizadas
        public static string BasePath => Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");
        public static string LogFile => Path.Combine(BasePath, "app_debug.log");
        public static string ExportPath => Path.Combine(BasePath, "Export");
        public static string TempPath => Path.Combine(BasePath, "Temp");
        public static string TempExportPathXml => Path.Combine(TempPath, "Xml");
        public static string TempExportPathVci => Path.Combine(TempPath, "Vci");
        public static string TempExportPathNewProcess => Path.Combine(TempPath, "GeneratedProcess");
        public static string AppConfigFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_config.json");

        private readonly ILogService _logService;


        public AppConfigService(ILogService logService)
        {
            _logService = logService;
        }


        // ==================================================================================================================
        /// <summary>
        /// Prepara el entorno de carpetas base y garantiza la existencia/carga del app_config.json
        /// </summary>
        public void InitializeEnvironment()
        {
            try
            {
                //  Si el directorio base ya existe, intentamos eliminarlo para partir de cero (limpieza de logs, temp, etc). Si falla, lo ignoramos y seguimos adelante para no bloquear el arranque de la app.
                if (Directory.Exists(BasePath))
                {
                    try
                    {
                        Directory.Delete(BasePath, true);
                    }
                    catch (Exception)
                    {
                    }
                }


                // Crear árbol de directorios efímeros
                if (!Directory.Exists(BasePath)) Directory.CreateDirectory(BasePath);
                if (!Directory.Exists(ExportPath)) Directory.CreateDirectory(ExportPath);
                if (!Directory.Exists(TempPath)) Directory.CreateDirectory(TempPath);
                if (!Directory.Exists(TempExportPathXml)) Directory.CreateDirectory(TempExportPathXml);
                if (!Directory.Exists(TempExportPathVci)) Directory.CreateDirectory(TempExportPathVci);
                if (!Directory.Exists(TempExportPathNewProcess)) Directory.CreateDirectory(TempExportPathNewProcess);

                // Comprobar si existe el archivo JSON en la ruta de ejecución
                if (!File.Exists(AppConfigFile))
                {
                    _logService.Write("[APP-CONFIG] [InitializeEnvironment] app_config.json no encontrado. Procediendo a extraerlo de los recursos...");
                    CreateAppConfigFile(AppConfigFile);
                }

                // 3. Cargar en memoria
                LoadConfigToMemory();
            }
            catch (Exception ex)
            {
                _logService.Write($"[APP-CONFIG] [InitializeEnvironment] Error inicializando entorno: {ex.Message}", true);
                throw;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Crea el archivo de configuración extrayéndolo de los recursos embebidos (.dll / .exe)
        /// </summary>
        private void CreateAppConfigFile(string targetPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = "ZC_ALM_TOOLS.Resources.app_config.json";

            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        string availableResources = string.Join(", ", assembly.GetManifestResourceNames());
                        throw new FileNotFoundException($"No se encontró el recurso '{resourceName}'. Recursos detectados: {availableResources}");
                    }

                    using (FileStream fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.CopyTo(fileStream);
                    }
                }
                _logService.Write("[APP-CONFIG] [CreateAppConfigFile] Configuración JSON maestra extraída y creada correctamente.");
            }
            catch (UnauthorizedAccessException)
            {
                _logService.Write($"[APP-CONFIG] [CreateAppConfigFile] Error. No se puede escribir en {targetPath}.", true);
                throw;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Método privado centralizado para leer el archivo físico y volcarlo a la caché de RAM
        /// </summary>
        private void LoadConfigToMemory()
        {
            if (!File.Exists(AppConfigFile)) return;

            string json = File.ReadAllText(AppConfigFile);
            _appConfigCache = JsonConvert.DeserializeObject<AppSettings>(json);
            _logService.Write("[APP-CONFIG] [LoadConfigToMemory] Configuración JSON cargada en memoria correctamente.");
        }



        // ==================================================================================================================
        /// <summary>
        /// Recarga la configuración desde el archivo JSON a la memoria RAM (Caché).
        /// </summary>
        public void Reload()
        {
            try
            {
                if (File.Exists(AppConfigFile))
                {
                    string json = File.ReadAllText(AppConfigFile);
                    _appConfigCache = JsonConvert.DeserializeObject<AppSettings>(json);
                    _logService.Write("[APP-CONFIG] [Reload] Configuración JSON recargada en memoria correctamente.");
                }
            }
            catch (Exception ex)
            {
                _logService.Write($"[APP-CONFIG] [Reload] ERROR recargando JSON: {ex.Message}", true);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la configuracion global de la aplicación desde la caché en memoria. Si no está cargada, devuelve una instancia vacía para evitar nulls.
        /// </summary>
        public ConfigGlobalSettings GetGlobalSettings() => _appConfigCache?.GlobalSettings ?? new ConfigGlobalSettings();



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la configuracion de dispositivos desde la caché en memoria. Si no está cargada, devuelve una instancia vacía para evitar nulls.
        /// </summary>
        public ConfigDeviceSettings GetDeviceSettings() => _appConfigCache?.DeviceSettings ?? new ConfigDeviceSettings();



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la lista de categorías de dispositivos desde la caché en memoria. Si no está cargada, devuelve una lista vacía para evitar nulls.
        /// </summary>
        public List<ConfigDeviceCategory> GetDeviceCategories() => _appConfigCache?.Devices ?? new List<ConfigDeviceCategory>();



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la configuracion de procesos desde la caché en memoria. Si no está cargada, devuelve una instancia vacía para evitar nulls.
        /// </summary>
        public ConfigProcessSettings GetProcessConfig() => _appConfigCache?.ProcessSettings ?? new ConfigProcessSettings();



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la configuracion de red desde la caché en memoria. Si no está cargada, devuelve una instancia vacía para evitar nulls.
        /// </summary>
        public ConfigNetworkSettings GetNetworkConfig() => _appConfigCache?.NetworkSettings ?? new ConfigNetworkSettings();



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la configuracion de PReal desde la caché en memoria. Si no está cargada, devuelve una instancia vacía para evitar nulls.
        /// </summary>
        public ConfigPRealSettings GetPRealConfig() => _appConfigCache?.PRealSettings ?? new ConfigPRealSettings();



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la configuracion de PInt desde la caché en memoria. Si no está cargada, devuelve una instancia vacía para evitar nulls.
        /// </summary>
        public ConfigPIntSettings GetPIntConfig() => _appConfigCache?.PIntSettings ?? new ConfigPIntSettings();



        // ==================================================================================================================
        /// <summary>
        /// Lectura de la configuracion de alarmas desde la caché en memoria. Si no está cargada, devuelve una instancia vacía para evitar nulls.
        /// </summary>
        public ConfigAlarmSettings GetAlarmConfig() => _appConfigCache?.AlarmSettings ?? new ConfigAlarmSettings();



    }
}