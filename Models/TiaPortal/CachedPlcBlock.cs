using Siemens.Engineering.SW.Blocks;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;

namespace ZC_ALM_TOOLS.Models.TiaPortal
{
    // ==================================================================================================================
    /// <summary>
    /// Modelo que representa un bloque de Tia portal para la cache
    /// </summary>
    public class CachedPlcBlock : ObservableObject
    {

        // Referencia al bloque original para poder interactuar con él (Exportar, compilar...)
        public PlcBlock Block { get; set; }

        // Datos planos extraídos una sola vez
        public string Name { get; set; }
        public int Number { get; set; }
        public string ApiType { get; set; }
        public string SimpleType { get; set; }
        public string FolderPath { get; set; }

        public string ProgrammingLanguage { get; set; }

        // ==================================================================================================================
        // Propiedades de estado
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }


        // KOP/FUP (LAD/FBD) en TIA Portal no se pueden exportar como texto plano.
        public bool IsExportable => ProgrammingLanguage == "SCL" || ProgrammingLanguage == "DB" || ProgrammingLanguage == "STL";

        // Lógica de inyección: Solo vamos a inyectar /// <Requires> en SCL
        public bool CanUpdateDependencies => ProgrammingLanguage == "SCL";

    }
}