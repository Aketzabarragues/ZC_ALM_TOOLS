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
        // 1. Definición de Rutas
        private static string BasePath = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");
        public static string ConfigPath = Path.Combine(BasePath, "Config");
        public static string ExportPath = Path.Combine(BasePath, "Export");
        public static string TempPath = Path.Combine(BasePath, "Temp");

        public static string DeviceXmlFile = Path.Combine(ConfigPath, "dispositivos.xml");
        public static string SettingsXmlFile = Path.Combine(ConfigPath, "settings.xml");


        public static void InicializarEntorno()
        {
            // Crear carpetas si no existen
            if (!Directory.Exists(ConfigPath)) Directory.CreateDirectory(ConfigPath);
            if (!Directory.Exists(ExportPath)) Directory.CreateDirectory(ExportPath);
            if (!Directory.Exists(TempPath)) Directory.CreateDirectory(TempPath);

            // Crear XML inicial si no existe
            if (!File.Exists(DeviceXmlFile)) CrearXmlDispositivosPorDefecto();
            if (!File.Exists(SettingsXmlFile)) CrearSettingsPorDefecto();
        }


        public static List<DeviceCategory> ObtenerMapaDispositivos()
        {
            var lista = new List<DeviceCategory>();

            if (!File.Exists(DeviceXmlFile)) return lista;

            try
            {
                XDocument doc = XDocument.Load(DeviceXmlFile);

                // Buscamos todos los nodos "DeviceCategory" y los mapeamos a nuestra clase
                lista = doc.Descendants("DeviceCategory")
                    .Select(x => new DeviceCategory
                    {
                        Name = x.Attribute("Name")?.Value,
                        ExcelSheet = x.Element("ExcelSheet")?.Value,
                        TiaGroup = x.Element("TiaGroup")?.Value,
                        TiaTable = x.Element("TiaTable")?.Value,
                        ModelClass = x.Element("ModelClass")?.Value
                    }).ToList();
            }
            catch (Exception)
            {
                // En caso de error (XML mal formado), devolvemos lista vacía
            }

            return lista;
        }




        private static void CrearSettingsPorDefecto()
        {
            XDocument doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("Settings",
                    new XElement("ExePath", @"C:\Program Files\Siemens\Automation\Portal V18\AddIns\ZC_ALM_TOOLS\ZC_Extractor.exe"),
                    new XElement("UltimoUsuario", Environment.UserName)
                )
            );
            doc.Save(SettingsXmlFile);
        }

        public static string LeerExePath()
        {
            try
            {
                XDocument doc = XDocument.Load(SettingsXmlFile);
                return doc.Root.Element("ExePath")?.Value;
            }
            catch { return ""; }
        }

        public static void GuardarExePath(string nuevaRuta)
        {
            try
            {
                XDocument doc = XDocument.Load(SettingsXmlFile);
                doc.Root.Element("ExePath").Value = nuevaRuta;
                doc.Save(SettingsXmlFile);
            }
            catch { }
        }

        private static void CrearXmlDispositivosPorDefecto()
        {
            XDocument doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("DeviceSettings",
                    new XElement("DeviceCategory", new XAttribute("Name", "Válvulas"),
                        new XElement("ExcelSheet", "DISP_V"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_V"),
                        new XElement("ModelClass", "Disp_V")),

                    new XElement("DeviceCategory", new XAttribute("Name", "Entradas Digitales"),
                        new XElement("ExcelSheet", "DISP_ED"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_ED"),
                        new XElement("ModelClass", "Disp_ED")),

                    new XElement("DeviceCategory", new XAttribute("Name", "Entradas Analógicas"),
                        new XElement("ExcelSheet", "DISP_EA"),
                        new XElement("TiaGroup", "002_Dispositivos"),
                        new XElement("TiaTable", "002_Disp_EA"),
                        new XElement("ModelClass", "Disp_EA"))
                )
            );
            doc.Save(DeviceXmlFile);
        }
    }
}