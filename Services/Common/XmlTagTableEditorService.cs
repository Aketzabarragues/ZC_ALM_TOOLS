using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ZC_ALM_TOOLS.Services.Common
{
    /// <summary>
    /// Clase encargada de leer y modificar las Tablas de Variables XML de TIA Portal.
    /// </summary>
    public class XmlTagTableEditorService
    {



        private readonly string _xmlPath;
        private readonly XDocument _doc;
        private readonly XNamespace _ns;
        private readonly XElement _objectList;
        private int _maxId;



        // ==================================================================================================================
        /// <summary>
        /// Constructor.
        /// </summary>
        public XmlTagTableEditorService(string xmlPath)
        {
            _xmlPath = xmlPath;
            _doc = XDocument.Load(xmlPath);
            _ns = _doc.Root.GetDefaultNamespace();

            var tableNode = _doc.Descendants(_ns + "SW.Tags.PlcTagTable").FirstOrDefault();
            if (tableNode == null) throw new Exception("No se encontró el nodo SW.Tags.PlcTagTable en el XML.");

            _objectList = tableNode.Element(_ns + "ObjectList");

            // Si la tabla estaba vacía en TIA Portal, creamos la envoltura
            if (_objectList == null)
            {
                _objectList = new XElement(_ns + "ObjectList");
                tableNode.Add(_objectList);
            }

            // Calculamos el ID máximo actual de todo el documento para no pisar IDs al añadir nodos nuevos
            _maxId = 0;
            foreach (var attr in _doc.Descendants().Attributes("ID"))
            {
                if (int.TryParse(attr.Value, out int val) && val > _maxId) _maxId = val;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer constantes de usuario.
        /// </summary>
        public Dictionary<int, string> GetTags()
        {
            var dic = new Dictionary<int, string>();
            var constants = _objectList.Elements(_ns + "SW.Tags.PlcUserConstant");

            foreach (var con in constants)
            {
                var attrList = con.Element(_ns + "AttributeList");
                if (attrList == null) continue;

                var name = attrList.Element(_ns + "Name")?.Value;
                var val = attrList.Element(_ns + "Value")?.Value;

                if (int.TryParse(val, out int id) && !string.IsNullOrEmpty(name))
                {
                    if (!dic.ContainsKey(id)) dic.Add(id, name);
                }
            }
            return dic;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para eliminar constantes de usuario.
        /// </summary>
        public void ClearConstants()
        {
            var xmlConstants = _objectList.Elements(_ns + "SW.Tags.PlcUserConstant").ToList();
            foreach (var c in xmlConstants) c.Remove();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para añadir constantes de usuario.
        /// </summary>
        public void AddConstant(string name, int value, string comment)
        {
            var constantNode = new XElement(_ns + "SW.Tags.PlcUserConstant",
                new XAttribute("ID", (++_maxId).ToString()),
                new XAttribute("CompositionName", "UserConstants"),

                new XElement(_ns + "AttributeList",
                    new XElement(_ns + "DataTypeName", "Int"),
                    new XElement(_ns + "Name", name),
                    new XElement(_ns + "Value", value.ToString())
                ),
                new XElement(_ns + "ObjectList",
                    new XElement(_ns + "MultilingualText",
                        new XAttribute("ID", (++_maxId).ToString()),
                        new XAttribute("CompositionName", "Comment"),

                        new XElement(_ns + "ObjectList",
                            new XElement(_ns + "MultilingualTextItem",
                                new XAttribute("ID", (++_maxId).ToString()),
                                new XAttribute("CompositionName", "Items"),

                                new XElement(_ns + "AttributeList",
                                    new XElement(_ns + "Culture", "es-ES"),
                                    new XElement(_ns + "Text", comment ?? "")
                                )
                            )
                        )
                    )
                )
            );

            _objectList.Add(constantNode);
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para guardar y cerrar el xml de constantes de usuario.
        /// </summary>
        public void Save()
        {
            _doc.Save(_xmlPath);
        }
    }
}