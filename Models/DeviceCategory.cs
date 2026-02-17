using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{

    // Representa una categoría de dispositivos y su configuración de ingeniería
    public class DeviceCategory : ObservableObject
    {

        // ==================================================================================================================
        // Propiedades de identificación y Excel    
        public string Name { get; set; } // Nombre de la categoría (ej. "Motor")
        public string ExcelSheet { get; set; } // Nombre de la hoja de Excel origen


        // ==================================================================================================================
        // Configuración TIA Portal
        public string TiaGroup { get; set; } // Carpeta donde se encuentra la tabla de variables
        public string TiaTable { get; set; } // Nombre de la tabla de variables
        public string TiaDbName { get; set; } // Nombre del bloque de datos para cirugía XML
        public string TiaDbArrayName { get; set; } // Nombre del Array dentro del DB para los comentarios


        // ==================================================================================================================
        // Lógica interna y Archivos
        public string ModelClass { get; set; } // Clase C# que representa el modelo (ej. Disp_V)
        public string XmlFile { get; set; } // Nombre del archivo XML intermedio generado por Python
        public string GlobalConfigKey { get; set; } // Clave para buscar el N_MAX en el Excel        
        public string PlcCountConstant { get; set; } // Nombre de la constante de dimensionado en el PLC


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
    }
}