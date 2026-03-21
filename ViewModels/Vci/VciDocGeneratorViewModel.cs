using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Vci;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel para la gestión, exportación y compilación de la documentación de código y datos del proyecto.
    /// </summary>
    public class VciDocGeneratorViewModel : ObservableObject
    {
        private readonly TiaPlcService _tiaPlcService;

        public ObservableCollection<VciSelectableItem> PlcItems { get; set; }

        public ICommand LoadItemsCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand GenerateDocCommand { get; }

        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciDocGeneratorViewModel(TiaPlcService tiaPlcService)
        {
            _tiaPlcService = tiaPlcService;
            PlcItems = new ObservableCollection<VciSelectableItem>();

            LoadItemsCommand = new RelayCommand(LoadItems);
            SelectAllCommand = new RelayCommand(() => SetAllSelection(true));
            DeselectAllCommand = new RelayCommand(() => SetAllSelection(false));
            GenerateDocCommand = new RelayCommand(GenerateDoc);
        }

        // ==================================================================================================================
        /// <summary>
        /// Obtiene y consolida todos los bloques y tipos de datos (UDTs) desde la caché del PLC activo.
        /// </summary>
        // ==================================================================================================================
        /// <summary>
        /// Obtiene y consolida todos los bloques y tipos de datos (UDTs) desde la caché del PLC activo.
        /// </summary>
        public void LoadItems()
        {
            try
            {
                LogService.Write("[VCI-DOC-GENERATOR] Iniciando escaneo y refresco de elementos...");
                PlcItems.Clear();

                // 1. Extracción de Bloques (FC, FB, OB, DB)
                var blocks = _tiaPlcService.GetAllBlocks();
                LogService.Write($"[VCI-DOC-GENERATOR] Bloques recuperados de la caché: {blocks?.Count ?? 0}");

                if (blocks != null)
                {
                    foreach (var b in blocks)
                    {
                        PlcItems.Add(new VciSelectableItem
                        {
                            OriginalItem = b,
                            IsSelected = false,
                            Name = b.Name,
                            SimpleType = b.SimpleType,
                            FolderPath = b.FolderPath
                        });
                    }
                }

                // 2. Extracción de Tipos de Datos (UDT)
                var types = _tiaPlcService.GetAllTypes();
                LogService.Write($"[VCI-DOC-GENERATOR] UDTs recuperados de la caché: {types?.Count ?? 0}");

                if (types != null)
                {
                    foreach (var t in types)
                    {
                        PlcItems.Add(new VciSelectableItem
                        {
                            OriginalItem = t,
                            IsSelected = false,
                            Name = t.Name,
                            SimpleType = "UDT",
                            FolderPath = t.FolderPath
                        });
                    }
                }

                // Comprobación de seguridad
                if (PlcItems.Count == 0)
                {
                    LogService.Write("[VCI-DOC-GENERATOR] ATENCIÓN: La lista resultante está vacía. ¿Se ha seleccionado un PLC en el desplegable principal?");
                    StatusService.Set("No se han encontrado bloques ni UDTs. Verifica que has seleccionado un PLC arriba.", StatusType.Warning);
                    return;
                }

                LogService.Write("[VCI-DOC-GENERATOR] Ordenando elementos para la vista...");

                // 3. Ordenación visual: Primero por Tipo de elemento y luego alfabéticamente por Nombre
                var sortedList = PlcItems.OrderBy(i => i.SimpleType).ThenBy(i => i.Name).ToList();

                PlcItems.Clear();
                foreach (var item in sortedList)
                {
                    PlcItems.Add(item);
                }

                LogService.Write($"[VCI-DOC-GENERATOR] Refresco completado exitosamente. Total cargados: {PlcItems.Count}");
                StatusService.Set($"Se han cargado {PlcItems.Count} elementos en el generador de ayuda.", StatusType.Ok);
            }
            catch (System.Exception ex)
            {
                LogService.Write($"[VCI-DOC-GENERATOR] Error crítico al cargar los elementos: {ex.Message}", true);
                StatusService.Set("Error al cargar los elementos del PLC. Revisa los logs.", StatusType.Error);
            }
        }

        // ==================================================================================================================
        /// <summary>
        /// Aplica un estado de selección masiva a todos los elementos del listado.
        /// </summary>
        private void SetAllSelection(bool state)
        {
            foreach (var item in PlcItems)
            {
                item.IsSelected = state;
            }
        }

        // ==================================================================================================================
        /// <summary>
        /// Orquesta la exportación a texto plano y dispara el compilador estático de Python.
        /// </summary>
        private void GenerateDoc()
        {
            var selectedItems = PlcItems.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                StatusService.Set("No hay elementos seleccionados para generar documentación.", StatusType.Warning);
                return;
            }

            // TODO: FASE 3 - Inyectaremos aquí la lógica de exportación Openness y llamadas a System.Diagnostics.Process
            StatusService.Set($"[FASE 3 PENDIENTE] Simulación: Se exportarían {selectedItems.Count} elementos y se lanzaría ZCALM.exe", StatusType.Ok);
            LogService.Write($"[VCI-DOC-GENERATOR] Elementos seleccionados para documentar: {selectedItems.Count}");
        }
    }
}