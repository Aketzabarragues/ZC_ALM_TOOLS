using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Generator
{
    public class ProjectedBlock : ObservableObject
    {
        public string Tipo { get; set; }
        public int NumeroProyectado { get; set; }
        public string NombreProyectado { get; set; }
        public string ArchivoOrigen { get; set; }

        private SynchronizationStatus _estado = SynchronizationStatus.Pending;
        public SynchronizationStatus Estado
        {
            get => _estado;
            set { _estado = value; OnPropertyChanged(); }
        }

        private string _mensaje = "Esperando comprobación...";
        public string Mensaje
        {
            get => _mensaje;
            set { _mensaje = value; OnPropertyChanged(); }
        }
    }

    public class ProcessGeneratorViewModel : ObservableObject
    {
        private TiaPlcService _tiaPlcService;
        private ConfigProcessSettings _processSettings;
        private Dictionary<string, List<object>> _engineeringCache;
        private ConfigGlobalSettings _globalSettings;

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


        // Comandos
        public RelayCommand CompareCommand { get; set; }
        public RelayCommand GenerateCommand { get; set; }
        public RelayCommand RefreshTemplatesCommand { get; set; }




        // ==================================================================================================================
        // CONSTRUCTOR
        public ProcessGeneratorViewModel(TiaPlcService tiaPlcService)
        {

            _tiaPlcService = tiaPlcService;

            // El botón Comparar solo se habilita si hay bloques en la lista
            CompareCommand = new RelayCommand(ExecuteCompare, () => ProjectedBlocks.Count > 0);
            GenerateCommand = new RelayCommand(ExecuteGenerate, () => CanGenerate);
            RefreshTemplatesCommand = new RelayCommand(() => LoadTemplates(_globalSettings));
        }



        // ==================================================================================================================
        // 1. RELLENAR LA TABLA (AUTOMÁTICO) - No interacciona con TIA Portal
        private void UpdateProjections()
        {
            ProjectedBlocks.Clear();
            CanGenerate = false;

            if (SelectedProcess == null || string.IsNullOrEmpty(SelectedTemplate) || _globalSettings == null)
            {
                StatusService.Set("Esperando selección de proceso y plantilla...", StatusType.Warning);
                return;
            }

            if (!int.TryParse(SelectedProcess.Id, out int processId)) return;

            string templateIdStr = SelectedTemplate.Split('_')[0];
            if (!int.TryParse(templateIdStr, out int templateId)) return;

            string templateRootPath = Path.Combine(_globalSettings.ProcessTemplatePath, SelectedTemplate);
            string bloquesPath = Path.Combine(templateRootPath, "Bloques");

            if (!Directory.Exists(bloquesPath))
            {
                StatusService.Set($"Falta la carpeta 'Bloques' en la plantilla.", StatusType.Error);
                return;
            }

            // Escaneo de archivos
            Regex regex = new Regex(@"^(FC|FB|DB)(\d+)", RegexOptions.IgnoreCase);
            string[] archivosXml = Directory.GetFiles(bloquesPath, "*.xml", SearchOption.AllDirectories);

            foreach (var archivoPath in archivosXml)
            {
                string nombreArchivo = Path.GetFileNameWithoutExtension(archivoPath);
                Match match = regex.Match(nombreArchivo);

                if (match.Success)
                {
                    string tipoBloque = match.Groups[1].Value.ToUpper();
                    int numeroOriginal = int.Parse(match.Groups[2].Value);
                    int numeroProyectado = numeroOriginal - templateId + processId;

                    ProjectedBlocks.Add(new ProjectedBlock
                    {
                        Tipo = tipoBloque,
                        NumeroProyectado = numeroProyectado,
                        NombreProyectado = $"{tipoBloque}{numeroProyectado}",
                        ArchivoOrigen = nombreArchivo + ".xml",
                        Estado = SynchronizationStatus.Pending,
                        Mensaje = "Pendiente de comprobar..."
                    });
                }
            }

            // Tabla de variables
            ProjectedBlocks.Add(new ProjectedBlock
            {
                Tipo = "Tabla",
                NumeroProyectado = 0,
                NombreProyectado = $"{SelectedProcess.Id}_{SelectedProcess.Nombre}",
                ArchivoOrigen = "Generación Dinámica",
                Estado = SynchronizationStatus.Pending,
                Mensaje = "Pendiente de comprobar..."
            });

            StatusService.Set($"Lista cargada. Pulsa 'Comparar con PLC' para validar los {ProjectedBlocks.Count} elementos.", StatusType.Ok);
        }



        // ==================================================================================================================
        // 2. LA COMPARACIÓN CON TIA PORTAL (Botón)
        private async void ExecuteCompare()
        {
            if (_tiaPlcService == null || ProjectedBlocks.Count == 0) return;

            StatusService.SetBusy(true);
            StatusService.Set("Validando contra TIA Portal...", StatusType.Ok);
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
                        StatusService.Set($"Error: Falta el bloque '{depLimpia}' en el PLC (Dependencia de la plantilla).", StatusType.Error);
                        StatusService.SetBusy(false);
                        CanGenerate = false;
                        return;
                    }
                }
            }

            // 2. BUCLE DE MATRÍCULAS
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
                StatusService.Set("Colisiones detectadas. Revisa la lista en pantalla.", StatusType.Error);
                CanGenerate = false;
            }
            else
            {
                StatusService.Set($"Vía libre. {ProjectedBlocks.Count} elementos listos para proyectar.", StatusType.Ok);
                CanGenerate = true;
            }

            StatusService.SetBusy(false);
        }



        // ==================================================================================================================
        // 3. LA GENERACIÓN (Botón final)
        private async void ExecuteGenerate()
        {
            StatusService.SetBusy(true);
            StatusService.Set($"Generando proceso {SelectedProcess.Nombre}...", StatusType.Warning);
            LogService.Write($"[GENERATOR-VM] === INICIANDO GENERACIÓN DEL PROCESO {SelectedProcess.Nombre} ===");

            try
            {
                await Task.Delay(500); // Simulamos trabajo por ahora

                // TODO: FASE 2 -> Modificar XMLs de la plantilla e inyectar tabla de variables.

                StatusService.Set("¡Generación completada con éxito!", StatusType.Ok);
            }
            catch (System.Exception ex)
            {
                StatusService.Set($"Error al generar: {ex.Message}", StatusType.Error);
                LogService.Write($"[GENERATOR-VM] Error Crítico: {ex.Message}", true);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }



        // ==================================================================================================================
        // 
        public void LoadData(Dictionary<string, List<object>> cache, ConfigProcessSettings settings, ConfigGlobalSettings globalSettings)
        {
            _engineeringCache = cache;
            _processSettings = settings;
            _globalSettings = globalSettings;

            if (_engineeringCache.TryGetValue(_processSettings.ProcessName, out var procList))
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
        // 
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
        // 
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
                StatusService.Set($"Ruta de plantillas no encontrada: {templatePath}", StatusType.Warning);
            }
        }
    }
}