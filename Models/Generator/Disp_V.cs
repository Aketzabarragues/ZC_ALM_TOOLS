using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una valvula
    /// </summary>
    public class Disp_V : ObservableObject, IDevice
    {
        // ==================================================================================================================
        // Propiedades de Excel  
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Tag { get; set; }
        public string Descripcion { get; set; }
        public string FAT { get; set; }
        public string SByte { get; set; }
        public string SBit { get; set; }
        public string RRByte { get; set; }
        public string RRBit { get; set; }
        public string RTByte { get; set; }
        public string RTBit { get; set; }
        public string GrAlarma { get; set; }
        public string Cuadro { get; set; }
        public string Observaciones { get; set; }
        public string CPTag { get; set; }
        public string CPComentario { get; set; }

        // ==================================================================================================================
        // Propiedades de estado
        private string _estado = "Sin comprobar";
        public string Estado { get => _estado; set { _estado = value; OnPropertyChanged(); } }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer dispositivo valvula desde XML
        /// </summary>
        public static Disp_V FromXml(XElement x) => new Disp_V
        {
            UID = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Tag = DataHelper.GetXmlVal(x, "Tag"),
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            FAT = DataHelper.GetXmlVal(x, "FAT"),
            SByte = DataHelper.GetXmlVal(x, "S.Byte"),
            SBit = DataHelper.GetXmlVal(x, "S.Bit"),
            RRByte = DataHelper.GetXmlVal(x, "RR.Byte"),
            RRBit = DataHelper.GetXmlVal(x, "RR.Bit"),
            RTByte = DataHelper.GetXmlVal(x, "RT.Byte"),
            RTBit = DataHelper.GetXmlVal(x, "RT.Bit"),
            GrAlarma = DataHelper.GetXmlVal(x, "Gr.Alarma"),
            Cuadro = DataHelper.GetXmlVal(x, "Cuadro"),
            Observaciones = DataHelper.GetXmlVal(x, "Observaciones"),
            CPTag = DataHelper.GetXmlVal(x, "CP.Tag"),
            CPComentario = DataHelper.GetXmlVal(x, "CP.Comentario")
        };
    }
}