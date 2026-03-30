
namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una la configuracion de procesos
    /// </summary>
    public class ConfigProcessSettings
    {
        public string Name { get; set; }
        public string ExcelSheet { get; set; }
        public string ExcelTable { get; set; }
        public string ArrayNameReal { get; set; }
        public string ArrayNameInt { get; set; }
        public string ArrayNameAlm { get; set; }
        public string SuffixConstReal { get; set; }
        public string SuffixConstInt { get; set; }
        public string SuffixConstAlm { get; set; }
        public string SuffixConstAlmHmi { get; set; }
        public string SuffixDbReal { get; set; }
        public string SuffixDbInt { get; set; }
        public string SuffixDbAlm { get; set; }

    }
}