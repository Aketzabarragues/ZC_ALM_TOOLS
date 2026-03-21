using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private TiaPlcService _tiaPlcService;
        private TiaVciService _tiaVciService;

        public ObservableCollection<TiaTarget> PlcTargets { get; set; }


        public ObservableCollection<VciWorkspaceModel> ConfiguredWorkspaces { get; set; }

        // ViewModels Hijos
        public VciMappingViewModel MappingVM { get; set; }
        public VciAuditViewModel AuditVM { get; set; }
        public VciAuditViewModel DocGeneratorVM { get; set; }


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

        // Comandos
        public RelayCommand ToggleCreateModeCommand { get; }
        public RelayCommand CreateNewWorkspaceCommand { get; }
        public RelayCommand DeleteWorkspaceCommand { get; }
        public RelayCommand ChangeWorkspaceFolderCommand { get; }

        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciMainViewModel(TiaPortal tiaPortal, Project project, TiaPlcService tiaPlcService, TiaVciService tiaVciService,
                                      ObservableCollection<TiaTarget> plcTargets)
        {
            LogService.Write("[VCI-MAIN-VM] [VciMainViewModel] Inicializando VciMainViewModel...");

            _tiaPortal = tiaPortal;
            _tiaproject = project;

            // Inicializamos servicios
            _tiaPlcService = tiaPlcService;
            _tiaVciService = tiaVciService;

            PlcTargets = plcTargets;

            // Inicializamos viewmodels hijos
            MappingVM = new VciMappingViewModel(_tiaPlcService, _tiaVciService);
            AuditVM = new VciAuditViewModel(_tiaPlcService);

            ConfiguredWorkspaces = new ObservableCollection<VciWorkspaceModel>();

            ToggleCreateModeCommand = new RelayCommand(ExecuteToggleCreateMode);
            CreateNewWorkspaceCommand = new RelayCommand(ExecuteCreateNewWorkspace, CanExecuteCreateNewWorkspace);
            DeleteWorkspaceCommand = new RelayCommand(ExecuteDeleteWorkspace, CanExecuteDeleteWorkspace);
            ChangeWorkspaceFolderCommand = new RelayCommand(ExecuteChangeWorkspaceFolder, CanExecuteChangeWorkspaceFoldere);

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
                //AuditVM?.NotifyPlcChanged(SelectedTarget.Name);
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
        /// Metodo
        /// </summary>
        private void ExecuteToggleCreateMode()
        {
            IsCreatingWorkspace = !IsCreatingWorkspace;
            if (IsCreatingWorkspace) NewWorkspaceName = ""; // Limpiar al entrar
        }



        // ==================================================================================================================
        private bool CanExecuteCreateNewWorkspace() => !string.IsNullOrWhiteSpace(NewWorkspaceName);
        // ==================================================================================================================
        /// <summary>
        /// Metodo para crear un Workspace nuevo
        /// </summary>
        private void ExecuteCreateNewWorkspace()
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
                StatusService.Set($"Creando Workspace '{NewWorkspaceName}' en TIA Portal...", StatusType.Warning);

                var newWs = _tiaVciService.CreateWorkspace(NewWorkspaceName, chosenPath);

                if (newWs != null)
                {
                    ConfiguredWorkspaces.Add(newWs);
                    SelectedWorkspace = newWs;
                    StatusService.Set($"Workspace creado y seleccionado correctamente.", StatusType.Ok);
                    IsCreatingWorkspace = false; // Volver al modo normal
                }
                else
                {
                    StatusService.Set("Error al crear el Workspace. Revisa los logs.", StatusType.Error);
                }
            }
        }



        // ==================================================================================================================
        private bool CanExecuteDeleteWorkspace() => SelectedWorkspace != null;
        // ==================================================================================================================
        /// <summary>
        /// Borra el Workspace seleccionado tras pedir confirmación al usuario
        /// </summary>
        private void ExecuteDeleteWorkspace()
        {
            var result = System.Windows.MessageBox.Show(
                $"¿Estás seguro de que deseas desvincular el Workspace '{SelectedWorkspace.Name}' de TIA Portal?\n\n(Tranquilo, no se borrarán los archivos de tu disco duro)",
                "Confirmar borrado",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                StatusService.Set($"Borrando Workspace '{SelectedWorkspace.Name}'...", StatusType.Warning);

                bool success = _tiaVciService.DeleteWorkspace(SelectedWorkspace.Name);

                if (success)
                {
                    StatusService.Set($"Workspace borrado correctamente.", StatusType.Ok);

                    // Lo quitamos de la lista visual
                    ConfiguredWorkspaces.Remove(SelectedWorkspace);

                    // Seleccionamos el primero que quede, o null si la lista se queda vacía
                    SelectedWorkspace = ConfiguredWorkspaces.FirstOrDefault();
                }
                else
                {
                    StatusService.Set("Error al borrar el Workspace. Revisa los logs.", StatusType.Error);
                }
            }
        }


        // ==================================================================================================================
        private bool CanExecuteChangeWorkspaceFoldere() => SelectedWorkspace != null;
        // ==================================================================================================================
        /// <summary>
        /// Cambia la ruta de la carpeta fisica del Workspace seleccionado
        /// </summary>
        private void ExecuteChangeWorkspaceFolder()
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
                    StatusService.Set($"Cambiando ruta del Workspace a '{chosenPath}'...", StatusType.Warning);
                    if (_tiaVciService.UpdateWorkspacePath(SelectedWorkspace.Name, chosenPath))
                    {
                        SelectedWorkspace.Path = chosenPath;
                        OnPropertyChanged(nameof(SelectedWorkspace));

                        // Actualizamos la ruta a la vista hija para que reinicie la tabla
                        if (MappingVM != null) MappingVM.WorkspacePath = chosenPath;

                        StatusService.Set("Ruta del Workspace actualizada correctamente.", StatusType.Ok);
                    }
                }
            }
        }

    }
}