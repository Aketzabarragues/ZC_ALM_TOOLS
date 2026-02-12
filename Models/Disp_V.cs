using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class Disp_V : ObservableObject
    {
        // --- PROPIEDADES DEL EXCEL
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
        public string CPTipo { get; set; }
        public int CPNum { get; set; }
        public string CPComentario { get; set; }


        // --- PROPIEDAD PARA LA INTERFAZ ---
        // No se carga desde el CSV, se usa solo para mostrar resultados de comparación
        private string _Estado = "Sin comprobar";
        public string Estado
        {
            get => _Estado;
            set { _Estado = value; OnPropertyChanged(); }
        }

        // --- MÉTODO DE CARGA
        public static Disp_V FromCsv(string[] c) => new Disp_V
        {
            UID = DataHelper.GetVal(c, 0),
            Numero = DataHelper.ParseInt(DataHelper.GetVal(c, 1)),
            Tag = DataHelper.GetVal(c, 2),
            Descripcion = DataHelper.GetVal(c, 3),
            FAT = DataHelper.GetVal(c, 4),
            SByte = DataHelper.GetVal(c, 5),
            SBit =  DataHelper.GetVal(c, 6),
            RRByte = DataHelper.GetVal(c, 7),
            RRBit = DataHelper.GetVal(c, 8),
            RTByte = DataHelper.GetVal(c, 9),
            RTBit = DataHelper.GetVal(c, 10),
            GrAlarma = DataHelper.GetVal(c, 11),
            Cuadro = DataHelper.GetVal(c, 12),
            Observaciones = DataHelper.GetVal(c, 13),
            CPTag = DataHelper.GetVal(c, 14),
            CPTipo = DataHelper.GetVal(c, 15),
            CPNum = DataHelper.ParseInt(DataHelper.GetVal(c, 16)),
            CPComentario = DataHelper.GetVal(c, 17)
        };

    }


}