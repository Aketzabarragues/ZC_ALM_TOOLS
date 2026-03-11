using Siemens.Engineering.SW.Tags;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa una tabla de variables de Tia portal para la cache
    /// </summary>
    public class CachedPlcTagTable
    {        
        public PlcTagTable Table { get; set; }

        public string Name { get; set; }
        public string FolderPath { get; set; }
    }
}