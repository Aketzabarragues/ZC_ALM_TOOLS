using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // Archivos de configuración y datos
        public static string SettingsFile => Path.Combine(ConfigPath, "main_settings.xml");
        public static string DeviceSettingsFile => Path.Combine(ConfigPath, "disp_settings.xml");
        public static string DeviceDataConfig => Path.Combine(ExportPath, "config_disp.xml");



        // ==================================================================================================================
        // Prepara el entorno de carpetas y archivos base
        public static void InitializeEnvironment()
        {
            // Crear directorios si no existen
            if (!Directory.Exists(ConfigPath)) Directory.CreateDirectory(ConfigPath);
            if (!Directory.Exists(ExportPath)) Directory.CreateDirectory(ExportPath);
            if (!Directory.Exists(TempPath)) Directory.CreateDirectory(TempPath);

            // Crear configuración general por defecto
            if (!File.Exists(SettingsFile))
            {
                var doc = new XDocument(new XElement("Settings",
                    new XElement("ExePath", @"C:\Program Files\Siemens\Automation\Portal V18\AddIns\ZC_ALM_TOOLS\ZC_Extractor.exe"),
                    new XElement("UltimoUsuario", Environment.UserName)
                ));
                doc.Save(SettingsFile);
            }

            // Crear configuración de dispositivos por defecto
            if (!File.Exists(DeviceSettingsFile))
            {
                CreateDefaultDeviceSettings();            
            }
        }



        // ==================================================================================================================
        // Lee el archivo XML y devuelve la lista de categorías configuradas
        public static List<DeviceCategory> GetDeviceCategories()
        {
            if (!File.Exists(DeviceSettingsFile)) return new List<DeviceCategory>();

            try
            {
                var doc = XDocument.Load(DeviceSettingsFile);
                return doc.Descendants("DeviceCategory").Select(x => new DeviceCategory
                {
                    Name = x.Attribute("Name")?.Value,
                    ExcelSheet = x.Element("ExcelSheet")?.Value,
                    TiaGroup = x.Element("TiaGroup")?.Value,
                    TiaTable = x.Element("TiaTable")?.Value,
                    ModelClass = x.Element("ModelClass")?.Value,
                    XmlFile = x.Element("XmlFile")?.Value,
                    GlobalConfigKey = x.Element("GlobalConfigKey")?.Value,
                    PlcCountConstant = x.Element("PlcCountConstant")?.Value,
                    TiaDbName = x.Element("TiaDbName")?.Value,
                    TiaDbArrayName = x.Element("TiaDbArrayName")?.Value
                }).ToList();
            }
            catch
            {
                return new List<DeviceCategory>();
            }
        }



        // ==================================================================================================================
        // Obtiene la ruta del ejecutor Python desde los settings
        public static string ReadExePath()
        {
            if (!File.Exists(SettingsFile)) return string.Empty;
            return XDocument.Load(SettingsFile).Root?.Element("ExePath")?.Value ?? string.Empty;
        }



        // ==================================================================================================================
        // Genera el archivo disp_settings.xml con los valores iniciales
        private static void CreateDefaultDeviceSettings()
        {
            var doc = new XDocument(new XElement("DeviceSettings",
                CreateCategoryElement("Entrada Digital", "DISP_ED", "Disp_ED", "disp_ed.xml", "Num_Disp_ED", "N_MAX_DISP_ED", "DB2000_ED", "ED"),
                CreateCategoryElement("Entrada Analogica", "DISP_EA", "Disp_EA", "disp_ea.xml", "Num_Disp_EA", "N_MAX_DISP_EA", "DB2001_EA", "EA"),
                CreateCategoryElement("Salida Analogica", "DISP_SA", "Disp_SA", "disp_sa.xml", "Num_Disp_SA", "N_MAX_DISP_SA", "DB2006_SA", "SA"),
                CreateCategoryElement("Válvula", "DISP_V", "Disp_V", "disp_v.xml", "Num_Disp_V", "N_MAX_DISP_V", "DB2010_V", "V"),
                CreateCategoryElement("Motor", "DISP_M", "Disp_M", "disp_m.xml", "Num_Disp_M", "N_MAX_DISP_M", "DB2015_M", "M"),
                CreateCategoryElement("Motor variador", "DISP_M_VF", "Disp_M_VF", "disp_m_vf.xml", "Num_Disp_M_VF", "N_MAX_DISP_M_VF", "DB2016_M_VF", "M_VF")
            ));
            doc.Save(DeviceSettingsFile);
        }



        // ==================================================================================================================
        // Helper para no repetir código al crear el XML por defecto
        private static XElement CreateCategoryElement(string name, string sheet, string model, string file, string configKey, string plcConst, string db, string array)
        {
            return new XElement("DeviceCategory", new XAttribute("Name", name),
                new XElement("ExcelSheet", sheet),
                new XElement("TiaGroup", "002_Dispositivos"),
                new XElement("TiaTable", "002_Disp_" + sheet.Replace("DISP_", "")),
                new XElement("ModelClass", model),
                new XElement("XmlFile", file),
                new XElement("GlobalConfigKey", configKey),
                new XElement("PlcCountConstant", plcConst),
                new XElement("TiaDbName", db),
                new XElement("TiaDbArrayName", array)
            );
        }

    }
}