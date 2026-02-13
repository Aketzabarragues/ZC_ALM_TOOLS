using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class ProcessConfig
    {
        public string Id { get; set; }
        public string Nombre { get; set; }
        public int NumEtapas { get; set; }
        public int MaxPReal { get; set; }
        public int MaxPInt { get; set; }
        public int NumAlarmas { get; set; }

        public static ProcessConfig FromXml(XElement x) => new ProcessConfig
        {
            Id = DataHelper.GetXmlVal(x, "UID"), // En el XML es UID
            Nombre = DataHelper.GetXmlVal(x, "Nombre"),
            NumEtapas = DataHelper.GetXmlInt(x, "Num.Etapas"),
            MaxPReal = DataHelper.GetXmlInt(x, "PReal"),
            MaxPInt = DataHelper.GetXmlInt(x, "PInt"),
            NumAlarmas = DataHelper.GetXmlInt(x, "Alarmas")
        };
    }
}