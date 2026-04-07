using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Siemens.Engineering;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Models.Vci;
using ZC_ALM_TOOLS.Services;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel principal del módulo VCI. Gestiona el entorno de trabajo (Workspace) global para todas sus pestañas.
    /// </summary>
    public class VciMainViewModel : ObservableObject
    {
        // =================================================================================================================
        // Tia portal
        private readonly Project _tiaproject;
        private readonly TiaPortal _tiaPortal;
        private readonly TiaVciService _tiaVciService;

        private readonly TargetStateService _targetStateService;
        public ObservableCollection<TiaTarget> PlcTargets => _targetStateService.PlcTargets;

        public ObservableCollection<VciWorkspaceModel> ConfiguredWorkspaces { get; set; }

        // ViewModels Hijos
        public VciMappingViewModel MappingVM { get; }
        public VciAuditViewModel AuditVM { get; }
        public VciDocGeneratorViewModel DocGeneratorVM { get; }

        private TiaTarget _selectedTarget;
        public TiaTarget SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                _selectedTarget = value;
                OnPropertyChanged();
                NotifyPlcChanged();
            }
        }

        private VciWorkspaceModel _selectedWorkspace;
        public VciWorkspaceModel SelectedWorkspace
        {
            get => _selectedWorkspace;
            set
            {
                _selectedWorkspace = value;
                OnPropertyChanged();

                if (MappingVM != null)
                {
                    MappingVM.WorkspacePath = _selectedWorkspace?.Path;
                    MappingVM.WorkspaceName = _selectedWorkspace?.Name;
                }
            }
        }

        private bool _isCreatingWorkspace;
        public bool IsCreatingWorkspace
        {
            get => _isCreatingWorkspace;
            set { _isCreatingWorkspace = value; OnPropertyChanged(); }
        }

        private string _newWorkspaceName;
        public string NewWorkspaceName
        {
            get => _newWorkspaceName;
            set { _newWorkspaceName = value; OnPropertyChanged(); }
        }

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;

        // Comandos
        public AsyncRelayCommand ToggleCreateModeCommand { get; }
        public AsyncRelayCommand CreateNewWorkspaceCommand { get; }
        public AsyncRelayCommand DeleteWorkspaceCommand { get; }
        public AsyncRelayCommand ChangeWorkspaceFolderCommand { get; }


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciMainViewModel(TiaPortal tiaPortal,
            Project project,
            TiaVciService tiaVciService,
            TargetStateService targetStateService,
            VciMappingViewModel mappingVM,
            VciAuditViewModel auditVM,
            VciDocGeneratorViewModel docGeneratorVM,
            ILogService logService,
            IStatusService statusService)
        {
            _tiaPortal = tiaPortal;
            _tiaproject = project;
            _tiaVciService = tiaVciService;
            _targetStateService = targetStateService;
            _logService = logService;
            _statusService = statusService;

            _logService.Write("[VCI-MAIN-VM] [VciMainViewModel] Inicializando VciMainViewModel...");

            // Inicializamos viewmodels hijos
            MappingVM = mappingVM;
            AuditVM = auditVM;
            DocGeneratorVM = docGeneratorVM;

            ConfiguredWorkspaces = new ObservableCollection<VciWorkspaceModel>();

            // Enlazamos a la tarea asíncrona
            ToggleCreateModeCommand = new AsyncRelayCommand(ExecuteToggleCreateMode);
            CreateNewWorkspaceCommand = new AsyncRelayCommand(ExecuteCreateNewWorkspace, CanExecuteCreateNewWorkspace);
            DeleteWorkspaceCommand = new AsyncRelayCommand(ExecuteDeleteWorkspace, CanExecuteDeleteWorkspace);
            ChangeWorkspaceFolderCommand = new AsyncRelayCommand(ExecuteChangeWorkspaceFolder, CanExecuteChangeWorkspaceFolder);

            // Cargamos los datos de TIA Portal al iniciar
            LoadWorkspaces();
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar el PLC de trabajo cuando el usuario cambia la selección desde arriba
        /// </summary>
        private void NotifyPlcChanged()
        {
            if (SelectedTarget != null && SelectedTarget.SoftwareObject is Siemens.Engineering.SW.PlcSoftware plc)
            {
                // Avisamos a las pestañas hijas
                MappingVM?.NotifyPlcChanged(SelectedTarget.Name);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para cargar todos los Workspaces en el proyecto
        /// </summary>
        private void LoadWorkspaces()
        {
            var workspaces = _tiaVciService.GetConfiguredWorkspaces();
            ConfiguredWorkspaces.Clear();

            foreach (var ws in workspaces)
            {
                ConfiguredWorkspaces.Add(ws);
            }

            if (ConfiguredWorkspaces.Any())
            {
                SelectedWorkspace = ConfiguredWorkspaces.First();
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para conmutar la visibilidad del panel de creación
        /// </summary>
        private Task ExecuteToggleCreateMode()
        {
            IsCreatingWorkspace = !IsCreatingWorkspace;
            if (IsCreatingWorkspace) NewWorkspaceName = ""; // Limpiar al entrar
            return Task.CompletedTask;
        }



        // ==================================================================================================================
        private bool CanExecuteCreateNewWorkspace() => !string.IsNullOrWhiteSpace(NewWorkspaceName);
        /// <summary>
        /// Metodo para crear un Workspace nuevo de forma asíncrona
        /// </summary>
        private async Task ExecuteCreateNewWorkspace()
        {
            OpenFileDialog folderDialog = new OpenFileDialog
            {
                Title = "Selecciona la carpeta raíz para el Workspace y pulsa 'Abrir'",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Seleccionar esta carpeta",
                Filter = "Carpetas|*.ocultar"
            };

            if (folderDialog.ShowDialog() == true)
            {
                string chosenPath = Path.GetDirectoryName(folderDialog.FileName);
                _statusService.Set($"[VCI-MAIN] Creando Workspace '{NewWorkspaceName}' en TIA Portal...", StatusType.Warning);

                await Task.Delay(50); // Pausa visual

                try
                {
                    var newWs = _tiaVciService.CreateWorkspace(NewWorkspaceName, chosenPath);

                    if (newWs != null)
                    {
                        ConfiguredWorkspaces.Add(newWs);
                        SelectedWorkspace = newWs;
                        _statusService.Set($"[VCI-MAIN] Workspace creado y seleccionado correctamente.", StatusType.Ok);
                        IsCreatingWorkspace = false; // Volver al modo normal
                    }
                    else
                    {
                        _statusService.Set("[VCI-MAIN] Error al crear el Workspace. Revisa los logs.", StatusType.Error);
                    }
                }
                catch (Exception ex)
                {
                    _statusService.Set($"[VCI-MAIN] Excepción al crear el Workspace: {ex.Message}", StatusType.Error);
                }
            }
        }



        // ==================================================================================================================
        private bool CanExecuteDeleteWorkspace() => SelectedWorkspace != null;
        /// <summary>
        /// Borra el Workspace seleccionado tras pedir confirmación al usuario de forma asíncrona
        /// </summary>
        private async Task ExecuteDeleteWorkspace()
        {
            var result = System.Windows.MessageBox.Show(
                $"¿Estás seguro de que deseas desvincular el Workspace '{SelectedWorkspace.Name}' de TIA Portal?\n\n(Tranquilo, no se borrarán los archivos de tu disco duro)",
                "Confirmar borrado",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _statusService.Set($"[VCI-MAIN] Borrando Workspace '{SelectedWorkspace.Name}'...", StatusType.Warning);
                await Task.Delay(50); // Pausa visual

                try
                {
                    bool success = _tiaVciService.DeleteWorkspace(SelectedWorkspace.Name);

                    if (success)
                    {
                        _statusService.Set($"[VCI-MAIN] Workspace borrado correctamente.", StatusType.Ok);

                        // Lo quitamos de la lista visual
                        ConfiguredWorkspaces.Remove(SelectedWorkspace);

                        // Seleccionamos el primero que quede, o null si la lista se queda vacía
                        SelectedWorkspace = ConfiguredWorkspaces.FirstOrDefault();
                    }
                    else
                    {
                        _statusService.Set("[VCI-MAIN] Error al borrar el Workspace. Revisa los logs.", StatusType.Error);
                    }
                }
                catch (Exception ex)
                {
                    _statusService.Set($"[VCI-MAIN] Excepción al borrar el Workspace: {ex.Message}", StatusType.Error);
                }
            }
        }



        // ==================================================================================================================
        private bool CanExecuteChangeWorkspaceFolder() => SelectedWorkspace != null;
        /// <summary>
        /// Cambia la ruta de la carpeta física del Workspace seleccionado de forma asíncrona
        /// </summary>
        private async Task ExecuteChangeWorkspaceFolder()
        {
            OpenFileDialog folderDialog = new OpenFileDialog
            {
                Title = "Selecciona la nueva carpeta raíz para el Workspace",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Seleccionar esta carpeta",
                Filter = "Carpetas|*.ocultar"
            };

            if (folderDialog.ShowDialog() == true)
            {
                string chosenPath = Path.GetDirectoryName(folderDialog.FileName);
                if (chosenPath != SelectedWorkspace.Path)
                {
                    _statusService.Set($"[VCI-MAIN] Cambiando ruta del Workspace a '{chosenPath}'...", StatusType.Warning);
                    await Task.Delay(50); // Pausa visual

                    try
                    {
                        if (_tiaVciService.UpdateWorkspacePath(SelectedWorkspace.Name, chosenPath))
                        {
                            SelectedWorkspace.Path = chosenPath;
                            OnPropertyChanged(nameof(SelectedWorkspace));

                            // Actualizamos la ruta a la vista hija para que reinicie la tabla
                            if (MappingVM != null) MappingVM.WorkspacePath = chosenPath;

                            _statusService.Set("[VCI-MAIN] Ruta del Workspace actualizada correctamente.", StatusType.Ok);
                        }
                        else
                        {
                            _statusService.Set("[VCI-MAIN] Error al cambiar la ruta. Revisa los logs.", StatusType.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        _statusService.Set($"[VCI-MAIN] Excepción al cambiar la ruta: {ex.Message}", StatusType.Error);
                    }
                }
            }
        }
    }
}