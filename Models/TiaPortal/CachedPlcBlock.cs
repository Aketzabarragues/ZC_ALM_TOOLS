using Siemens.Engineering.SW.Blocks;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    public class CachedPlcBlock
    {

        // Referencia al bloque original para poder interactuar con él (Exportar, compilar...)
        public PlcBlock Block { get; set; }

        // Datos planos extraídos una sola vez
        public string Name { get; set; }
        public int Number { get; set; }
        public string ApiType { get; set; }      // Ej: "GlobalDB", "InstanceDB", "FC"
        public string SimpleType { get; set; }   // Ej: "DB", "FC", "FB", "OB"
        public string FolderPath { get; set; }   // Ej: "Root\0_Sistema\Alarmas"

    }
}