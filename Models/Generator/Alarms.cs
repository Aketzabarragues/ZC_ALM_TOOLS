using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa alarmas
    /// </summary>
    public class Alarms : ObservableObject
    {

        // ==================================================================================================================
        // Propiedades de identificación y Excel   
        public string UID { get; set; }
        public int Numero { get; set; }
        public string Proceso { get; set; }
        public int DbNumber { get; set; }
        public string Descripcion { get; set; }
        public string ComentarioDB { get; set; }


        // ==================================================================================================================
        // Propiedades de estado
        private string _estado = "Pendiente";
        public string Estado
        {
            get => _estado;
            set { _estado = value; OnPropertyChanged(); }
        }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer alarmas desde XML
        /// </summary>
        public static Alarms FromXml(XElement x) => new Alarms
        {
            UID = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Proceso = DataHelper.GetXmlVal(x, "Proceso"),
            DbNumber = DataHelper.GetXmlInt(x, "Num.DB"), // Fíjate en el punto, como en el XML
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            ComentarioDB = DataHelper.GetXmlVal(x, "ComentarioDB")
        };

    }
}
