using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class Disp_ED : ObservableObject, IDevice
    {
        // --- PROPIEDADES DEL EXCEL
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Tag { get; set; }
        public string Descripcion { get; set; }
        public string FAT { get; set; }
        public string EByte { get; set; }
        public string EBit { get; set; }
        public string GrAlarma { get; set; }
        public string Cuadro { get; set; }
        public string Observaciones { get; set; }
        public string CPTag { get; set; }
        public string CPTipo { get; set; }
        public int CPNum { get; set; }
        public string CPComentario { get; set; }

        // --- PROPIEDAD PARA LA INTERFAZ ---
        private string _Estado = "Sin comprobar";
        public string Estado { get => _Estado; set { _Estado = value; OnPropertyChanged(); } }

        public static Disp_ED FromXml(XElement x) => new Disp_ED
        {
            UID = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Tag = DataHelper.GetXmlVal(x, "Tag"),
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            FAT = DataHelper.GetXmlVal(x, "FAT"),
            EByte = DataHelper.GetXmlVal(x, "E.Byte"),
            EBit = DataHelper.GetXmlVal(x, "E.Bit"),
            GrAlarma = DataHelper.GetXmlVal(x, "Gr.Alarma"),
            Cuadro = DataHelper.GetXmlVal(x, "Cuadro"),
            Observaciones = DataHelper.GetXmlVal(x, "Observaciones"),
            CPTag = DataHelper.GetXmlVal(x, "CP.Tag"),
            CPTipo = DataHelper.GetXmlVal(x, "CP.Tipo"),
            CPNum = DataHelper.GetXmlInt(x, "CP.Num."),
            CPComentario = DataHelper.GetXmlVal(x, "CP.Comentario")
        };
    

    }

}