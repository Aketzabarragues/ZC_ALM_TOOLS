using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ZC_ALM_TOOLS.Models;

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
                    new XElement("DeviceCategory", new XAttribute("Name", "Válvulas"),
                        new XElement("ExcelSheet", "DISP_V"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_V"),
                        new XElement("ModelClass", "Disp_V"),
                        new XElement("XmlFile", "disp_v.xml")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Entradas Digitales"),
                        new XElement("ExcelSheet", "DISP_ED"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_ED"),
                        new XElement("ModelClass", "Disp_ED"),
                        new XElement("XmlFile", "disp_ed.xml")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Entradas Analogicas"),
                        new XElement("ExcelSheet", "DISP_EA"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_EA"),
                        new XElement("ModelClass", "Disp_EA"),
                        new XElement("XmlFile", "disp_ea.xml")),
                    new XElement("DeviceCategory", new XAttribute("Name", "Salidas Analogicas"),
                        new XElement("ExcelSheet", "DISP_SA"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_SA"),
                        new XElement("ModelClass", "Disp_SA"),
                        new XElement("XmlFile", "disp_sa.xml"))
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
                XmlFile = x.Element("XmlFile")?.Value
            }).ToList();
        }

        public static string LeerExePath()
        {
            if (!File.Exists(SettingsXmlFile)) return "";
            return XDocument.Load(SettingsXmlFile).Root?.Element("ExePath")?.Value ?? "";
        }
    }
}