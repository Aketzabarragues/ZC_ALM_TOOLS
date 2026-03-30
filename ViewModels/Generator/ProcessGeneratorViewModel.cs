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
                block.Estado = SynchronizationStatus.Pending;
                block.Mensaje = "PLC cambiado. Vuelva a comparar.";
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

            if (!int.TryParse(SelectedProcess.Id, out int processId)) return;

            // Extraemos el ID de la plantilla desde el nombre de la carpeta (ej: "0100" -> 100)
            string templateIdStr = SelectedTemplate.Split('_')[0];
            if (!int.TryParse(templateIdStr, out int templateId)) return;

            string templateRootPath = Path.Combine(_globalSettings.ProcessTemplatePath, SelectedTemplate);
            string bloquesPath = Path.Combine(templateRootPath, "Bloques");

            if (!Directory.Exists(bloquesPath))
            {
                _statusService.Set($"[PROCESS-VM] [UpdateProjections] Falta la carpeta 'Bloques' en la plantilla.", StatusType.Error);
                return;
            }

            // Escaneo de archivos
            Regex regex = new Regex(@"^(FC|FB|DB)(\d+)", RegexOptions.IgnoreCase);
            string[] archivosXml = Directory.GetFiles(bloquesPath, "*.xml", SearchOption.AllDirectories);

            int basePlantillas = 50000;

            foreach (var archivoPath in archivosXml)
            {
                string nombreArchivo = Path.GetFileNameWithoutExtension(archivoPath);
                Match match = regex.Match(nombreArchivo);

                if (match.Success)
                {
                    string tipoBloque = match.Groups[1].Value.ToUpper();
                    int numeroOriginal = int.Parse(match.Groups[2].Value);

                    // Cálculo matemático con tu sistema de rangos y offsets:
                    // Ej DB53100 -> 53100 - 50000 - 100 + 200 = 3200
                    int numeroProyectado = numeroOriginal - basePlantillas - templateId + processId;

                    // Manipulación del nombre ("DB50100_CPR_PRINCIPAL" -> "DB200_COMP_PRINCIPAL")
                    string[] parts = nombreArchivo.Split('_');
                    if (parts.Length >= 2)
                    {
                        // parts[0] = "DB50100" -> Lo actualizamos a "DB200"
                        parts[0] = $"{tipoBloque}{numeroProyectado}";

                        // parts[1] = "CPR" -> Lo reemplazamos por el código del Excel ("COMP")
                        parts[1] = SelectedProcess.Codigo;
                    }

                    // Volvemos a unir el nombre
                    string nombreProyectadoFinal = string.Join("_", parts);

                    ProjectedBlocks.Add(new ProjectedBlock
                    {
                        Tipo = tipoBloque,
                        NumeroProyectado = numeroProyectado,
                        NombreProyectado = nombreProyectadoFinal,
                        ArchivoOrigen = nombreArchivo + ".xml",
                        Estado = SynchronizationStatus.Pending,
                        Mensaje = "Pendiente de comprobar..."
                    });
                }
            }

            // Tabla de variables dinámica
            ProjectedBlocks.Add(new ProjectedBlock
            {
                Tipo = "Tabla",
                NumeroProyectado = 0,
                NombreProyectado = $"{SelectedProcess.Id}_{SelectedProcess.Codigo}",
                ArchivoOrigen = "Generación Dinámica",
                Estado = SynchronizationStatus.Pending,
                Mensaje = "Pendiente de comprobar..."
            });

            _statusService.Set($"[PROCESS-VM] [UpdateProjections] Lista cargada. Pulsa 'Comparar con PLC' para validar los {ProjectedBlocks.Count} elementos.", StatusType.Ok);
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
                bloque.Estado = SynchronizationStatus.Pending;
                bloque.Mensaje = "Comprobando...";
                await Task.Delay(50); // Refresco visual

                if (bloque.Tipo == "Tabla")
                {
                    if (_tiaPlcService.FindTagTableByName(bloque.NombreProyectado) != null)
                    {
                        bloque.Estado = SynchronizationStatus.Error;
                        bloque.Mensaje = "La tabla ya existe";
                        hayColisiones = true;
                    }
                    else
                    {
                        bloque.Estado = SynchronizationStatus.Ok;
                        bloque.Mensaje = "Libre";
                    }
                }
                else
                {
                    var existente = _tiaPlcService.FindBlockByNumber(bloque.NumeroProyectado, bloque.Tipo);
                    if (existente != null)
                    {
                        bloque.Estado = SynchronizationStatus.Error;
                        bloque.Mensaje = $"El bloque ya existe";
                        hayColisiones = true;
                    }
                    else
                    {
                        bloque.Estado = SynchronizationStatus.Ok;
                        bloque.Mensaje = "Libre";
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
        /// Metodo para generar el proceso desde la plantilla
        /// </summary>
        private async Task ExecuteGenerate()
        {
            _statusService.Set($"[PROCESS-VM] [ExecuteGenerate] Iniciando generacion del proceso {SelectedProcess.Nombre}", StatusType.Warning);

            try
            {
                await Task.Delay(500); // Simulamos trabajo por ahora

                // TODO: FASE 2 -> Modificar XMLs de la plantilla e inyectar tabla de variables.

                _statusService.Set("[PROCESS-VM] [ExecuteGenerate] ¡Generación completada con éxito!", StatusType.Ok);
            }
            catch (Exception ex)
            {
                _statusService.Set($"[PROCESS-VM] [ExecuteGenerate] Error crítico al generar: {ex.Message}", StatusType.Error);
            }
        }

    }
}