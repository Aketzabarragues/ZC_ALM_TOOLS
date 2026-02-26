using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Services
{
    public static class XmlParserService
    {
        // ==================================================================================================================
        // Extrae las etiquetas (Tags) de una Tabla de Variables exportada desde TIA Portal (Usado en Dispositivos)
        public static Dictionary<int, string> ParseDispTableXml(string path)
        {
            var dic = new Dictionary<int, string>();
            if (!File.Exists(path)) return dic;

            try
            {
                XDocument doc = XDocument.Load(path);
                var constants = doc.Descendants().Where(x => x.Name.LocalName.EndsWith("PlcUserConstant"));

                foreach (var con in constants)
                {
                    XNamespace ns = con.Name.Namespace;
                    var attrList = con.Element(ns + "AttributeList");
                    if (attrList == null) continue;

                    var name = attrList.Element(ns + "Name")?.Value;
                    var val = attrList.Element(ns + "Value")?.Value;

                    if (int.TryParse(val, out int id) && !string.IsNullOrEmpty(name))
                    {
                        if (!dic.ContainsKey(id)) dic.Add(id, name);
                    }
                }
                LogService.Write($"[XML-PARSER] [ParseDispTableXml] Leídas {dic.Count} variables del archivo {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                LogService.Write($"[XML-PARSER] [ParseDispTableXml] Error en {Path.GetFileName(path)}: {ex.Message}", true);
            }

            return dic;
        }

        // ==================================================================================================================
        // Extrae los comentarios de los Arrays de un Bloque de Datos (DB) exportado desde TIA Portal (Usado en Parámetros/Alarmas)
        public static Dictionary<int, string> ParseDbCommentsXml(string path)
        {
            var dic = new Dictionary<int, string>();
            if (!File.Exists(path)) return dic;

            try
            {
                XDocument doc = XDocument.Load(path);

                // Buscamos todos los Subelement que tengan atributo Path (índices de los Arrays)
                var subelements = doc.Descendants().Where(x => x.Name.LocalName == "Subelement" && x.Attribute("Path") != null);

                foreach (var sub in subelements)
                {
                    if (int.TryParse(sub.Attribute("Path").Value, out int id))
                    {
                        var commentNode = sub.Descendants().FirstOrDefault(x => x.Name.LocalName == "MultiLanguageText" && x.Attribute("Lang")?.Value == "es-ES");

                        if (commentNode != null)
                        {
                            string comment = commentNode.Value;

                            // Usamos ContainsKey por si hay otros arrays (como 'Vis') que repiten los índices. 
                            if (!dic.ContainsKey(id))
                            {
                                dic.Add(id, comment);
                            }
                        }
                    }
                }
                LogService.Write($"[XML-PARSER] [ParseDbCommentsXml] Leídos {dic.Count} comentarios del archivo {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                LogService.Write($"[XML-PARSER] [ParseDbCommentsXml] Error en {Path.GetFileName(path)}: {ex.Message}", true);
            }

            return dic;
        }
    }
}