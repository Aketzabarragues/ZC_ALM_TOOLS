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
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

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
            GenerateDocCommand = new RelayCommand(ExecuteGenerateDocCommand, CanExecuteGenerateDoc);
        }



        // ==================================================================================================================
        /// <summary>
        /// Obtiene y consolida todos los bloques y tipos de datos (UDTs) desde la caché del PLC activo.
        /// </summary>
        public void LoadItems()
        {
            try
            {
                LogService.Write("[VCI-DOC-GENERATOR] [LoadItems] Iniciando escaneo y refresco de elementos...");
                PlcItems.Clear();

                // 1. Extracción de Bloques (FC, FB, OB, DB)
                var blocks = _tiaPlcService.GetAllBlocks();
                LogService.Write($"[VCI-DOC-GENERATOR] [LoadItems] Bloques recuperados de la caché: {blocks?.Count ?? 0}");

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
                                    Number = b.Number
                                });
                            }
                        }
                        catch (Exception blockEx)
                        {
                            LogService.Write($"[VCI-DOC-GENERATOR] [LoadItems] Error leyendo propiedades del bloque {b.Name}: {blockEx.Message}", true);
                        }                       
                    }
                }

                // Extracción de Tipos de Datos (UDT)
                var types = _tiaPlcService.GetAllTypes();
                LogService.Write($"[VCI-DOC-GENERATOR] [LoadItems] UDTs recuperados de la caché: {types?.Count ?? 0}");

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
                            Number = 0
                        });
                    }
                }

                // Comprobación de seguridad
                if (PlcItems.Count == 0)
                {
                    LogService.Write("[VCI-DOC-GENERATOR] [LoadItems] ATENCIÓN: La lista resultante está vacía. ¿Se ha seleccionado un PLC en el desplegable principal?");
                    StatusService.Set("No se han encontrado bloques ni UDTs. Verifica que has seleccionado un PLC arriba.", StatusType.Warning);
                    return;
                }

                LogService.Write("[VCI-DOC-GENERATOR] [LoadItems] Ordenando elementos para la vista...");

                LogService.Write("[VCI-DOC-GENERATOR] [LoadItems] Ordenando elementos para la vista...");

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

                LogService.Write($"[VCI-DOC-GENERATOR] [LoadItems] Refresco completado exitosamente. Total cargados: {PlcItems.Count}");
                StatusService.Set($"Se han cargado {PlcItems.Count} elementos en el generador de ayuda.", StatusType.Ok);
            }
            catch (System.Exception ex)
            {
                LogService.Write($"[VCI-DOC-GENERATOR] [LoadItems] Error crítico al cargar los elementos: {ex.Message}", true);
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
        private bool CanExecuteGenerateDoc()
        {
            // Devuelve 'true' solo si la lista no es nula y al menos 1 elemento está seleccionado
            return PlcItems != null && PlcItems.Any(i => i.IsSelected);
        }
        // ==================================================================================================================
        /// <summary>
        /// Método envoltorio asíncrono para el botón de generar documentacion de la UI
        /// </summary>
        private async void ExecuteGenerateDocCommand()
        {
            await GenerateDocAsync();
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
                StatusService.Set("No hay elementos seleccionados para generar documentación.", StatusType.Warning);
                return;
            }

            try
            {
                LogService.Write($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Iniciando exportación de {selectedItems.Count} fuentes a disco...");

                // Leer la configuración global desde el XML
                var globalSettings = AppConfigService.GetGlobalSettings();
                string exportDir = globalSettings.DocExportSourcesPath;

                // Fallback de seguridad: si la ruta del XML está vacía, usamos la temporal por defecto
                if (string.IsNullOrWhiteSpace(exportDir))
                {
                    LogService.Write("[VCI-DOC-GENERATOR] [GenerateDocAsync] AVISO: 'DocExportSourcesPath' está vacío en app_config.xml. Usando directorio por defecto.");
                    exportDir = AppConfigService.ExportPath;
                }

                LogService.Write($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Iniciando exportación de {selectedItems.Count} fuentes a disco...");


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

                    StatusService.Set($"Generando fuente: {item.Name} ({i + 1}/{selectedItems.Count})...", StatusType.Warning);
                    await Task.Delay(10);

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

                    // Llamamos a nuestra nueva función en TiaPlcService
                    bool isSuccess = _tiaPlcService.ExportAsSource(item.OriginalItem, filePath);

                    if (isSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        errorCount++;
                    }
                }

                LogService.Write($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Exportación de fuentes finalizada. Correctos: {successCount} | Errores: {errorCount}");
                StatusService.Set($"Fase 1 completada: {successCount} archivos fuente exportados.", StatusType.Ok);

                await Task.Delay(500);

                // 3. FASE 3 PARTE 2: Llamada a Python
                StatusService.Set("Ejecutando script de Python para generar la documentación...", StatusType.Warning);
                LogService.Write("[VCI-DOC-GENERATOR] [GenerateDocAsync] Preparando llamada al entorno de Python...");

                StatusService.Set("Compilando documentación web (Python)...", StatusType.Warning);
                LogService.Write("[VCI-DOC-GENERATOR] [GenerateDocAsync] Preparando llamada al entorno de Python...");

                // Asegúrate de tener estas propiedades en tu ConfigGlobalSettings
                string exePath = globalSettings.DocGeneratorExePath;
                string wordPath = globalSettings.DocWordManualPath;
                string destinoHtml = globalSettings.DocOutputPath;

                // Verificaciones de seguridad antes de lanzar
                if (!File.Exists(exePath))
                {
                    LogService.Write($"[VCI-DOC-GENERATOR] ERROR: No se encuentra el ejecutable en: {exePath}", true);
                    StatusService.Set("Falta el ejecutable del generador. Revisa configuración.", StatusType.Error);
                    return;
                }

                if (!File.Exists(wordPath))
                {
                    LogService.Write($"[VCI-DOC-GENERATOR] ERROR: No se encuentra el documento Word base en: {wordPath}", true);
                    StatusService.Set("Falta el documento Word base.", StatusType.Error);
                    return;
                }

                // Creamos directorio destino si no existe
                if (!Directory.Exists(destinoHtml)) Directory.CreateDirectory(destinoHtml);

                // Disparamos el proceso en segundo plano (le pasamos exportDir que es donde acabamos de exportar los SCL)
                bool docSuccess = await StartDocGenerator(exePath, wordPath, exportDir, destinoHtml);

                if (docSuccess)
                {
                    StatusService.Set("¡Documentación generada con éxito!", StatusType.Ok);
                }
                else
                {
                    StatusService.Set("Error compilando la documentación. Revisa el Log.", StatusType.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"[VCI-DOC-GENERATOR] [GenerateDocAsync] Error general durante el flujo de exportación: {ex.Message}", true);
                StatusService.Set("Error en el proceso de exportación. Revisa los logs.", StatusType.Error);
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

                LogService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] Lanzando: {exePath} {arguments}");

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
                        LogService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] {e.Data}");
                };

                myProcess.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        LogService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] {e.Data}", true);
                };

                // 4. Lanzar proceso
                if (myProcess.Start())
                {
                    myProcess.BeginOutputReadLine();
                    myProcess.BeginErrorReadLine();

                    LogService.Write("[VCI-DOC-GENERATOR] [StartDocGenerator] Compilador Python ejecutándose en segundo plano...");

                    // Esperar de forma asíncrona a que termine para no bloquear la UI de TIA Portal
                    await Task.Run(() =>
                    {
                        while (!myProcess.HasExited)
                        {
                            myProcess.WaitForExit();
                        }
                    });

                    LogService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] Compilador finalizado con código: {myProcess.ExitCode}");
                    return myProcess.ExitCode == 0;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[VCI-DOC-GENERATOR] [StartDocGenerator] Error crítico lanzando Python: {ex.Message}", true);
                return false;
            }
        }



    }
}