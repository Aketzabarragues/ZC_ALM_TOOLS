using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class ParameterConfig
    {
        public string Uid { get; set; }
        public int Numero { get; set; }
        public string Proceso { get; set; }
        public int DbNumber { get; set; }
        public string Producto { get; set; }
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
        public string ComentarioDB { get; set; }
        public string Visibilidad { get; set; }

        public static ParameterConfig FromXml(XElement x) => new ParameterConfig
        {
            Uid = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Proceso = DataHelper.GetXmlVal(x, "Proceso"),
            DbNumber = DataHelper.GetXmlInt(x, "Num.DB"),
            Producto = DataHelper.GetXmlVal(x, "Producto"),
            Tipo = DataHelper.GetXmlVal(x, "Tipo"),
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            ComentarioDB = DataHelper.GetXmlVal(x, "ComentarioDB"),
            Visibilidad = DataHelper.GetXmlVal(x, "Visibilidad")
        };
    }
}