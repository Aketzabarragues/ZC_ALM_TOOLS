using Siemens.Engineering.SW.Blocks;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa un bloque de Tia portal para la cache
    /// </summary>
    public class CachedPlcBlock
    {

        // Referencia al bloque original para poder interactuar con él (Exportar, compilar...)
        public PlcBlock Block { get; set; }

        // Datos planos extraídos una sola vez
        public string Name { get; set; }
        public int Number { get; set; }
        public string ApiType { get; set; }
        public string SimpleType { get; set; }
        public string FolderPath { get; set; }

    }
}