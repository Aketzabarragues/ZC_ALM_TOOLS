namespace ZC_ALM_TOOLS.Models
{
    public class DeviceCategory
    {
        public string Name { get; set; }        // Ej: "Válvulas"
        public string ExcelSheet { get; set; }  // Ej: "DISP_V"
        public string TiaGroup { get; set; }    // Ej: "002_Dispositivos"
        public string TiaTable { get; set; }    // Ej: "002_Disp_V"
        public string ModelClass { get; set; }  // Ej: "Disp_V"
    }
}