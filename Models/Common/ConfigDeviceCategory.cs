using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo de datos observable que define la configuración y el mapeo de una categoría 
    /// de dispositivos (ej. Motores, Válvulas). Vincula la información del Excel con las tablas 
    /// y bloques de datos correspondientes en TIA Portal, y mantiene el estado visual de su 
    /// sincronización para la interfaz gráfica.
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
        /// Método estático que construye e inicializa una instancia de ConfigDeviceCategory 
        /// a partir de un nodo XML (XElement) extraído del archivo de configuración de la aplicación (app_config.xml).
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