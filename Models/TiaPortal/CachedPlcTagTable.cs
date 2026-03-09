using Siemens.Engineering.SW.Tags;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    public class CachedPlcTagTable
    {        
        public PlcTagTable Table { get; set; }

        public string Name { get; set; }
        public string FolderPath { get; set; }
    }
}