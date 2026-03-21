using Siemens.Engineering.SW.Types;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa un Tipo de Datos de Usuario (UDT) de Tia Portal para la caché
    /// </summary>
    public class CachedPlcType
    {
        // Referencia al tipo original para exportar o manipular
        public PlcType Type { get; set; }

        // Datos planos extraídos una sola vez
        public string Name { get; set; }
        public string FolderPath { get; set; }
    }
}