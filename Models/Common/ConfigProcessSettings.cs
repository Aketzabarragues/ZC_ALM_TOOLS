
namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una la configuracion de procesos
    /// </summary>
    public class ConfigProcessSettings
    {

        // Claves para la caché de ingeniería
        public string ProcessName { get; set; } = "Procesos";
        public string PRealName { get; set; } = "P_Real";
        public string PIntName { get; set; } = "P_Int";
        public string AlarmName { get; set; } = "Alarmas";
        public string StageName { get; set; } = "Etapas";

        // Nombres de recursos (Excel + TIA Portal)
        public string ExcelSheet { get; set; }
        public string ExcelTable { get; set; }
        public string SuffixConstReal { get; set; }
        public string SuffixConstInt { get; set; }
        public string SuffixConstAlm { get; set; }
        public string SuffixConstAlmHmi { get; set; }
        public string SuffixDbReal { get; set; }
        public string SuffixDbInt { get; set; }
        public string SuffixDbAlm { get; set; }

    }
}