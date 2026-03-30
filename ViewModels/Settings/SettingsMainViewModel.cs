using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Newtonsoft.Json;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.ViewModels.Settings
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel que gestiona la pestaña principal de configuración, donde se editan los ajustes globales, de dispositivos y procesos.
    /// </summary>
    public class SettingsMainViewModel : ObservableObject
    {
        // Configuración estructurada en secciones para facilitar la edición y el binding en la UI
        private ConfigGlobalSettings _globalSettings;
        public ConfigGlobalSettings GlobalSettings { get => _globalSettings; set { _globalSettings = value; OnPropertyChanged(); } }

        private ConfigDeviceSettings _deviceSettings;
        public ConfigDeviceSettings DeviceSettings { get => _deviceSettings; set { _deviceSettings = value; OnPropertyChanged(); } }

        private ConfigProcessSettings _processSettings;
        public ConfigProcessSettings ProcessSettings { get => _processSettings; set { _processSettings = value; OnPropertyChanged(); } }

        // Propiedad para la lista de categorías de dispositivos, que se muestra en un DataGrid editable
        private ObservableCollection<ConfigDeviceCategory> _devices;
        public ObservableCollection<ConfigDeviceCategory> Devices { get => _devices; set { _devices = value; OnPropertyChanged(); } }

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;
        private readonly IAppConfigService _appConfigService;

        // Comandos
        public AsyncRelayCommand SaveCommand { get; }



        // ==================================================================================================================
        /// <summary>
        /// Constructor: Carga la configuración inicial desde el servicio y asigna el comando de guardado asíncrono.
        /// </summary>
        public SettingsMainViewModel(ILogService logService, IStatusService statusService, IAppConfigService appConfigService)
        {
            _logService = logService;
            _statusService = statusService;
            _appConfigService = appConfigService;
            SaveCommand = new AsyncRelayCommand(ExecuteSave);
            LoadSettings();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo que carga la configuración desde el AppConfigService al iniciar la ViewModel. Se apoya en el servicio que ya parsea el JSON al arrancar, 
        /// por lo que aquí solo asignamos las propiedades para el binding.
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // Se apoyará en el servicio que ya lee y parsea el JSON al arrancar
                GlobalSettings = _appConfigService.GetGlobalSettings() ?? new ConfigGlobalSettings();
                DeviceSettings = _appConfigService.GetDeviceSettings() ?? new ConfigDeviceSettings();
                ProcessSettings = _appConfigService.GetProcessConfig() ?? new ConfigProcessSettings();

                var devicesList = _appConfigService.GetDeviceCategories() ?? Enumerable.Empty<ConfigDeviceCategory>();
                Devices = new ObservableCollection<ConfigDeviceCategory>(devicesList);
            }
            catch (Exception ex)
            {
                _logService.Write($"[SETTINGS-VM] [LoadSettings] Error cargando configuración: {ex.Message}", true);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo que se ejecuta al guardar los ajustes. Construye un objeto completo con toda la configuración (incluyendo las partes que no se editan en esta View), 
        /// lo serializa a JSON formateado y lo guarda en el archivo app_config.json de forma asíncrona para no bloquear la UI.
        /// </summary>
        private async Task ExecuteSave()
        {
            try
            {
                // Construimos el root completo estructurado para no machacar los nodos que 
                // no editamos directamente en esta pantalla (Network, PReal, etc.)
                var fullConfig = new
                {
                    GlobalSettings = this.GlobalSettings,
                    DeviceSettings = this.DeviceSettings,
                    Devices = this.Devices.ToList(),
                    ProcessSettings = this.ProcessSettings,

                    // Mantenemos intactos los que no se gestionan desde esta View
                    PRealSettings = _appConfigService.GetPRealConfig(),
                    PIntSettings = _appConfigService.GetPIntConfig(),
                    AlarmSettings = _appConfigService.GetAlarmConfig(),
                    NetworkSettings = _appConfigService.GetNetworkConfig()
                };

                // Enviamos la serialización pesada y la escritura física a un hilo en segundo plano
                await Task.Run(() =>
                {
                    // Serializar a JSON formateado (Indented) para que sea legible
                    string jsonOutput = JsonConvert.SerializeObject(fullConfig, Formatting.Indented);

                    // Guardar 
                    File.WriteAllText(AppConfigService.AppConfigFile, jsonOutput);
                });

                // Unificamos el log y el mensaje visual
                _statusService.Set("[SETTINGS-VM] [ExecuteSave] Ajustes guardados correctamente y archivo app_config.json actualizado con éxito.", StatusType.Ok);

                // OPCIONAL: Si AppConfigService tiene un método de recarga, llámalo aquí
                _appConfigService.Reload();
            }
            catch (Exception ex)
            {
                _statusService.Set($"[SETTINGS-VM] [ExecuteSave] Error al guardar los ajustes: {ex.Message}", StatusType.Error);
            }
        }
    }
}