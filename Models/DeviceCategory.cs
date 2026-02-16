
namespace ZC_ALM_TOOLS.Models
{
    public class DeviceCategory
    {
        public string Name { get; set; }        // Ej: "Válvulas"
        public string ExcelSheet { get; set; }  // Ej: "DISP_V"
        public string TiaGroup { get; set; }    // Ej: "002_Dispositivos"
        public string TiaTable { get; set; }    // Ej: "002_Disp_V"
        public string ModelClass { get; set; }  // Ej: "Disp_V" (Nombre de la clase modelo)
        public string XmlFile { get; set; }     // Ej: "disp_v.xml"                                                
        public string GlobalConfigKey { get; set; } // El nombre que Python ha puesto en config_disp.xml (ej: "Num_Disp_V")        
        public string PlcCountConstant { get; set; } // El nombre de la constante en la tabla 000_Config_Dispositivos del PLC (ej: "N_MAX_DISP_V")
        public string TiaDbName { get; set; }   // El nombre del DB de dispositivos en TIA Portal (ej: "DB2010_V")
        public string TiaDbArrayName { get; set; } // Ej: "V" o "Mux"

    }
}