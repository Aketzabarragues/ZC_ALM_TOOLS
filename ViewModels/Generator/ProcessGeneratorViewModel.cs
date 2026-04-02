using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Siemens.Engineering.Hmi;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel que gestiona la pestaña de procesos
    /// </summary>
    public class ProcessGeneratorViewModel : ObservableObject
    {
        // ==================================================================================================================
        // Tia portal
        private TiaPlcService _tiaPlcService;
        private TiaHmiService _tiaHmiService;

        private ObservableCollection<TiaTarget> _hmiTargets { get; set; }

        private ConfigProcessSettings _processSettings;
        private Dictionary<string, List<object>> _engineeringCache;
        private ConfigGlobalSettings _globalSettings;
        private ConfigNetworkSettings _configNetworkSettings;

        public ObservableCollection<Process> Processes { get; } = new ObservableCollection<Process>();
        public ObservableCollection<string> Templates { get; } = new ObservableCollection<string>();
        public ObservableCollection<ProjectedBlock> ProjectedBlocks { get; } = new ObservableCollection<ProjectedBlock>();


        private Process _selectedProcess;
        public Process SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                _selectedProcess = value;
                OnPropertyChanged();
                UpdateProjections(); // Al cambiar, solo rellena la tabla (sin consultar TIA)
            }
        }


        private string _selectedTemplate;
        public string SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                _selectedTemplate = value;
                OnPropertyChanged();
                UpdateProjections(); // Al cambiar, solo rellena la tabla
            }
        }

        private bool _canGenerate = false;
        public bool CanGenerate { get => _canGenerate; set { _canGenerate = value; OnPropertyChanged(); } }

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;
        private readonly IAppConfigService _appConfigService;

        // Comandos
        public AsyncRelayCommand CompareCommand { get; set; }
        public AsyncRelayCommand GenerateCommand { get; set; }
        public AsyncRelayCommand RefreshTemplatesCommand { get; set; }



        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public ProcessGeneratorViewModel(TiaPlcService tiaPlcService,
                                          TiaHmiService tiaHmiService,
                                          TargetStateService targetStateService,
                                          ILogService logService,
                                          IStatusService statusService,
                                          IAppConfigService appConfigService)
        {
            _tiaPlcService = tiaPlcService;
            _tiaHmiService = tiaHmiService;

            _logService = logService;
            _statusService = statusService;
            _appConfigService = appConfigService;

            _hmiTargets = targetStateService.HmiTargets;

            // Enlazamos directamente a las tareas asíncronas
            CompareCommand = new AsyncRelayCommand(ExecuteCompare, () => ProjectedBlocks.Count > 0);
            GenerateCommand = new AsyncRelayCommand(ExecuteGenerate, () => CanGenerate);
            RefreshTemplatesCommand = new AsyncRelayCommand(ExecuteRefreshTemplates);

            LoadTemplates(_appConfigService.GetGlobalSettings());
        }



        // ==================================================================================================================
        /// <summary>
        /// Carga los datos provenientes del MainViewModel
        /// </summary>
        public void LoadData(Dictionary<string, List<object>> cache, ConfigProcessSettings settings, ConfigGlobalSettings globalSettings, ConfigNetworkSettings NetworkSettings)
        {
            _engineeringCache = cache;
            _processSettings = settings;
            _globalSettings = globalSettings;
            _configNetworkSettings = NetworkSettings;
            if (_engineeringCache.TryGetValue(_processSettings.Name, out var procList))
            {
                Processes.Clear();
                foreach (var proc in procList.Cast<Process>())
                {
                    Processes.Add(proc);
                }
            }

            if (Processes.Count > 0 && SelectedProcess == null)
                SelectedProcess = Processes[0];
            else
                UpdateProjections();
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar que la selección del PLC ha cambiado
        /// </summary>
        public void NotifyPlcChanged(string plcName)
        {
            // Si cambian de PLC, obligamos a que vuelvan a comparar
            foreach (var block in ProjectedBlocks)
            {
                block.Status = SynchronizationStatus.Pending;
                block.Message = "PLC cambiado. Vuelva a comparar.";
            }
            CanGenerate = false;
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para cargar los archivos de la carpeta de plantillas
        /// </summary>
        public void LoadTemplates(ConfigGlobalSettings globalSettings)
        {
            Templates.Clear();

            if (globalSettings != null) _globalSettings = globalSettings;
            if (_globalSettings == null || string.IsNullOrEmpty(_globalSettings.ProcessTemplatePath)) return;

            string templatePath = _globalSettings.ProcessTemplatePath;

            if (Directory.Exists(templatePath))
            {
                var directories = Directory.GetDirectories(templatePath);
                foreach (var dir in directories) Templates.Add(Path.GetFileName(dir));
                if (Templates.Count > 0) SelectedTemplate = Templates[0];
            }
            else
            {
                _statusService.Set($"[PROCESS-VM] [LoadTemplates] Ruta de plantillas no encontrada: {templatePath}", StatusType.Warning);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Método envoltorio para refrescar plantillas dentro de la infraestructura asíncrona
        /// </summary>
        private Task ExecuteRefreshTemplates()
        {
            LoadTemplates(_globalSettings);
            return Task.CompletedTask;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para rellenar la tabla con los bloques a generar
        /// </summary>
        private void UpdateProjections()
        {
            ProjectedBlocks.Clear();
            CanGenerate = false;

            if (SelectedProcess == null || string.IsNullOrEmpty(SelectedTemplate) || _globalSettings == null)
            {
                _statusService.Set("[PROCESS-VM] [UpdateProjections] Esperando selección de proceso y plantilla...", StatusType.Warning);
                return;
            }

            try
            {
                // Delegamos toda la lógica de negocio, Regex y File I/O al servicio
                var calculatedBlocks = _tiaPlcService.CalculateProjectedBlocks(
                    _globalSettings.ProcessTemplatePath,
                    SelectedTemplate,
                    SelectedProcess.Id,
                    SelectedProcess.Codigo);

                if (calculatedBlocks.Count == 0)
                {
                    _statusService.Set("[PROCESS-VM] [UpdateProjections] No se encontraron archivos XML válidos en la plantilla.", StatusType.Warning);
                    return;
                }

                // Alimentamos la ObservableCollection para que WPF renderice la tabla
                foreach (var block in calculatedBlocks)
                {
                    ProjectedBlocks.Add(block);
                }

                _statusService.Set($"[PROCESS-VM] [UpdateProjections] Lista cargada. Pulsa 'Comparar con PLC' para validar los {ProjectedBlocks.Count} elementos.", StatusType.Ok);
            }
            catch (DirectoryNotFoundException dirEx)
            {
                _statusService.Set($"[PROCESS-VM] [UpdateProjections] Error de plantilla: {dirEx.Message}", StatusType.Error);
            }
            catch (Exception ex)
            {
                _statusService.Set($"[PROCESS-VM] [UpdateProjections] Error al calcular las proyecciones: {ex.Message}", StatusType.Error);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para comparar con el PLC de los bloques a añadir
        /// </summary>
        private async Task ExecuteCompare()
        {
            if (_tiaPlcService == null || ProjectedBlocks.Count == 0) return;

            _statusService.Set("[PROCESS-VM] [ExecuteCompare] Validando contra TIA Portal...", StatusType.Ok);
            bool hayColisiones = false;

            // 1. CHECK DEL MANIFIESTO (Bloques estándar)
            string templateRootPath = Path.Combine(_globalSettings.ProcessTemplatePath, SelectedTemplate);
            string dependenciesFile = Path.Combine(templateRootPath, "dependencias.txt");

            if (File.Exists(dependenciesFile))
            {
                var lineas = File.ReadAllLines(dependenciesFile);
                foreach (var dependecia in lineas)
                {
                    string depLimpia = dependecia.Trim();
                    if (string.IsNullOrEmpty(depLimpia)) continue;

                    if (_tiaPlcService.FindBlockByName(depLimpia) == null)
                    {
                        _statusService.Set($"[PROCESS-VM] [ExecuteCompare] Error: Falta el bloque '{depLimpia}' en el PLC (Dependencia de la plantilla).", StatusType.Error);
                        CanGenerate = false;
                        return;
                    }
                }
            }

            // BUCLE DE MATRÍCULAS
            foreach (var bloque in ProjectedBlocks)
            {
                bloque.Status = SynchronizationStatus.Pending;
                bloque.Message = "Comprobando...";
                await Task.Delay(50); // Refresco visual

                if (bloque.Type == "Tabla")
                {
                    if (_tiaPlcService.FindTagTableByName(bloque.ProjectedName) != null)
                    {
                        bloque.Status = SynchronizationStatus.Error;
                        bloque.Message = "La tabla ya existe";
                        hayColisiones = true;
                    }
                    else
                    {
                        bloque.Status = SynchronizationStatus.Ok;
                        bloque.Message = "Libre";
                    }
                }
                else
                {
                    var existente = _tiaPlcService.FindBlockByNumber(bloque.ProjectedNumber, bloque.Type);
                    if (existente != null)
                    {
                        bloque.Status = SynchronizationStatus.Error;
                        bloque.Message = $"El bloque ya existe";
                        hayColisiones = true;
                    }
                    else
                    {
                        bloque.Status = SynchronizationStatus.Ok;
                        bloque.Message = "Libre";
                    }
                }
            }

            if (hayColisiones)
            {
                _statusService.Set("[[PROCESS-VM] [ExecuteCompare] Colisiones detectadas. Revisa la lista en pantalla.", StatusType.Error);
                CanGenerate = false;
            }
            else
            {
                _statusService.Set($"[PROCESS-VM] [ExecuteCompare] Vía libre. {ProjectedBlocks.Count} elementos listos para proyectar.", StatusType.Ok);
                CanGenerate = true;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para generar el proceso (Cirugía XML Profunda + Importación con Rollback)
        /// </summary>
        private async Task ExecuteGenerate()
        {
            _statusService.Set($"[PROCESS-VM] [ExecuteGenerate] Iniciando generación del proceso {SelectedProcess.Nombre}...", StatusType.Warning);

            try
            {
                string tempDirectory = AppConfigService.TempExportPathNewProcess;
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
                Directory.CreateDirectory(tempDirectory);

                // 1. CALCULAMOS LAS BASES Y PREPARAMOS TODOS LOS BLOQUES DE GOLPE
                _statusService.Set("[PROCESS-VM] [ExecuteGenerate] Realizando cirugía XML offline de los bloques...", StatusType.Ok);

                int templateId = int.Parse(SelectedTemplate.Split('_')[0]); // Ej: Saca el 100 de "100_Plantilla"
                string originalBaseStr = (50000 + templateId).ToString();   // Ej: "50100"
                string projectedBaseStr = SelectedProcess.Id;               // Ej: "3100" (O la propiedad que contenga el número base real)

                var allBlocks = ProjectedBlocks.ToList();
                foreach (var block in allBlocks) block.Status = SynchronizationStatus.Pending;

                // EJECUTAMOS LA CIRUGÍA CON TODOS LOS BLOQUES JUNTOS PARA NO PERDER REFERENCIAS
                var allProcessedDict = _tiaPlcService.PrepareBlocksForImport(
                    allBlocks,
                    tempDirectory,
                    SelectedProcess.Codigo);

                // 2. FILTRAMOS LA TABLA DE VARIABLES Y LA SEPARAMOS DEL RESTO
                var tagTableBlock = ProjectedBlocks.FirstOrDefault(b => b.Type == "Tabla");
                string targetTableName = string.Empty;

                if (tagTableBlock != null)
                {
                    _statusService.Set("[PROCESS-VM] [ExecuteGenerate] Procesando Tabla de Variables...", StatusType.Warning);

                    string tableXmlPath = Path.Combine(tempDirectory, $"{tagTableBlock.ProjectedName}.xml");
                    targetTableName = tagTableBlock.ProjectedName;

                    // IMPORTAMOS LA TABLA DE VARIABLES PRIMERO
                    bool isTableImported = await _tiaPlcService.ImportTagTableAsync(tableXmlPath, tagTableBlock.PlcGroupPath);

                    if (!isTableImported)
                    {
                        _statusService.Set("[PROCESS-VM] [ExecuteGenerate] Abortando: Falló la importación de la Tabla de Variables.", StatusType.Error);
                        tagTableBlock.Status = SynchronizationStatus.Error;
                        return;
                    }

                    tagTableBlock.Status = SynchronizationStatus.Ok;
                    tagTableBlock.Message = "Tabla inyectada";

                    // Retiramos la tabla del diccionario global para no intentar importarla después como un bloque de código
                    allProcessedDict.Remove(tableXmlPath);
                }

                await Task.Delay(25);

                // 3 y 4. IMPORTAMOS EL RESTO DE BLOQUES LÓGICOS (DB, FC, FB)
                var logicBlocks = ProjectedBlocks.Where(b => b.Type != "Tabla").ToList();

                _statusService.Set("[PROCESS-VM] [ExecuteGenerate] Importando bloques a TIA Portal...", StatusType.Warning);

                // Ahora allProcessedDict ya NO tiene la tabla, solo los bloques lógicos, y le pasamos el nombre de la tabla para enlazar
                // Añadimos la ruta de la carpeta de la tabla de variables como tercer parámetro
                bool isImportSuccessful = await _tiaPlcService.ImportBlocksMassivelyAsync(
                    allProcessedDict,
                    targetTableName,
                    tagTableBlock != null ? tagTableBlock.PlcGroupPath : "");

                if (!isImportSuccessful)
                {
                    _statusService.Set("[PROCESS-VM] [ExecuteGenerate] Falló la importación. Se ha ejecutado Rollback.", StatusType.Error);
                    foreach (var block in logicBlocks) block.Status = SynchronizationStatus.Error;
                    return;
                }

                // 5. COMPILAMOS TODO
                _statusService.Set("[PROCESS-VM] [ExecuteGenerate] Compilando dependencias en TIA Portal...", StatusType.Warning);
                foreach (var block in logicBlocks) block.Message = "Compilando...";

                bool isCompilationSuccessful = await _tiaPlcService.CompileSoftwareAsync();

                foreach (var block in logicBlocks)
                {
                    block.Status = isCompilationSuccessful ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                    block.Message = isCompilationSuccessful ? "Generado y Compilado" : "Error en Compilación";
                }

                if (isCompilationSuccessful)
                {
                    _statusService.Set($"[PROCESS-VM] [ExecuteGenerate] ¡Proceso {SelectedProcess.Nombre} inyectado con éxito!", StatusType.Ok);
                    CanGenerate = false;
                }
                else
                {
                    _statusService.Set("[PROCESS-VM] [ExecuteGenerate] Falló la importación. Se ha ejecutado Rollback.", StatusType.Error);

                    foreach (var block in logicBlocks)
                    {
                        block.Status = SynchronizationStatus.Error;
                        block.Message = "Rollback ejecutado";
                    }

                    if (tagTableBlock != null)
                    {
                        tagTableBlock.Status = SynchronizationStatus.Error;
                        tagTableBlock.Message = "Rollback ejecutado";
                    }

                    return;
                }
            }
            catch (Exception ex)
            {
                _statusService.Set($"[PROCESS-VM] [ExecuteGenerate] Error crítico al generar: {ex.Message}", StatusType.Error);
            }
        }


    }
}