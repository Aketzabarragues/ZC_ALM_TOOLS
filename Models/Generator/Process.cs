using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa la definicion de un proceso
    /// </summary>
    public class Process : ObservableObject
    {
        // ==================================================================================================================
        // Propiedades de identificación y Excel    
        public string Id { get; set; }
        public string Nombre { get; set; }
        public int NumEtapas { get; set; }
        public int MaxPReal { get; set; }
        public int MaxPInt { get; set; }
        public int NumAlarmas { get; set; }


        // ==================================================================================================================
        // Propiedades de estado
        private SynchronizationStatus _statusPReal = SynchronizationStatus.Pending;
        public SynchronizationStatus StatusPReal
        {
            get => _statusPReal;
            set { _statusPReal = value; OnPropertyChanged(); }
        }

        private SynchronizationStatus _statusPInt = SynchronizationStatus.Pending;
        public SynchronizationStatus StatusPInt
        {
            get => _statusPInt;
            set { _statusPInt = value; OnPropertyChanged(); }
        }

        private SynchronizationStatus _statusAlm = SynchronizationStatus.Pending;
        public SynchronizationStatus StatusAlm
        {
            get => _statusAlm;
            set { _statusAlm = value; OnPropertyChanged(); }
        }

        private SynchronizationStatus _statusAlmHmi = SynchronizationStatus.Pending;
        public SynchronizationStatus StatusAlmHmi
        {
            get => _statusAlmHmi;
            set { _statusAlmHmi = value; OnPropertyChanged(); }
        }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer la definicion de un proceso desde XML
        /// </summary>
        public static Process FromXml(XElement x) => new Process
        {
            Id = DataHelper.GetXmlVal(x, "UID"),
            Nombre = DataHelper.GetXmlVal(x, "Nombre"),
            NumEtapas = DataHelper.GetXmlInt(x, "Num.Etapas"),
            MaxPReal = DataHelper.GetXmlInt(x, "PReal"),
            MaxPInt = DataHelper.GetXmlInt(x, "PInt"),
            NumAlarmas = DataHelper.GetXmlInt(x, "Alarmas")
        };

        public override string ToString() => Nombre;
    }
}