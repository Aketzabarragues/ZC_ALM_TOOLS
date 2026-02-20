using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class Alarms
    {
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Proceso { get; set; }
        public int NumDB { get; set; }
        public string Descripcion { get; set; }
        public string ComentarioDB { get; set; }

        public static Alarms FromXml(XElement x) => new Alarms
        {
            UID = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Proceso = DataHelper.GetXmlVal(x, "Proceso"),
            NumDB = DataHelper.GetXmlInt(x, "Num.DB"), // Fíjate en el punto, como en el XML
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            ComentarioDB = DataHelper.GetXmlVal(x, "ComentarioDB")
        };

    }
}
