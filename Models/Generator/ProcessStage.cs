using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Generator
{
    public class ProcessStage : ObservableObject
    {

        // ==================================================================================================================
        // Propiedades de identificación y Excel    
        public string Uid { get; set; }
        public int ProcessUid { get; set; }
        public int Numero { get; set; }
        public string Proceso { get; set; }
        public int ValorEtapa { get; set; }
        public string Descripcion { get; set; }
        public string NombreVariable { get; set; }
        public string CpTag { get; set; }
        public string CpComentario { get; set; }


        // ==================================================================================================================
        // Propiedades de estado
        private string _estado = "Pendiente";
        public string Estado
        {
            get => _estado;
            set { _estado = value; OnPropertyChanged(); }
        }


        public static ProcessStage FromXml(XElement x) => new ProcessStage
        {
            Uid = DataHelper.GetXmlVal(x, "UID"),
            ProcessUid = DataHelper.GetXmlInt(x, "UID.Proceso"),
            Numero = DataHelper.GetXmlInt(x, "Numero"),
            Proceso = DataHelper.GetXmlVal(x, "Proceso"),
            ValorEtapa = DataHelper.GetXmlInt(x, "Valor.Etapa"),
            Descripcion = DataHelper.GetXmlVal(x, "Descripcion"),
            NombreVariable = DataHelper.GetXmlVal(x, "Nombre.Variable"),
            CpTag = DataHelper.GetXmlVal(x, "CP.Tag"),
            CpComentario = DataHelper.GetXmlVal(x, "CP.Comentario")
        };
    }
}
