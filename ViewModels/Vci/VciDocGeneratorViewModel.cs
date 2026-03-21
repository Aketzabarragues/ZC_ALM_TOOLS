using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Vci
{

    // ====================================================================================
    // Clase envoltorio adaptada para soportar tanto Bloques (CachedPlcBlock) como UDTs (CachedPlcType)
    // ====================================================================================
    public class SelectablePlcItem : ObservableObject
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // Almacena la referencia original genérica para usarla en la exportación (Fase 3)
        public object OriginalItem { get; set; }

        // Propiedades planas para la tabla gráfica (DataGrid)
        public string Name { get; set; }
        public string SimpleType { get; set; }
        public string FolderPath { get; set; }
    }



    // ==================================================================================================================
    /// <summary>
    /// ViewModel que gestiona la pestaña de documentacion del VCI
    /// </summary>
    public class VciDocGeneratorViewModel : ObservableObject
    {
        private readonly TiaPlcService _tiaPlcService;

        public ObservableCollection<SelectablePlcItem> PlcItems { get; set; } = new ObservableCollection<SelectablePlcItem>();

        public ICommand LoadItemsCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand GenerateDocCommand { get; }

        public VciDocGeneratorViewModel(TiaPlcService tiaPlcService)
        {
            _tiaPlcService = tiaPlcService;
            LoadItemsCommand = new RelayCommand(LoadItems);
            SelectAllCommand = new RelayCommand(() => SetAllSelection(true));
            DeselectAllCommand = new RelayCommand(() => SetAllSelection(false));
            GenerateDocCommand = new RelayCommand(GenerateDoc);
        }





        public void LoadItems()
        {
            PlcItems.Clear();

            // 1. Extraemos y añadimos los Bloques (FC, FB, OB, DB)
            var blocks = _tiaPlcService.GetAllBlocks();
            foreach (var b in blocks)
            {
                PlcItems.Add(new SelectablePlcItem
                {
                    OriginalItem = b,
                    IsSelected = false,
                    Name = b.Name,
                    SimpleType = b.SimpleType,
                    FolderPath = b.FolderPath
                });
            }

            // 2. Extraemos y añadimos los UDTs
            var types = _tiaPlcService.GetAllTypes();
            foreach (var t in types)
            {
                PlcItems.Add(new SelectablePlcItem
                {
                    OriginalItem = t,
                    IsSelected = false,
                    Name = t.Name,
                    SimpleType = "UDT",
                    FolderPath = t.FolderPath
                });
            }

            // 3. Ordenamos visualmente: Primero por Tipo y luego por Nombre alfabético
            var sortedList = PlcItems.OrderBy(i => i.SimpleType).ThenBy(i => i.Name).ToList();

            PlcItems.Clear();
            foreach (var item in sortedList)
            {
                PlcItems.Add(item);
            }

            StatusService.Set($"Se han cargado {PlcItems.Count} elementos en el generador de ayuda.", StatusType.Ok);
        }

        private void SetAllSelection(bool state)
        {
            foreach (var item in PlcItems)
            {
                item.IsSelected = state;
            }
        }

        private void GenerateDoc()
        {
            var selectedItems = PlcItems.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                StatusService.Set("No hay elementos seleccionados para generar documentación.", StatusType.Warning);
                return;
            }

            // TODO: FASE 3 - Inyectaremos aquí la lógica de exportación Openness y Process.Start(ZCALM.exe)
            StatusService.Set($"[FASE 3 PENDIENTE] Simulación: Se exportarían {selectedItems.Count} elementos y se lanzaría Python", StatusType.Ok);
            LogService.Write($"[DOC-GENERATOR] Elementos seleccionados para documentar: {selectedItems.Count}");
        }
    }
}