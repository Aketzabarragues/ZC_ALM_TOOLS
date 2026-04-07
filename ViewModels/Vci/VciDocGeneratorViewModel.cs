using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Siemens.Engineering.SW.Blocks;
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

        // Nuevos servicios inyectados
        private readonly TiaPlcCacheService _cacheService;
        private readonly TiaPlcImportExportService _importExportService;

        public ObservableCollection<VciSelectableItem> PlcItems { get; set; }

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;
        private readonly IAppConfigService _appConfigService;

        // Comandos
        public AsyncRelayCommand LoadItemsCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand DeselectAllCommand { get; }
        public AsyncRelayCommand GenerateDocCommand { get; }


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciDocGeneratorViewModel(
            TiaPlcCacheService cacheService,
            TiaPlcImportExportService importExportService,
            ILogService logService,
            IStatusService statusService,
            IAppConfigService appConfigService)
        {
            _cacheService = cacheService;
            _importExportService = importExportService;
            _logService = logService;
            _statusService = statusService;
            _appConfigService = appConfigService;

            PlcItems = new ObservableCollection<VciSelectableItem>();

            // Asignación de los comandos asíncronos
            LoadItemsCommand = new AsyncRelayCommand(ExecuteLoadItems);
            SelectAllCommand = new RelayCommand(() => SetAllSelection(true));
            DeselectAllCommand = new RelayCommand(() => SetAllSelection(false));
            GenerateDocCommand = new AsyncRelayCommand(GenerateDocAsync, CanExecuteGenerateDoc);
        }


        // ==================================================================================================================
        /// <summary>
        /// Obtiene y consolida todos los bloques y tipos de datos (UDTs) desde la caché del PLC activo.
        /// </summary>
        private async Task ExecuteLoadItems()
        {
            try
            {
                _statusService.Set("[VCI-DOC-GENERATOR] [ExecuteLoadItems] Iniciando escaneo y refresco de elementos...", StatusType.Warning);
                await Task.Delay(50); // Pausa visual para que WPF pinte el mensaje

                PlcItems.Clear();

                // 1. Extracción de Bloques (FC, FB, OB, DB) - Ahora desde la Caché
                var blocks = _cacheService.GetAllBlocks();
                _logService.Write($"[VCI-DOC-GENERATOR] [ExecuteLoadItems] Bloques recuperados de la caché: {blocks?.Count ?? 0}");

                // Extracción de bloques exportables (SCL, DB, STL)
                if (blocks != null)
                {
                    foreach (var b in blocks)
                    {
                        try
                        {
                            if (b.IsExportable)
                            {
                                PlcItems.Add(new VciSelectableItem
                                {
                                    OriginalItem = b.Block,
                                    IsSelected = false,
                                    Name = b.Name,
                                    SimpleType = b.SimpleType,
                                    FolderPath = b.FolderPath,
                                    Number = b.Number,
                                    ProgrammingLanguage = b.ProgrammingLanguage,
                                    CanUpdateDependencies = b.CanUpdateDependencies,
                                    IsExportable = b.IsExportable
                                });
                            }
                        }
                        catch (Exception blockEx)
                        {
                            _logService.Write($"[VCI-DOC-GENERATOR] [ExecuteLoadItems] Error leyendo propiedades del bloque {b.Name}: {blockEx.Message}", true);
                        }
                    }
                }

                // Extracción de Tipos de Datos (UDT) - Ahora desde la Caché
                var types = _cacheService.GetAllTypes();
                _logService.Write($"[VCI-DOC-GENERATOR] [ExecuteLoadItems] UDTs recuperados de la caché: {types?.Count ?? 0}");

                if (types != null)
                {
                    foreach (var t in types)
                    {
                        PlcItems.Add(new VciSelectableItem
                        {
                            OriginalItem = t.Type,
                            IsSelected = false,
                            Name = t.Name,
                            SimpleType = "UDT",
                            FolderPath = t.FolderPath,
                            Number = 0,
                            ProgrammingLanguage = "UDT",
                            CanUpdateDependencies = false,
                            IsExportable = true
                        });
                    }
                }

                // Comprobación de seguridad
                if (PlcItems.Count == 0)
                {
                    _statusService.Set("[VCI-DOC-GENERATOR] [ExecuteLoadItems] ATENCIÓN: La lista resultante está vacía. Verifica que has seleccionado un PLC arriba.", StatusType.Warning);
                    return;
                }

                _logService.Write("[VCI-DOC-GENERATOR] [ExecuteLoadItems] Ordenando elementos para la vista...");

                // Ordenación visual: 
                // 1º Por Tipo de elemento (DB, FB, FC, UDT...)
                // 2º Por Número de bloque
                // 3º Por Nombre
                var sortedList = PlcItems
                    .OrderBy(i => i.SimpleType)
                    .ThenBy(i => i.Number)
                    .ThenBy(i => i.Name)
                    .ToList();

                PlcItems.Clear();
                foreach (var item in sortedList)
                {
                    PlcItems.Add(item);
                }

                _statusService.Set($"[VCI-DOC-GENERATOR] [ExecuteLoadItems] Refresco completado exitosamente. Se han cargado {PlcItems.Count} elementos en el generador.", StatusType.Ok);
            }
            catch (System.Exception ex)
            {
                _statusService.Set($"[VCI-DOC-GENERATOR] [ExecuteLoadItems] Error al cargar los elementos del PLC: {ex.Message}", StatusType.Error);
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
        private bool CanExecuteGenerateDoc()
        {
            // Devuelve 'true' solo si la lista no es nula y al menos 1 elemento está seleccionado
            return PlcItems != null && PlcItems.Any(i => i.IsSelected);
        }


        // ==================================================================================================================
        /// <summary>
        /// Orquesta la exportación a texto plano y dispara el compilador estático de Python.
        /// </summary>
        private async Task GenerateDocAsync()
        {
            var selectedItems = PlcItems.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                _statusService.Set("[VCI-DOC-GENERATOR] [GenerateDocAsync] No hay elementos seleccionados para generar documentación.", StatusType.Warning);
                return;
            }

            try
            {
                _statusService.Set($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Iniciando exportación de {selectedItems.Count} fuentes a disco...", StatusType.Warning);
                await Task.Delay(50);

                // Leer la configuración global desde el XML/JSON
                var globalSettings = _appConfigService.GetGlobalSettings();
                string exportDir = globalSettings.DocExportSourcesPath;

                // Fallback de seguridad: si la ruta del XML está vacía, usamos la temporal por defecto
                if (string.IsNullOrWhiteSpace(exportDir))
                {
                    exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ZC_Exportaciones_SCL");
                    _statusService.Set("[VCI-DOC-GENERATOR] [GenerateDocAsync] Aviso: Ruta de exportación no definida en ajustes. Guardando en el Escritorio.", StatusType.Warning);
                    await Task.Delay(1500); // Pausa para que el usuario lea la advertencia
                }

                // 1. Limpiar directorio de exportación (Por si hay archivos viejos)               
                if (!Directory.Exists(exportDir))
                {
                    Directory.CreateDirectory(exportDir);
                }
                Directory.CreateDirectory(exportDir);

                int successCount = 0;
                int errorCount = 0;

                // 2. Bucle de exportación
                for (int i = 0; i < selectedItems.Count; i++)
                {
                    var item = selectedItems[i];

                    _statusService.Set($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Generando fuente: {item.Name} ({i + 1}/{selectedItems.Count})...", StatusType.Warning);
                    await Task.Delay(10); // Pausa visual por iteración

                    // Determinar la extensión correcta según el tipo de bloque
                    string extension = ".scl";

                    if (item.SimpleType == "UDT")
                    {
                        extension = ".udt";
                    }
                    else if (item.SimpleType == "DB")
                    {
                        extension = ".db";
                    }
                    else if (item.OriginalItem is PlcBlock block)
                    {
                        // Para FCs, FBs y OBs, comprobamos el lenguaje de programación nativo
                        if (block.ProgrammingLanguage == ProgrammingLanguage.STL)
                            extension = ".awl";
                        else
                            extension = ".scl";
                    }

                    string safeName = string.Join("_", item.Name.Split(Path.GetInvalidFileNameChars()));
                    string filePath = Path.Combine(exportDir, $"{safeName}{extension}");

                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    // Llamamos a nuestra nueva función en TiaPlcImportExportService
                    bool isSuccess = await _importExportService.ExportAsSourceAsync(item.OriginalItem, filePath);

                    if (isSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        errorCount++;
                    }
                }

                _statusService.Set($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Exportación de fuentes finalizada. Correctos: {successCount} | Errores: {errorCount}", StatusType.Ok);

                await Task.Delay(500);

                // 3. FASE 3 PARTE 2: Llamada a Python
                _logService.Write("[VCI-DOC-GENERATOR] [GenerateDocAsync] Preparando llamada al entorno de Python...");

                // Asegúrate de tener estas propiedades en tu ConfigGlobalSettings
                string wordPath = globalSettings.DocWordManualPath;
                string destinoHtml = globalSettings.DocOutputPath;

                // Verificaciones de seguridad antes de lanzar
                /*if (!File.Exists(exePath))
                {
                    _logService.Write($"[VCI-DOC-GENERATOR] ERROR: No se encuentra el ejecutable en: {exePath}", true);
                    _statusService.Set("Falta el ejecutable del generador. Revisa configuración.", StatusType.Error);
                    return;
                }

                if (!File.Exists(wordPath))
                {
                    _logService.Write($"[VCI-DOC-GENERATOR] ERROR: No se encuentra el documento Word base en: {wordPath}", true);
                    _statusService.Set("Falta el documento Word base.", StatusType.Error);
                    return;
                }

                // Creamos directorio destino si no existe
                if (!Directory.Exists(destinoHtml)) Directory.CreateDirectory(destinoHtml);

                // Disparamos el proceso en segundo plano (le pasamos exportDir que es donde acabamos de exportar los SCL)
                bool docSuccess = await StartDocGenerator(exePath, wordPath, exportDir, destinoHtml);

                if (docSuccess)
                {
                    _statusService.Set("¡Documentación generada con éxito!", StatusType.Ok);
                }
                else
                {
                    _statusService.Set("Error compilando la documentación. Revisa el Log.", StatusType.Error);
                }
                */
            }
            catch (Exception ex)
            {
                _statusService.Set($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Error general durante el flujo de exportación: {ex.Message}", StatusType.Error);
            }
        }


        // ==================================================================================================================
        /// <summary>
        /// Lanza el ejecutable de Python para generar el HTML de forma invisible e intercepta su consola.
        /// </summary>
        private async Task<bool> StartDocGenerator(string exePath, string wordPath, string fuentesPath, string destinoPath)
        {
            try
            {
                // Montamos los argumentos respetando el argparse de Python
                string arguments = $"--word \"{wordPath}\" --fuentes \"{fuentesPath}\" --destino \"{destinoPath}\"";

                _logService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] Lanzando: {exePath} {arguments}");
                /*
                // 1. Crear la info de inicio usando la librería del Add-In de Siemens
                var startInfo = new Siemens.Engineering.AddIn.Utilities.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                // 2. Crear el proceso de Siemens
                var myProcess = new Siemens.Engineering.AddIn.Utilities.Process
                {
                    StartInfo = startInfo
                };

                // 3. Suscribirse a los eventos para capturar los "log.info" y "log.error" de Python
                myProcess.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] {e.Data}");
                };

                myProcess.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] {e.Data}", true);
                };

                // 4. Lanzar proceso
                if (myProcess.Start())
                {
                    myProcess.BeginOutputReadLine();
                    myProcess.BeginErrorReadLine();

                    _logService.Write("[VCI-DOC-GENERATOR] [StartDocGenerator] Compilador documentacion ejecutándose en segundo plano...");

                    // Esperar de forma asíncrona a que termine para no bloquear la UI de TIA Portal
                    await Task.Run(() =>
                    {
                        while (!myProcess.HasExited)
                        {
                            myProcess.WaitForExit();
                        }
                    });

                    _logService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] Compilador finalizado con código: {myProcess.ExitCode}");
                    return myProcess.ExitCode == 0;
                }
                */
                return false;
            }
            catch (Exception ex)
            {
                _logService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] Error crítico lanzando Python: {ex.Message}", true);
                return false;
            }
        }
    }
}