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

        // ==================================================================================================================
        // Identificación y Excel
        public string Name { get; set; }
        public string ExcelSheet { get; set; }
        public string ExcelTable { get; set; }

        // ==================================================================================================================
        // Configuración TIA Portal
        public string TiaTable { get; set; }
        public string TiaDbName { get; set; }
        public string TiaDbArrayName { get; set; }
        public string PlcCountConstant { get; set; }

        // ==================================================================================================================
        // Lógica interna
        public string ModelClass { get; set; }
        public string GlobalConfigKey { get; set; }

        // ==================================================================================================================
        // Propiedades de estado
        private SynchronizationStatus _nMaxStatus = SynchronizationStatus.Pending;
        public SynchronizationStatus NMaxStatus { get => _nMaxStatus; set { _nMaxStatus = value; OnPropertyChanged(); } }

        private SynchronizationStatus _constantsStatus = SynchronizationStatus.Pending;
        public SynchronizationStatus ConstantsStatus { get => _constantsStatus; set { _constantsStatus = value; OnPropertyChanged(); } }

        private SynchronizationStatus _dbStatus = SynchronizationStatus.Pending;
        public SynchronizationStatus DbStatus { get => _dbStatus; set { _dbStatus = value; OnPropertyChanged(); } }
               

    }
}