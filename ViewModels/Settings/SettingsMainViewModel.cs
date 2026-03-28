using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Newtonsoft.Json;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.ViewModels.Settings
{
    public class SettingsMainViewModel : ObservableObject
    {
        // --- PROPIEDADES DE CONFIGURACIÓN ---
        private ConfigGlobalSettings _globalSettings;
        public ConfigGlobalSettings GlobalSettings { get => _globalSettings; set { _globalSettings = value; OnPropertyChanged(); } }

        private ConfigDeviceSettings _deviceSettings;
        public ConfigDeviceSettings DeviceSettings { get => _deviceSettings; set { _deviceSettings = value; OnPropertyChanged(); } }

        private ConfigProcessSettings _processSettings;
        public ConfigProcessSettings ProcessSettings { get => _processSettings; set { _processSettings = value; OnPropertyChanged(); } }

        // --- COLECCIÓN DE DISPOSITIVOS PARA EL DATAGRID ---
        private ObservableCollection<ConfigDeviceCategory> _devices;
        public ObservableCollection<ConfigDeviceCategory> Devices { get => _devices; set { _devices = value; OnPropertyChanged(); } }

        // --- COMANDOS ---
        public ICommand SaveCommand { get; }

        public SettingsMainViewModel()
        {
            SaveCommand = new RelayCommand(ExecuteSave);
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                // Se apoyará en el servicio que ya lee y parsea el JSON al arrancar
                GlobalSettings = AppConfigService.GetGlobalSettings() ?? new ConfigGlobalSettings();
                DeviceSettings = AppConfigService.GetDeviceSettings() ?? new ConfigDeviceSettings();
                ProcessSettings = AppConfigService.GetProcessConfig() ?? new ConfigProcessSettings();

                var devicesList = AppConfigService.GetDeviceCategories() ?? Enumerable.Empty<ConfigDeviceCategory>();
                Devices = new ObservableCollection<ConfigDeviceCategory>(devicesList);
            }
            catch (Exception ex)
            {
                LogService.Write($"[SETTINGS] Error cargando configuración: {ex.Message}", true);
            }
        }

        private void ExecuteSave()
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
                    PRealSettings = AppConfigService.GetPRealConfig(),
                    PIntSettings = AppConfigService.GetPIntConfig(),
                    AlarmSettings = AppConfigService.GetAlarmConfig(),
                    NetworkSettings = AppConfigService.GetNetworkConfig()
                };

                // Serializar a JSON formateado (Indented) para que sea legible
                string jsonOutput = JsonConvert.SerializeObject(fullConfig, Formatting.Indented);

                // Guardar (Asegúrate de que AppConfigService.AppConfigFile apunte a .json)
                File.WriteAllText(AppConfigService.AppConfigFile, jsonOutput);

                StatusService.Set("Ajustes guardados correctamente.", StatusType.Ok);
                LogService.Write("[SETTINGS] El archivo app_config.json ha sido actualizado con éxito.");

                // OPCIONAL: Si AppConfigService tiene un método de recarga, llámalo aquí
                AppConfigService.Reload(); 
            }
            catch (Exception ex)
            {
                LogService.Write($"[SETTINGS] Error guardando configuración: {ex.Message}", true);
                StatusService.Set("Error al guardar los ajustes.", StatusType.Error);
            }
        }
    }
}