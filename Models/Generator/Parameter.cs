using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa parametros
    /// </summary>
    public class Parameter : ObservableObject
    {

        // ==================================================================================================================
        // Propiedades de identificación y Excel   
        public string Uid { get; set; }
        public int Numero { get; set; }
        public string Proceso { get; set; }
        public int DbNumber { get; set; }
        public string Producto { get; set; }
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
        public string ComentarioDB { get; set; }
        public string Visibilidad { get; set; }


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
        /// Metodo para leer parametros desde XML
        /// </summary>
        public static Parameter FromXml(XElement x) => new Parameter
        {
            Uid = DataHelper.GetXmlVal(x, "UID"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Proceso = DataHelper.GetXmlVal(x, "Proceso"),
            DbNumber = DataHelper.GetXmlInt(x, "Num.DB"),
            Producto = DataHelper.GetXmlVal(x, "Producto"),
            Tipo = DataHelper.GetXmlVal(x, "Tipo"),
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            ComentarioDB = DataHelper.GetXmlVal(x, "ComentarioDB"),
            Visibilidad = DataHelper.GetXmlVal(x, "Visibilidad")
        };
    }
}