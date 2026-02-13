using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class Disp_EA : ObservableObject, IDispositivo
    {
        // --- PROPIEDADES DEL EXCEL
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Tag { get; set; }
        public string Descripcion { get; set; }
        public string FAT { get; set; }
        public string EByte { get; set; }
        public string Unidades { get; set; }
        public string RII { get; set; }
        public string RSI { get; set; }
        public string GrAlarma { get; set; }
        public string Cuadro { get; set; }
        public string Observaciones { get; set; }
        public string CPTag { get; set; }
        public string CPTipo { get; set; }
        public int CPNum { get; set; }        
        public string CPComentario { get; set; }

        // --- PROPIEDAD PARA LA INTERFAZ ---
        // No se carga desde el CSV, se usa solo para mostrar resultados de comparación
        private string _Estado = "Sin comprobar";
        public string Estado { get => _Estado; set { _Estado = value; OnPropertyChanged(); } }

        // --- MÉTODO DE CARGA
        public static Disp_EA FromXml(XElement x) => new Disp_EA
        {
            UID = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Tag = DataHelper.GetXmlVal(x, "Tag"),
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            FAT = DataHelper.GetXmlVal(x, "FAT"),
            EByte = DataHelper.GetXmlVal(x, "E.Byte"), // Ojo al punto, coincide con tu XML
            Unidades = DataHelper.GetXmlVal(x, "UNIDADES"),
            RII = DataHelper.GetXmlVal(x, "RII"),
            RSI = DataHelper.GetXmlVal(x, "RSI"),
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