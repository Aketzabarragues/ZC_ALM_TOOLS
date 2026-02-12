using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    public class Disp_EA : ObservableObject
    {
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



        public static Disp_EA FromCsv(string[] c) => new Disp_EA
        {
            UID = DataHelper.GetVal(c, 0),
            Numero = DataHelper.ParseInt(DataHelper.GetVal(c, 1)),
            Tag = DataHelper.GetVal(c, 2),
            Descripcion = DataHelper.GetVal(c, 3),
            FAT = DataHelper.GetVal(c, 4),
            EByte = DataHelper.GetVal(c, 5),
            Unidades = DataHelper.GetVal(c, 6),
            RII = DataHelper.GetVal(c, 7),
            RSI = DataHelper.GetVal(c, 8),
            GrAlarma = DataHelper.GetVal(c, 9),
            Cuadro = DataHelper.GetVal(c, 10),
            Observaciones = DataHelper.GetVal(c, 11),
            CPTag = DataHelper.GetVal(c, 12),
            CPTipo = DataHelper.GetVal(c, 13),
            CPNum = DataHelper.ParseInt(DataHelper.GetVal(c, 14)),
            CPComentario = DataHelper.GetVal(c, 15)
        };
    }


}