using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{    
    public class Disp_Config
    {
        public string Name { get; set; }
        public int Value { get; set; }

        public static Disp_Config FromXml(XElement x) => new Disp_Config
        {
            Name = DataHelper.GetXmlVal(x, "Name"),
            Value = DataHelper.GetXmlInt(x, "Value")
        };
    }
}
