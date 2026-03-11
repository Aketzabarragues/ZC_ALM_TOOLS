using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Services.Common
{
    /// <summary>
    /// Clase encargada de leer y modificar los Bloques de Datos (DB) XML de TIA Portal.
    /// </summary>
    public class XmlDataBlockEditorService
    {



        private readonly string _xmlPath;
        private readonly XDocument _doc;
        private readonly XNamespace _ns;



        // ==================================================================================================================
        /// <summary>
        /// Constructor.
        /// </summary>
        public XmlDataBlockEditorService(string xmlPath)
        {
            _xmlPath = xmlPath;
            _doc = XDocument.Load(xmlPath);

            // Usamos el namespace del propio archivo para ser compatibles con TIA V16, V17, V18, etc.
            // Esto elimina la necesidad de tener el "http://..." hardcodeado.
            _ns = _doc.Root.GetDefaultNamespace();
        }



        // ==================================================================================================================
        /// <summary>
        /// Obtiene los comentarios de los arrays del DataBlock.
        /// </summary>
        public Dictionary<int, string> GetArrayComments()
        {
            var dic = new Dictionary<int, string>();

            // Buscamos Subelements por LocalName para ignorar problemas de namespace y buscar en todo el DB
            var subelements = _doc.Descendants().Where(x => x.Name.LocalName == "Subelement" && x.Attribute("Path") != null);

            foreach (var sub in subelements)
            {
                if (int.TryParse(sub.Attribute("Path").Value, out int id))
                {
                    var commentNode = sub.Descendants().FirstOrDefault(x => x.Name.LocalName == "MultiLanguageText" && x.Attribute("Lang")?.Value == "es-ES");
                    if (commentNode != null && !dic.ContainsKey(id))
                    {
                        dic.Add(id, commentNode.Value);
                    }
                }
            }
            return dic;
        }



        // ==================================================================================================================
        /// <summary>
        /// Establece o actualiza el comentario de un índice concreto dentro de un Array principal (y opcionalmente en el array Vis).
        /// </summary>
        public bool SetComment(string arrayName, int index, string comment, bool updateVisArray = false)
        {
            bool isModified = false;

            // Buscamos el array principal por su nombre
            var dataMember = _doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Member" && x.Attribute("Name")?.Value == arrayName);
            if (dataMember == null) throw new Exception($"No se encontró el array '{arrayName}' en el DB.");

            // Actualizamos el comentario en el array principal
            if (UpdateOrAddCommentNode(dataMember, index, comment)) isModified = true;

            // Si es un parámetro y nos piden actualizar también el array paralelo "Vis" (visualización HMI)
            if (updateVisArray)
            {
                var visMember = _doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Member" && x.Attribute("Name")?.Value == "Vis");
                if (visMember != null)
                {
                    if (UpdateOrAddCommentNode(visMember, index, comment)) isModified = true;
                }
            }

            return isModified;
        }



        // ==================================================================================================================
        /// <summary>
        /// Método interno auxiliar para buscar, crear o actualizar el nodo de comentario dentro de un Member.
        /// </summary>
        private bool UpdateOrAddCommentNode(XElement memberNode, int index, string text)
        {
            if (memberNode == null) return false;

            // 1. Buscamos el Subelement (el índice del array)
            var subElement = memberNode.Elements().FirstOrDefault(x => x.Name.LocalName == "Subelement" && x.Attribute("Path")?.Value == index.ToString());

            if (subElement == null)
            {
                if (string.IsNullOrEmpty(text)) return false; // Si no existe y el texto viene vacío, no hacemos nada
                subElement = new XElement(_ns + "Subelement", new XAttribute("Path", index.ToString()));
                memberNode.Add(subElement);
            }

            // 2. Buscamos el nodo Comment
            var commentNode = subElement.Elements().FirstOrDefault(x => x.Name.LocalName == "Comment");

            if (commentNode == null && !string.IsNullOrEmpty(text))
            {
                commentNode = new XElement(_ns + "Comment");
                subElement.AddFirst(commentNode);
            }

            if (commentNode != null)
            {
                // 3. Buscamos el texto en español
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
                    commentNode.Add(new XElement(_ns + "MultiLanguageText", new XAttribute("Lang", "es-ES"), text));
                    return true;
                }
            }

            return false;
        }



        // ==================================================================================================================
        /// <summary>
        /// Guarda los cambios en el archivo XML.
        /// </summary>
        public void Save()
        {
            _doc.Save(_xmlPath);
        }
    }
}