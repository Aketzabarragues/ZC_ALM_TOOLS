using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Services.Common
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
        // 3. INYECTAR CONSTANTES DE USUARIO (Cirugía XML)
        public static bool InjectDispUserConstantsXml(string xmlPath, List<ZC_ALM_TOOLS.Models.Generator.IDevice> validExcelDevices)
        {
            try
            {
                XDocument doc = XDocument.Load(xmlPath);
                XNamespace ns = doc.Root.GetDefaultNamespace();
                var tableNode = doc.Descendants(ns + "SW.Tags.PlcTagTable").FirstOrDefault();
                var objectList = tableNode?.Element(ns + "ObjectList");

                if (objectList == null) throw new Exception("No se encontró el ObjectList en el XML.");

                // Borramos TODOS los nodos PlcUserConstant del XML de un plumazo
                var xmlConstants = objectList.Elements(ns + "SW.Tags.PlcUserConstant").ToList();
                foreach (var c in xmlConstants) c.Remove();

                int maxId = 0;
                foreach (var attr in doc.Descendants().Attributes("ID"))
                {
                    if (int.TryParse(attr.Value, out int val) && val > maxId) maxId = val;
                }

                // Fabricamos los nodos
                foreach (var dev in validExcelDevices)
                {
                    var constantNode = new XElement(ns + "SW.Tags.PlcUserConstant", new XAttribute("ID", (++maxId).ToString()), new XAttribute("CompositionName", "UserConstants"),
                        new XElement(ns + "AttributeList",
                            new XElement(ns + "DataTypeName", "Int"),
                            new XElement(ns + "Name", dev.CPTag),
                            new XElement(ns + "Value", dev.Numero.ToString()) // <-- Con la etiqueta correcta "Value"
                        ),
                        new XElement(ns + "ObjectList",
                            new XElement(ns + "MultilingualText", new XAttribute("ID", (++maxId).ToString()), new XAttribute("CompositionName", "Comment"),
                                new XElement(ns + "ObjectList",
                                    new XElement(ns + "MultilingualTextItem", new XAttribute("ID", (++maxId).ToString()), new XAttribute("CompositionName", "Items"),
                                        new XElement(ns + "AttributeList",
                                            new XElement(ns + "Culture", "es-ES"),
                                            new XElement(ns + "Text", dev.CPComentario ?? "")
                                        )
                                    )
                                )
                            )
                        )
                    );
                    objectList.Add(constantNode);
                }

                doc.Save(xmlPath);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[XML-PARSER] [InjectDispUserConstantsXml] Error: {ex.Message}", true);
                return false;
            }
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



        // ==================================================================================================================
        // INYECTAR COMENTARIOS EN DATABLOCK DE DISPOSITIVOS
        public static bool InjectDispDbCommentsXml(string xmlPath, string arrayName, List<ZC_ALM_TOOLS.Models.Generator.IDevice> devices)
        {
            try
            {
                XDocument doc = XDocument.Load(xmlPath);
                XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

                var staticSection = doc.Descendants(ns + "Section").FirstOrDefault(s => s.Attribute("Name")?.Value == "Static");
                if (staticSection == null) throw new Exception("No se encontró la sección 'Static' en el XML del DB.");

                var arrayMember = staticSection.Elements(ns + "Member").FirstOrDefault(m => m.Attribute("Name")?.Value == arrayName);
                if (arrayMember == null) throw new Exception($"No se encontró el array '{arrayName}' dentro de la sección Static.");

                foreach (var dev in devices)
                {
                    var subelement = arrayMember.Elements(ns + "Subelement").FirstOrDefault(s => s.Attribute("Path")?.Value == dev.Numero.ToString());
                    if (subelement == null)
                    {
                        subelement = new XElement(ns + "Subelement", new XAttribute("Path", dev.Numero.ToString()));
                        arrayMember.Add(subelement);
                    }

                    subelement.Elements(ns + "Comment").Remove();
                    subelement.Add(new XElement(ns + "Comment",
                        new XElement(ns + "MultiLanguageText", new XAttribute("Lang", "es-ES"), $"{dev.Tag} - {dev.Descripcion}")));
                }

                doc.Save(xmlPath);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[XML-PARSER] [InjectDispDbCommentsXml] Error: {ex.Message}", true);
                return false;
            }
        }




        // ==================================================================================================================
        // INYECTAR COMENTARIOS EN DATABLOCKS DE PARÁMETROS/ALARMAS
        public static bool InjectParamsAlarmsDbCommentsXml<T>(string xmlPath, string arrayName, IEnumerable<T> items, Func<T, int> getId, Func<T, string> getComment, bool hasVisArray)
        {
            try
            {
                XDocument doc = XDocument.Load(xmlPath);
                XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

                var dataMember = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Member" && x.Attribute("Name")?.Value == arrayName);
                if (dataMember == null) throw new Exception($"No se encontró el array '{arrayName}' en el DB.");

                XElement visMember = null;
                if (hasVisArray) visMember = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Member" && x.Attribute("Name")?.Value == "Vis");

                bool isModified = false;

                foreach (var item in items)
                {
                    int id = getId(item);
                    string expectedComment = getComment(item) ?? "";

                    if (UpdateOrAddCommentNode(dataMember, id, expectedComment, ns)) isModified = true;

                    if (hasVisArray && visMember != null)
                    {
                        if (UpdateOrAddCommentNode(visMember, id, expectedComment, ns)) isModified = true;
                    }
                }

                if (isModified) doc.Save(xmlPath);
                return isModified;
            }
            catch (Exception ex)
            {
                LogService.Write($"[XML-PARSER] [InjectParamsAlarmsDbCommentsXml] Error: {ex.Message}", true);
                return false;
            }
        }




        // Método auxiliar interno para actualizar un nodo de comentario
        private static bool UpdateOrAddCommentNode(XElement memberNode, int id, string text, XNamespace ns)
        {
            if (memberNode == null) return false;

            var subElement = memberNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Subelement" && x.Attribute("Path")?.Value == id.ToString());

            if (subElement == null)
            {
                if (string.IsNullOrEmpty(text)) return false;
                subElement = new XElement(ns + "Subelement", new XAttribute("Path", id.ToString()));
                memberNode.Add(subElement);
            }

            var commentNode = subElement.Elements().FirstOrDefault(x => x.Name.LocalName == "Comment");

            if (commentNode == null && !string.IsNullOrEmpty(text))
            {
                commentNode = new XElement(ns + "Comment");
                subElement.AddFirst(commentNode);
            }

            if (commentNode != null)
            {
                var multiLangNode = commentNode.Elements().FirstOrDefault(x => x.Name.LocalName == "MultiLanguageText" && x.Attribute("Lang")?.Value == "es-ES");

                if (multiLangNode != null)
                {
                    if (multiLangNode.Value != text)
                    {
                        multiLangNode.Value = text;
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    commentNode.Add(new XElement(ns + "MultiLanguageText", new XAttribute("Lang", "es-ES"), text));
                    return true;
                }
            }

            return false;
        }















    }
}