using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una salida analogica
    /// </summary>
    public class Disp_SA : ObservableObject, IDevice
    {
        // ==================================================================================================================
        // Propiedades de Excel  
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Tag { get; set; }
        public string Descripcion { get; set; }
        public string FAT { get; set; }
        public string SByte { get; set; }
        public string Unidades { get; set; }
        public string RII { get; set; }
        public string RSI { get; set; }
        public string GrAlarma { get; set; }
        public string Cuadro { get; set; }
        public string Observaciones { get; set; }
        public string CPTag { get; set; }
        public string CPComentario { get; set; }

        // ==================================================================================================================
        // Propiedades de estado
        private string _Estado = "Sin comprobar";
        public string Estado { get => _Estado; set { _Estado = value; OnPropertyChanged(); } }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer dispositivo salida analogica desde XML
        /// </summary>
        public static Disp_SA FromXml(XElement x) => new Disp_SA
        {
            UID = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Tag = DataHelper.GetXmlVal(x, "Tag"),
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            FAT = DataHelper.GetXmlVal(x, "FAT"),
            SByte = DataHelper.GetXmlVal(x, "S.Byte"), // Ojo al punto, coincide con tu XML
            Unidades = DataHelper.GetXmlVal(x, "UNIDADES"),
            RII = DataHelper.GetXmlVal(x, "RII"),
            RSI = DataHelper.GetXmlVal(x, "RSI"),
            GrAlarma = DataHelper.GetXmlVal(x, "Gr.Alarma"),
            Cuadro = DataHelper.GetXmlVal(x, "Cuadro"),
            Observaciones = DataHelper.GetXmlVal(x, "Observaciones"),
            CPTag = DataHelper.GetXmlVal(x, "CP.Tag"),
            CPComentario = DataHelper.GetXmlVal(x, "CP.Comentario")
        };
    }


}