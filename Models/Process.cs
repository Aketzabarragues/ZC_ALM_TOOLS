using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class Process
    {
        public string Id { get; set; }
        public string Nombre { get; set; }
        public int NumEtapas { get; set; }
        public int MaxPReal { get; set; }
        public int MaxPInt { get; set; }
        public int NumAlarmas { get; set; }

        public static Process FromXml(XElement x) => new Process
        {
            Id = DataHelper.GetXmlVal(x, "UID"),
            Nombre = DataHelper.GetXmlVal(x, "Nombre"),
            NumEtapas = DataHelper.GetXmlInt(x, "Num.Etapas"),
            MaxPReal = DataHelper.GetXmlInt(x, "PReal"),
            MaxPInt = DataHelper.GetXmlInt(x, "PInt"),
            NumAlarmas = DataHelper.GetXmlInt(x, "Alarmas")
        };

        // Imprescindible para que el ComboBox muestre el nombre del proceso
        public override string ToString() => Nombre;
    }
}