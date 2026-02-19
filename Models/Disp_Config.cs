using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class Disp_Config
    {
        public string Nombre { get; set; }
        public int Valor { get; set; }

        public static Disp_Config FromXml(XElement x) => new Disp_Config
        {
            Nombre = DataHelper.GetXmlVal(x, "Name"),
            Valor = DataHelper.GetXmlInt(x, "Value")
        };
    }
}