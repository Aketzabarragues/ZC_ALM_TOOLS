using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
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
                if (!System.IO.File.Exists(AppConfigService.AppConfigFile)) return;

                XDocument doc = XDocument.Load(AppConfigService.AppConfigFile);
                XElement root = doc.Root;

                GlobalSettings = ConfigGlobalSettings.FromXml(root.Element("GlobalSettings"));
                DeviceSettings = ConfigDeviceSettings.FromXml(root.Element("DeviceSettings"));
                ProcessSettings = ConfigProcessSettings.FromXml(root.Element("ProcessSettings"));

                var devicesList = root.Element("Devices")?.Elements("DeviceCategory")
                                      .Select(ConfigDeviceCategory.FromXml) ?? Enumerable.Empty<ConfigDeviceCategory>();

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
                // Reconstruimos el XML
                XDocument doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"));
                XElement root = new XElement("AppConfig");

                // Global Settings
                root.Add(new XElement("GlobalSettings",
                    new XElement("ExtractorExePath", GlobalSettings.ExtractorExePath),
                    new XElement("ProcessTemplatePath", GlobalSettings.ProcessTemplatePath),
                    new XElement("DocGeneratorExePath", GlobalSettings.DocGeneratorExePath),
                    new XElement("DocWordManualPath", GlobalSettings.DocWordManualPath),
                    new XElement("DocExportSourcesPath", GlobalSettings.DocExportSourcesPath),
                    new XElement("DocOutputPath", GlobalSettings.DocOutputPath)
                ));

                // Devices
                XElement devicesElement = new XElement("Devices");
                foreach (var d in Devices)
                {
                    devicesElement.Add(new XElement("DeviceCategory",
                        new XAttribute("Name", d.Name ?? ""),
                        new XElement("ExcelSheet", d.ExcelSheet),
                        new XElement("TiaGroup", d.TiaGroup),
                        new XElement("TiaTable", d.TiaTable),
                        new XElement("ModelClass", d.ModelClass),
                        new XElement("XmlFile", d.XmlFile),
                        new XElement("GlobalConfigKey", d.GlobalConfigKey),
                        new XElement("PlcCountConstant", d.PlcCountConstant),
                        new XElement("TiaDbName", d.TiaDbName),
                        new XElement("TiaDbArrayName", d.TiaDbArrayName)
                    ));
                }
                root.Add(devicesElement);

                // Device Settings
                root.Add(new XElement("DeviceSettings",
                    new XElement("ConfigTableName", DeviceSettings.ConfigTableName),
                    new XElement("DeviceDataConfigXml", new XAttribute("Name", DeviceSettings.Disp_N_Max), DeviceSettings.DeviceDataConfigXml)
                ));

                // Process Settings (simplificado, mapea el resto según sea necesario)
                root.Add(new XElement("ProcessSettings",
                    new XElement("ProcessXml", new XAttribute("Name", ProcessSettings.ProcessName), ProcessSettings.ProcessXml),
                    new XElement("PRealXml", new XAttribute("Name", ProcessSettings.PRealName), ProcessSettings.PRealXml),
                    new XElement("PIntXml", new XAttribute("Name", ProcessSettings.PIntName), ProcessSettings.PIntXml),
                    new XElement("AlarmXml", new XAttribute("Name", ProcessSettings.AlarmName), ProcessSettings.AlarmXml),
                    new XElement("StageXml", new XAttribute("Name", ProcessSettings.StageName), ProcessSettings.StageXml),
                    new XElement("SuffixConstReal", ProcessSettings.SuffixConstReal),
                    new XElement("SuffixConstInt", ProcessSettings.SuffixConstInt),
                    new XElement("SuffixConstAlm", ProcessSettings.SuffixConstAlm),
                    new XElement("SuffixConstAlmHmi", ProcessSettings.SuffixConstAlmHmi),
                    new XElement("SuffixDbReal", ProcessSettings.SuffixDbReal),
                    new XElement("SuffixDbInt", ProcessSettings.SuffixDbInt),
                    new XElement("SuffixDbAlm", ProcessSettings.SuffixDbAlm)
                ));

                doc.Add(root);
                doc.Save(AppConfigService.AppConfigFile);

                StatusService.Set("Ajustes guardados correctamente.", StatusType.Ok);
                LogService.Write("[SETTINGS] El archivo app_config.xml ha sido actualizado con éxito.");

                // Opcional: Aquí podrías llamar a AppConfigService.Reload() si lo tienes, para que el resto de la app se entere del cambio.
            }
            catch (Exception ex)
            {
                LogService.Write($"[SETTINGS] Error guardando configuración: {ex.Message}", true);
                StatusService.Set("Error al guardar los ajustes.", StatusType.Error);
            }
        }
    }
}
