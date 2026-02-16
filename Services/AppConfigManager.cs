using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ZC_ALM_TOOLS.Models;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace ZC_ALM_TOOLS.Services
{
    public static class AppConfigManager
    {
        // Rutas base centralizadas
        public static string BasePath => Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");
        public static string LogFile => Path.Combine(BasePath, "app_debug.log");
        public static string ConfigPath => Path.Combine(BasePath, "Config");
        public static string ExportPath => Path.Combine(BasePath, "Export");
        public static string TempPath => Path.Combine(BasePath, "Temp");

        public static string dispConfig = Path.Combine(BasePath, ExportPath, "config_disp.xml");

        // Nombres de archivos (Aplicando tu cambio de nombre)
        public static string SettingsXmlFile => Path.Combine(ConfigPath, "settings.xml");
        public static string DeviceXmlFile => Path.Combine(ConfigPath, "disp_settings.xml");

        public static void InicializarEntorno()
        {
            // 1. Crear carpetas si no existen
            if (!Directory.Exists(ConfigPath)) Directory.CreateDirectory(ConfigPath);
            if (!Directory.Exists(ExportPath)) Directory.CreateDirectory(ExportPath);
            if (!Directory.Exists(TempPath)) Directory.CreateDirectory(TempPath);

            // 2. Crear settings.xml por defecto si no existe
            if (!File.Exists(SettingsXmlFile))
            {
                var doc = new XDocument(new XElement("Settings",
                    new XElement("ExePath", @"C:\Program Files\Siemens\Automation\Portal V18\AddIns\ZC_ALM_TOOLS\ZC_Extractor.exe"),
                    new XElement("UltimoUsuario", Environment.UserName)
                ));
                doc.Save(SettingsXmlFile);
            }

            // 3. Crear disp_settings.xml por defecto si no existe
            if (!File.Exists(DeviceXmlFile))
            {
                var doc = new XDocument(new XElement("DeviceSettings",                    
                    new XElement("DeviceCategory", new XAttribute("Name", "Entrada Digital"),
                        new XElement("ExcelSheet", "DISP_ED"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_ED"),
                        new XElement("ModelClass", "Disp_ED"),
                        new XElement("XmlFile", "disp_ed.xml"),
                        new XElement("GlobalConfigKey", "Num_Disp_ED"),
                        new XElement("PlcCountConstant", "N_MAX_DISP_ED"),
                        new XElement("TiaDbName", "DB2000_ED"),
                        new XElement("TiaDbArrayName", "ED")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Entrada Analogica"),
                        new XElement("ExcelSheet", "DISP_EA"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_EA"),
                        new XElement("ModelClass", "Disp_EA"),
                        new XElement("XmlFile", "disp_ea.xml"),
                        new XElement("GlobalConfigKey", "Num_Disp_EA"),
                        new XElement("PlcCountConstant", "N_MAX_DISP_EA"),
                        new XElement("TiaDbName", "DB2001_EA"),
                        new XElement("TiaDbArrayName", "EA")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Salida Analogica"),
                        new XElement("ExcelSheet", "DISP_SA"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_SA"),
                        new XElement("ModelClass", "Disp_SA"),
                        new XElement("XmlFile", "disp_sa.xml"),
                        new XElement("GlobalConfigKey", "Num_Disp_SA"),
                        new XElement("PlcCountConstant", "N_MAX_DISP_SA"),
                        new XElement("TiaDbName", "DB2006_SA"),
                        new XElement("TiaDbArrayName", "SA")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Válvula"),
                        new XElement("ExcelSheet", "DISP_V"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_V"),
                        new XElement("ModelClass", "Disp_V"),
                        new XElement("XmlFile", "disp_v.xml"),
                        new XElement("GlobalConfigKey", "Num_Disp_V"),
                        new XElement("PlcCountConstant", "N_MAX_DISP_V"),
                        new XElement("TiaDbName", "DB2010_V"),
                        new XElement("TiaDbArrayName", "V")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Motor"),
                        new XElement("ExcelSheet", "DISP_M"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_M"),
                        new XElement("ModelClass", "Disp_M"),
                        new XElement("XmlFile", "disp_m.xml"),
                        new XElement("GlobalConfigKey", "Num_Disp_M"),
                        new XElement("PlcCountConstant", "N_MAX_DISP_M"),
                        new XElement("TiaDbName", "DB2015_M"),
                        new XElement("TiaDbArrayName", "M")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Motor variador"),
                        new XElement("ExcelSheet", "DISP_M_VF"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_M_VF"),
                        new XElement("ModelClass", "Disp_M_VF"),
                        new XElement("XmlFile", "disp_m_vf.xml"),
                        new XElement("GlobalConfigKey", "Num_Disp_M_VF"),
                        new XElement("PlcCountConstant", "N_MAX_DISP_M_VF"),
                        new XElement("TiaDbName", "DB2016_M_VF"),
                        new XElement("TiaDbArrayName", "M_VF"))
                ));
                doc.Save(DeviceXmlFile);
            }
        }

        // Método para obtener la lista de categorías de forma dinámica
        public static List<DeviceCategory> ObtenerMapaDispositivos()
        {
            if (!File.Exists(DeviceXmlFile)) return new List<DeviceCategory>();

            var doc = XDocument.Load(DeviceXmlFile);
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

        public static string LeerExePath()
        {
            if (!File.Exists(SettingsXmlFile)) return "";
            return XDocument.Load(SettingsXmlFile).Root?.Element("ExePath")?.Value ?? "";
        }
    }
}