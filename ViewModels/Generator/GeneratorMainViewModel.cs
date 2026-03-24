using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.Generator;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// ViewModel principal del modulo de generacion
    /// </summary>
    public class GeneratorMainViewModel : ObservableObject
    {

        // =================================================================================================================
        // Tia portal
        private readonly TiaPlcService _tiaPlcService;
        private readonly TiaHmiService _tiaHmiService;

        public ObservableCollection<TiaTarget> PlcTargets { get; }
        public ObservableCollection<TiaTarget> HmiTargets { get; }
        public ObservableCollection<TiaTarget> ScadaTargets { get; }


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


        // Caché de datos cargados
        private readonly Dictionary<string, List<object>> _engineeringCache = new Dictionary<string, List<object>>();

        // Cache de configuracion xml
        private ConfigNetworkSettings _configNetworkSettings;
        private ConfigProcessSettings _configProcessesSettings;
        private ConfigDeviceSettings _configDeviceSettings;
        private ConfigGlobalSettings _configGlobalSettings;
        private List<ConfigDeviceCategory> _configDeviceCategories;


        // ViewModels y Configuración
        public DevicesViewModel DevicesVM { get; set; }
        public ParamsAlarmsViewModel ParamsAlarmsVM { get; set; }
        public ProcessGeneratorViewModel ProcessGeneratorVM { get; set; }


        // Variable que indica si se ha cargado un Excel correctamente
        private bool _isDataLoaded;
        public bool IsDataLoaded
        {
            get => _isDataLoaded;
            set { _isDataLoaded = value; OnPropertyChanged(); }
        }

        private string _selectedExcelFile;
        public string SelectedExcelFile
        {
            get => _selectedExcelFile;
            set { _selectedExcelFile = value; OnPropertyChanged(); }
        }

        public RelayCommand LoadDataCommand { get; }




        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public GeneratorMainViewModel(TiaPlcService tiaPlcService,
                                      TiaHmiService tiaHmiService,
                                      ObservableCollection<TiaTarget> plcTargets,
                                      ObservableCollection<TiaTarget> hmiTargets,
                                      ObservableCollection<TiaTarget> scadaTargets)
        {
            LogService.Clear();
            LogService.Write("[MAIN-VM] [MainViewModel] Inicializando MainViewModel...");


            // Buscamos todos los dispositivos del proyecto         
            PlcTargets = plcTargets;
            HmiTargets = hmiTargets;
            ScadaTargets = scadaTargets;

            // Inicializamos servicios de Tia portal
            _tiaPlcService = tiaPlcService;
            _tiaHmiService = tiaHmiService;


            // Seleccionamos el primer PLC por defecto para la comparación
            SelectedTarget = PlcTargets.FirstOrDefault(t => t.Type == TargetType.PLC);

            // Inicializamos configuración y cargamos categorías
            AppConfigService.InitializeEnvironment();
            _configNetworkSettings = AppConfigService.GetNetworkConfig();
            _configProcessesSettings = AppConfigService.GetProcessConfig();
            _configDeviceSettings = AppConfigService.GetDeviceSettings();
            _configGlobalSettings = AppConfigService.GetGlobalSettings();
            _configDeviceCategories = AppConfigService.GetDeviceCategories();

            // Inicializamos viewmodels
            DevicesVM = new DevicesViewModel(_tiaPlcService, _tiaHmiService, _configDeviceCategories, HmiTargets);
            ParamsAlarmsVM = new ParamsAlarmsViewModel(_tiaPlcService);

            ProcessGeneratorVM = new ProcessGeneratorViewModel(_tiaPlcService);
            ProcessGeneratorVM.LoadTemplates(_configGlobalSettings);


            // Seleccionamos una categoria en el viewmodel
            if (_configDeviceCategories.Count > 0)
                DevicesVM.SelectedCategory = _configDeviceCategories[0];

            // Mapeo de comandos
            LoadDataCommand = new RelayCommand(LoadExcelAndGenerateJson);

            // Inicializar estado
            IsDataLoaded = false;
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar el PLC de trabajo cuando el usuario cambia la selección
        /// </summary>
        private void NotifyPlcChanged()
        {
            if (SelectedTarget != null && SelectedTarget.SoftwareObject is PlcSoftware plc)
            {
                DevicesVM?.NotifyPlcChanged(SelectedTarget.Name);
                ParamsAlarmsVM?.NotifyPlcChanged(SelectedTarget.Name);
                ProcessGeneratorVM?.NotifyPlcChanged(SelectedTarget.Name);

                StatusService.Set($"Objetivo cambiado a: {SelectedTarget.Name}", StatusType.Ok);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para leer y generar JSON con los datos de Excel
        /// </summary>
        private async void LoadExcelAndGenerateJson()
        {
            LogService.Write("[MAIN-VM] [LoadExcelAndGenerateJson] Botón 'Cargar' pulsado.");

            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsm;*.xlsx",
                    Title = "Selecciona el archivo de definición"
                };

                if (openFileDialog.ShowDialog() != true) return;

                StatusService.SetBusy(true);

                SelectedExcelFile = openFileDialog.FileName;
                LogService.Write($"[MAIN-VM] [LoadExcelAndGenerateJson] Archivo seleccionado: {SelectedExcelFile}");

                // Verificar ruta del extractor
                if (!File.Exists(_configGlobalSettings.ExtractorExePath))
                {
                    LogService.Write("[MAIN-VM] [LoadExcelAndGenerateJson] ERROR: Extractor no encontrado", true);
                    StatusService.Set("Error: No se encuentra ZC_Extractor.exe", StatusType.Error);
                    MessageBox.Show($"Extractor no encontrado en:\n{_configGlobalSettings.ExtractorExePath}", "Error de configuración");
                    return;
                }


                ClearExportFolder(AppConfigService.ExportPath);

                LogService.Write("[MAIN-VM] [LoadExcelAndGenerateJson] Lanzando proceso extractor excel...");
                StatusService.Set("Extrayendo datos de excel...", StatusType.Ok);



                // 1. Ejecutar y comprobar si Python terminó con éxito
                if (await StartExtractor())
                {
                    // 2. Si Python terminó bien, comprobamos que los archivos estén ahí
                    if (await WaitForPythonFiles())
                    {
                        LogService.Write("[MAIN-VM] [LoadExcelAndGenerateJson] Archivos XML detectados con éxito.");
                        StatusService.Set("Cargando datos en memoria...", StatusType.Ok);

                        await Task.Run(() => LoadAllFromFolder(AppConfigService.ExportPath));

                        // Actualizar ViewModels
                        DevicesVM.LoadData(_engineeringCache, _configDeviceSettings);
                        ParamsAlarmsVM.LoadData(_engineeringCache, _configProcessesSettings);
                        ProcessGeneratorVM.LoadData(_engineeringCache, _configProcessesSettings, _configGlobalSettings);                        

                        IsDataLoaded = true;
                        StatusService.Set("Listo. Todos los módulos cargados.", StatusType.Ok);
                    }
                    else
                    {
                        StatusService.Set("Error: El extractor de excel terminó pero no se encontraron los archivos XML.", StatusType.Error);
                    }
                }
                else
                {
                    // Si llegamos aquí, es que Python falló (ExitCode != 0)
                    StatusService.Set("Error en el script de extracción. Revisa el LOG.", StatusType.Error);
                    MessageBox.Show("El extractor de excel ha fallado. Consulta los detalles en la pestaña de Log.",
                                    "Error de Extracción", MessageBoxButton.OK, MessageBoxImage.Error);
                }


            }
            catch (Exception ex)
            {
                LogService.Write($"[MAIN-VM] [LoadExcelAndGenerateJson] CRASH EN CARGA: {ex.Message}", true);
                LogService.Write($"[MAIN-VM] [LoadExcelAndGenerateJson] CRASH EN CARGA:\n{ex.ToString()}", true);
                StatusService.Set("Error general en el proceso.", StatusType.Error);
                MessageBox.Show($"{ex.Message}", "Error Crítico");
            }
            finally 
            {
                StatusService.SetBusy(false);
            }

        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para lanzar el programa de extraccion de python
        /// </summary>
        private async Task<bool> StartExtractor()
        {
            try
            {
                string arguments = $"--path \"{SelectedExcelFile}\"";

                // 1. Crear la info de inicio (Asegúrate de que sea la de Siemens)
                var startInfo = new Siemens.Engineering.AddIn.Utilities.ProcessStartInfo
                {
                    FileName = _configGlobalSettings.ExtractorExePath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                // 2. CREAR EL OBJETO PROCESO
                var myProcess = new Siemens.Engineering.AddIn.Utilities.Process();
                myProcess.StartInfo = startInfo;

                // 3. Suscribirse a los eventos ANTES de empezar
                myProcess.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) LogService.Write($"[MAIN-VM] [StartExtractor] {e.Data}");
                };

                myProcess.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) LogService.Write($"[MAIN-VM] [StartExtractor] {e.Data}", true);
                };

                // 4. LANZAR E INICIAR LECTURA
                if (myProcess.Start())
                {
                    myProcess.BeginOutputReadLine();
                    myProcess.BeginErrorReadLine();

                    LogService.Write("[MAIN-VM] [StartExtractor] Extractor ejecutándose en segundo plano...");
                    
                    await Task.Run(() =>
                    {
                        while (!myProcess.HasExited)
                        {
                            myProcess.WaitForExit();
                        }
                    });

                    LogService.Write($"[MAIN-VM] [StartExtractor] Extractor finalizado con código: {myProcess.ExitCode}");
                    return myProcess.ExitCode == 0;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogService.Write($"[MAIN-VM] [StartExtractor] Error crítico lanzando extractor de excel: {ex.Message}", true);
                return false;
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para esperar a que se encuentren todos los archivos esperados
        /// </summary>
        private async Task<bool> WaitForPythonFiles()
        {        

            LogService.Write($"[MAIN-VM] [WaitForPythonFiles] Iniciando espera en: {AppConfigService.ExportPath}");

            // Creamos la lista de archivos que esperamos basándonos en la configuración
            List<string> expectedFiles = new List<string>();
            expectedFiles.AddRange(_configDeviceCategories.Select(c => c.XmlFile));
            expectedFiles.Add(_configProcessesSettings.ProcessXml);
            expectedFiles.Add(_configProcessesSettings.PRealXml);
            expectedFiles.Add(_configProcessesSettings.PIntXml);
            expectedFiles.Add(_configProcessesSettings.StageXml);
            expectedFiles.Add(_configNetworkSettings.ConnectionsXml);
            expectedFiles.Add(_configDeviceSettings.DeviceDataConfigXml);

            for (int i = 0; i < 150; i++)
            {
                bool allFound = true;
                foreach (var file in expectedFiles)
                {
                    if (string.IsNullOrEmpty(file)) continue;
                    if (!File.Exists(Path.Combine(AppConfigService.ExportPath, file)))
                    {
                        allFound = false;
                        break;
                    }
                }

                if (allFound) return true;

                await Task.Delay(200);
            }
            return false;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para cargar todos los archivos desde una carpeta
        /// </summary>
        private void LoadAllFromFolder(string folderPath)
        {
            _engineeringCache.Clear();

            // Cargar dispositivos de cada categoría
            foreach (var cat in _configDeviceCategories)
            {
                string filePath = Path.Combine(folderPath, cat.XmlFile);
                if (File.Exists(filePath))
                {
                    _engineeringCache[cat.Name] = DataService.LoadDispCategoryData(filePath, cat);
                }
            }

            // Cargar numero maximo de dispositivos
            if (_configDeviceSettings != null)
            {
                string path = Path.Combine(folderPath, _configDeviceSettings.DeviceDataConfigXml);
                if (File.Exists(path))
                {
                    // Cargamos como lista de objetos Disp_Config
                    var data = DataService.LoadDeviceNMax(path);
                    _engineeringCache[_configDeviceSettings.Disp_N_Max] = data.Cast<object>().ToList();
                }
            }

            // Cargar configuracion de procesos
            if (_configProcessesSettings != null)
            {
                // Lista de procesos
                string pathProcess = Path.Combine(folderPath, _configProcessesSettings.ProcessXml);
                if (File.Exists(pathProcess))
                {
                    var data = DataService.LoadProcess(pathProcess);
                    _engineeringCache[_configProcessesSettings.ProcessName] = data.Cast<object>().ToList();
                }

                // Parámetros Reales
                string pathPReal = Path.Combine(folderPath, _configProcessesSettings.PRealXml);
                if (File.Exists(pathPReal))
                {
                    var data = DataService.LoadParameters(pathPReal);
                    _engineeringCache[_configProcessesSettings.PRealName] = data.Cast<object>().ToList();
                }

                // Parámetros Enteros
                string pathPInt = Path.Combine(folderPath, _configProcessesSettings.PIntXml);
                if (File.Exists(pathPInt))
                {
                    var data = DataService.LoadParameters(pathPInt);
                    _engineeringCache[_configProcessesSettings.PIntName] = data.Cast<object>().ToList();
                }

                // Alarmas
                string pathAlm = Path.Combine(folderPath, _configProcessesSettings.AlarmXml);
                if (File.Exists(pathAlm))
                {
                    var data = DataService.LoadAlarms(pathAlm);
                    _engineeringCache[_configProcessesSettings.AlarmName] = data.Cast<object>().ToList();
                }

                string pathStages = Path.Combine(folderPath, _configProcessesSettings.StageXml);
                if (File.Exists(pathStages))
                {
                    var data = DataService.LoadStages(pathStages);
                    _engineeringCache[_configProcessesSettings.StageName] = data.Cast<object>().ToList();
                }
            }

            // Cargar configuración de red (Conexiones)
            if (_configNetworkSettings != null)
            {
                string pathConnections = Path.Combine(folderPath, _configNetworkSettings.ConnectionsXml);
                if (File.Exists(pathConnections))
                {
                    var data = DataService.LoadConections(pathConnections);
                    _engineeringCache[_configNetworkSettings.ConnectionsName] = data.Cast<object>().ToList();
                }
            }

        }



        // // ==================================================================================================================
        /// <summary>
        /// Limpiar la carpeta de exportacion de archivos
        /// </summary>
        private void ClearExportFolder(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string f in Directory.GetFiles(path))
            {
                try { File.Delete(f); } catch { }
            }
        }



    }
}