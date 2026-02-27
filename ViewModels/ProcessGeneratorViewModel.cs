using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Siemens.Engineering.Safety;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{
    public class ProcessGeneratorViewModel : ObservableObject
    {
        private TiaPlcService _tiaPlcService;
        private ConfigProcessSettings _processSettings;
        private Dictionary<string, List<object>> _engineeringCache;
        private ConfigGlobalSettings _globalSettings;

        // --- UI Properties ---
        public ObservableCollection<Process> Processes { get; set; } = new ObservableCollection<Process>();

        private Process _selectedProcess;
        public Process SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                _selectedProcess = value;
                OnPropertyChanged();
                CheckProcessExistence(); // Comprobamos si existe en el PLC al seleccionarlo
            }
        }

        public ObservableCollection<string> Templates { get; set; } = new ObservableCollection<string>();

        private string _selectedTemplate;
        public string SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                _selectedTemplate = value;
                OnPropertyChanged();
                CheckProcessExistence(); // Re-validamos si cambia la plantilla
            }
        }

        private string _statusMessage = "Seleccione un proceso...";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        private string _statusColor = "Transparent";
        public string StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }

        private bool _canGenerate = false;
        public bool CanGenerate { get => _canGenerate; set { _canGenerate = value; OnPropertyChanged(); } }

        // --- Commands ---
        public RelayCommand GenerateCommand { get; set; }
        public RelayCommand RefreshTemplatesCommand { get; set; }

        public ProcessGeneratorViewModel()
        {
            GenerateCommand = new RelayCommand(ExecuteGenerate, () => CanGenerate);
            RefreshTemplatesCommand = new RelayCommand(() => LoadTemplates(_globalSettings));
        }

        public void SetTiaService(TiaPlcService tiaPlcService)
        {
            _tiaPlcService = tiaPlcService;
            CheckProcessExistence();
        }

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
                CheckProcessExistence();
        }







        public void LoadTemplates(ConfigGlobalSettings globalSettings)
        {
            Templates.Clear();

            if (globalSettings == null || string.IsNullOrEmpty(globalSettings.ProcessTemplatePath)) return;

            string templatePath = globalSettings.ProcessTemplatePath;

            if (System.IO.Directory.Exists(templatePath))
            {
                // Leemos solo las carpetas del primer nivel (ej. "21990_PLANTILLA")
                var directories = System.IO.Directory.GetDirectories(templatePath);
                foreach (var dir in directories)
                {
                    Templates.Add(System.IO.Path.GetFileName(dir));
                }

                if (Templates.Count > 0)
                {
                    SelectedTemplate = Templates[0]; // Seleccionamos la primera por defecto
                }
            }
            else
            {
                LogService.Write($"[GENERATOR-VM] ¡ATENCIÓN! No existe la ruta de plantillas: {templatePath}", true);
                StatusMessage = $"Ruta de plantillas no encontrada: {templatePath}";
                StatusColor = "#FFF3CD";
            }
        }

        // ==============================================================================
        // MÉTODOS DE VALIDACIÓN
        // ==============================================================================
        public void NotifyPlcChanged(string plcName)
        {
            CheckProcessExistence();
        }

        private void CheckProcessExistence()
        {
            if (SelectedProcess == null || _tiaPlcService == null || _processSettings == null)
            {
                StatusMessage = "Esperando conexión...";
                StatusColor = "#E0E0E0"; // Gris
                CanGenerate = false;
                return;
            }

            // Convertimos el ID (string) a entero de forma segura
            if (int.TryParse(SelectedProcess.Id, out int processId))
            {
                int expectedDbNumber = processId + 3000;
                string expectedDbName = $"DB{expectedDbNumber}{_processSettings.SuffixDbReal}"; // ej. DB3100_PREAL

                LogService.Write($"[GENERATOR-VM] Buscando si existe el bloque {expectedDbName} para el proceso {SelectedProcess.Nombre}...");

                var existingBlock = _tiaPlcService.FindBlockByName(expectedDbName);

                if (existingBlock != null)
                {
                    StatusMessage = $"El proceso {SelectedProcess.Nombre} ya existe en el PLC (Se detectó {expectedDbName}).";
                    StatusColor = "#F2DEDE"; // Rojo claro
                    CanGenerate = false; // Bloqueamos el botón
                }
                else
                {
                    StatusMessage = $"El proceso {SelectedProcess.Nombre} no existe. Listo para ser generado.";
                    StatusColor = "#DFF0D8"; // Verde claro
                    CanGenerate = true; // Habilitamos el botón
                }
            }
            else
            {
                StatusMessage = $"Error: El ID del proceso '{SelectedProcess.Id}' no es un número válido.";
                StatusColor = "#FFF3CD"; // Naranja clarito
                CanGenerate = false;
            }
        }

        // ==============================================================================
        // MÉTODOS DE ACCIÓN (FASE 2 en adelante)
        // ==============================================================================
        private async void ExecuteGenerate()
        {
            StatusService.SetBusy(true);
            StatusService.Set($"Generando proceso {SelectedProcess.Nombre}...", StatusType.Ok);
            LogService.Write($"[GENERATOR-VM] === INICIANDO GENERACIÓN DEL PROCESO {SelectedProcess.Nombre} ===");

            try
            {
                await Task.Delay(500); // Simulamos trabajo por ahora

                // TODO: FASE 2 -> Modificar XMLs de la plantilla e inyectar tabla de variables.

                StatusService.Set("¡Generación (Simulada) completada!", StatusType.Ok);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }
    }
}