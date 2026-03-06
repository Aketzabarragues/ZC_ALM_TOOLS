using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Vci;
using ZC_ALM_TOOLS.Services;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;
using ZC_ALM_TOOLS.Services.Vci;

namespace ZC_ALM_TOOLS.ViewModels.Vci
{
    public class VciMappingViewModel : ObservableObject
    {
        private readonly TiaVciService _tiaVciService;

        private string _workspacePath;
        public string WorkspacePath
        {
            get => _workspacePath;
            set { _workspacePath = value; OnPropertyChanged(); }
        }

        private string _workspaceStatusText;
        public string WorkspaceStatusText
        {
            get => _workspaceStatusText;
            set { _workspaceStatusText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<VciMappingAction> MappingActions { get; set; }

        public RelayCommand SelectFolderCommand { get; }
        public RelayCommand AnalyzeProjectCommand { get; }
        public RelayCommand ApplyMappingsCommand { get; }

        // ==================================================================================================================
        // CONSTRUCTOR
        public VciMappingViewModel(TiaVciService tiaVciService)
        {
            LogService.Write("[VCI-MAPPING-VM] [Constructor] Inicializando VciMappingViewModel...");

            _tiaVciService = tiaVciService;
            MappingActions = new ObservableCollection<VciMappingAction>();
            WorkspaceStatusText = "Esperando análisis...";

            SelectFolderCommand = new RelayCommand(ExecuteSelectFolder);
            AnalyzeProjectCommand = new RelayCommand(ExecuteAnalyzeProject, CanExecuteAnalyze);
            ApplyMappingsCommand = new RelayCommand(ExecuteApplyMappings, CanExecuteApply);
        }


        // ==================================================================================================================
        // BOTÓN: Explorar Carpeta
        private void ExecuteSelectFolder()
        {
            LogService.Write("[VCI-MAPPING-VM] [ExecuteSelectFolder] Abriendo diálogo de selección de carpeta...");

            OpenFileDialog folderDialog = new OpenFileDialog
            {
                Title = "Entra en la carpeta de tu Workspace VCI y pulsa 'Abrir'",
                ValidateNames = false,
                CheckFileExists = false,      // MUY IMPORTANTE: Permite aceptar sin elegir un archivo real
                CheckPathExists = true,
                FileName = "Seleccionar esta carpeta", // Texto por defecto que aparece en la caja
                Filter = "Carpetas|*.ocultar" // Un filtro inventado para que no se vean los archivos, solo las carpetas
            };

            // Mostrará el diálogo nativo de Windows sin bloquearse
            if (folderDialog.ShowDialog() == true)
            {
                // Como el FileName devolverá algo como: "C:\Ruta\Al\Workspace\Seleccionar esta carpeta"
                // Usamos GetDirectoryName para cortar ese texto falso y quedarnos con la ruta pura de la carpeta.
                WorkspacePath = Path.GetDirectoryName(folderDialog.FileName);
                LogService.Write($"[VCI-MAPPING-VM] [ExecuteSelectFolder] Carpeta seleccionada: {WorkspacePath}");
            }
            else
            {
                LogService.Write("[VCI-MAPPING-VM] [ExecuteSelectFolder] Selección cancelada por el usuario.");
            }
        }


        private bool CanExecuteAnalyze() => !string.IsNullOrWhiteSpace(WorkspacePath);


        // ==================================================================================================================
        // BOTÓN: Analizar Proyecto (Fase 1: Solo Windows)
        private void ExecuteAnalyzeProject()
        {
            LogService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Iniciando escaneo de Workspace en: {WorkspacePath}");
            StatusService.SetBusy(true);
            StatusService.Set("Escaneando archivos del Workspace en disco...", StatusType.Ok);

            try
            {
                // Limpiamos la tabla
                MappingActions.Clear();

                // 1. Instanciamos el servicio de exploración de Windows
                var vciWorkspaceService = new VciWorkspaceService();

                // 2. Extraemos el Diccionario (Nombre del bloque -> Ruta del XML)
                var localFilesDict = vciWorkspaceService.GetVciFilesFromWorkspace(WorkspacePath);

                LogService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Análisis completado. Se encontraron {localFilesDict.Count} archivos XML.");

                // 3. Volcamos los datos a la interfaz visual
                foreach (var file in localFilesDict)
                {
                    MappingActions.Add(new VciMappingAction
                    {
                        BlockName = file.Key,
                        DiskPath = file.Value,
                        BlockType = "XML",

                        // Como aún no tenemos Openness, forzamos el estado a "Conflicto" (rojo) 
                        // simplemente para que veamos algo en la tabla al probar.
                        State = VciMatchState.Conflicto
                    });
                }

                WorkspaceStatusText = $"Se han encontrado {localFilesDict.Count} archivos XML en disco.";
                StatusService.Set($"Análisis completado: {localFilesDict.Count} archivos encontrados.", StatusType.Ok);
                LogService.Write("[VCI-MAPPING-VM] [ExecuteAnalyzeProject] Interfaz actualizada correctamente con los datos locales.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] EXCEPCIÓN: {ex.Message}", true);
                LogService.Write($"[VCI-MAPPING-VM] [ExecuteAnalyzeProject] STACKTRACE:\n{ex.StackTrace}", true);
                StatusService.Set("Error al leer la carpeta local.", StatusType.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }


        // ==================================================================================================================
        // BOTÓN: Aplicar (Pendiente)
        private bool CanExecuteApply() => MappingActions != null && MappingActions.Count > 0;

        private void ExecuteApplyMappings()
        {
            LogService.Write("[VCI-MAPPING-VM] [ExecuteApplyMappings] Botón de aplicar pulsado. (Pendiente de implementación).");
            // Se hará en la siguiente fase
        }
    }
}