using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una categoría de dispositivos y su configuración de ingeniería
    /// </summary>
    public class ConfigDeviceCategory : ObservableObject
    {

        // Propiedades de identificación y Excel    
        public string Name { get; set; }
        public string ExcelSheet { get; set; }


        // ==================================================================================================================
        // Configuración TIA Portal
        public string TiaGroup { get; set; }
        public string TiaTable { get; set; }
        public string TiaDbName { get; set; }
        public string TiaDbArrayName { get; set; }


        // ==================================================================================================================
        // Lógica interna y Archivos
        public string ModelClass { get; set; }
        public string XmlFile { get; set; }
        public string GlobalConfigKey { get; set; }     
        public string PlcCountConstant { get; set; } 


        // ==================================================================================================================
        // Propiedades de estado
        private SynchronizationStatus _nMaxStatus = SynchronizationStatus.Pending;
        public SynchronizationStatus NMaxStatus
        {
            get => _nMaxStatus;
            set { _nMaxStatus = value; OnPropertyChanged(); }
        }

        private SynchronizationStatus _constantsStatus = SynchronizationStatus.Pending;
        public SynchronizationStatus ConstantsStatus
        {
            get => _constantsStatus;
            set { _constantsStatus = value; OnPropertyChanged(); }
        }

        private SynchronizationStatus _dbStatus = SynchronizationStatus.Pending;
        public SynchronizationStatus DbStatus
        {
            get => _dbStatus;
            set { _dbStatus = value; OnPropertyChanged(); }
        }


        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer la categoria de dispositivo desde XML
        /// </summary>
        public static ConfigDeviceCategory FromXml(XElement x) => new ConfigDeviceCategory
        {
            Name = x.Attribute("Name")?.Value,
            ExcelSheet = DataHelper.GetXmlVal(x, "ExcelSheet"),
            TiaGroup = DataHelper.GetXmlVal(x, "TiaGroup"),
            TiaTable = DataHelper.GetXmlVal(x, "TiaTable"),
            TiaDbName = DataHelper.GetXmlVal(x, "TiaDbName"),
            TiaDbArrayName = DataHelper.GetXmlVal(x, "TiaDbArrayName"),
            ModelClass = DataHelper.GetXmlVal(x, "ModelClass"),
            XmlFile = DataHelper.GetXmlVal(x, "XmlFile"),
            GlobalConfigKey = DataHelper.GetXmlVal(x, "GlobalConfigKey"),
            PlcCountConstant = DataHelper.GetXmlVal(x, "PlcCountConstant")
        };

    }
}