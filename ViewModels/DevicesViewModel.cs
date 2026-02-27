using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.Design;
using System.Xml.Linq;
using Siemens.Engineering.Hmi;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{
    // ViewModel que gestiona la pestaña de detalles (tabla de dispositivos, comparaciones y sincronización)
    public class DevicesViewModel : ObservableObject
    {


        // ==================================================================================================================
        // Tia portal
        private TiaPlcService _tiaPlcService;
        private TiaHmiService _tiaHmiService;

        public string ActivePlcName { get; private set; }
        public ObservableCollection<TiaTarget> HmiTargets { get; set; }


        // Cachés de datos
        private ConfigDeviceSettings _deviceSettings; // Configuración dinámica del XML
        private Dictionary<string, List<object>> _engineeringCache; // Almacén Central
        private Dictionary<string, int> _plcCache = new Dictionary<string, int>();

        private int _currentPlcNMax = 0;


        // Colección visual para el DataGrid. Usamos 'object' para admitir cualquier modelo (Disp_V, Disp_M, etc.)
        public ObservableCollection<object> CurrentDevices { get; set; } = new ObservableCollection<object>();

        // Lista de categorías (ComboBox)
        private List<ConfigDeviceCategory> _categories;
        public List<ConfigDeviceCategory> Categories { get => _categories; set { _categories = value; OnPropertyChanged(); } }


        // Categoría seleccionada
        private ConfigDeviceCategory _selectedCategory;
        public ConfigDeviceCategory SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; OnPropertyChanged(); if (_selectedCategory != null) { LogService.Write($"[DEVICE-VM] [SelectedCategory] Cambio de categoría: {_selectedCategory.Name}"); } RefreshView(); } }


        // Selecciones individuales de lo que se quiere sincronizar de Hmi y Scada
        private bool _syncHmiVariables = true;
        public bool SyncHmiVariables { get => _syncHmiVariables; set { _syncHmiVariables = value; OnPropertyChanged(); } }

        private bool _syncHmiTextLists = true;
        public bool SyncHmiTextLists { get => _syncHmiTextLists; set { _syncHmiTextLists = value; OnPropertyChanged(); } }

        private bool _syncHmiAlarms = false;
        public bool SyncHmiAlarms { get => _syncHmiAlarms; set { _syncHmiAlarms = value; OnPropertyChanged(); } }

        // Texto informativo del label de dimensiones
        private string _dimensionInfo;
        public string DimensionInfo{ get => _dimensionInfo; set { _dimensionInfo = value; OnPropertyChanged(); } }

        // Color del label de dimensiones (Verde/Rojo)
        private string _dimensionColor = "Transparent";
        public string DimensionColor { get => _dimensionColor; set { _dimensionColor = value; OnPropertyChanged(); } }

        // Comandos
        public RelayCommand SyncCommand { get; set; }
        public RelayCommand CompareCommand { get; set; }



        // ==================================================================================================================
        // Constructor
        public DevicesViewModel()
        {
            SyncCommand = new RelayCommand(ExecuteSync, CanExecuteAction);
            CompareCommand = new RelayCommand(ExecuteCompareCommand, CanExecuteAction);
        }



        // ==================================================================================================================
        // Método puente para el botón Comparar
        private async void ExecuteCompareCommand()
        {
            await ExecuteCompare(false);
        }



        // ==================================================================================================================
        // Asigna la instancia de Tia Portal
        public void SetTiaService(TiaPlcService service, TiaHmiService hmiService)
        {
            _tiaPlcService = service;
            _tiaHmiService = hmiService;
        }



        // ==================================================================================================================
        // Carga los datos provenientes del MainViewModel
        public void LoadData(Dictionary<string, List<object>> cache, ConfigDeviceSettings settings)
        {
            _engineeringCache = cache;
            _deviceSettings = settings;

            if (SelectedCategory != null) RefreshView();
        }



        // ==================================================================================================================
        // Actualizar la vista del datagrid
        private void RefreshView()
        {
            if (SelectedCategory == null || _engineeringCache == null) return;

            // 1. Limpiar y llenar tabla
            CurrentDevices.Clear();
            if (_engineeringCache.TryGetValue(SelectedCategory.Name, out var list))
            {
                foreach (var item in list) CurrentDevices.Add(item);
                LogService.Write($"[DEVICE-VM] [RefreshView] Mostrando {list.Count} dispositivos de tipo '{SelectedCategory.Name}'.");
            }
            else
            {
                LogService.Write($"[DEVICE-VM] [RefreshView] No hay datos en caché para la categoría '{SelectedCategory.Name}'.");
            }
            UpdateDimensionInfo();
        }



        // ==================================================================================================================
        // Metodo para actualizar que la seleccion del PLC ha cambiado
        public void NotifyPlcChanged(string plcName)
        {
            ActivePlcName = plcName;
            LogService.Write($"[DEVICE-VM] [NotifyPlcChanged] El PLC de origen ha cambiado. Reiniciando estados de comparación...");
                        
            _plcCache.Clear();

            if (Categories != null)
            {
                foreach (var cat in Categories)
                {
                    cat.NMaxStatus = SynchronizationStatus.Pending;
                    cat.ConstantsStatus = SynchronizationStatus.Pending;
                    cat.DbStatus = SynchronizationStatus.Pending;
                }
            }

            if (_engineeringCache != null)
            {
                foreach (var categoryList in _engineeringCache.Values)
                {
                    foreach (var item in categoryList)
                    {
                        if (item is IDevice device)
                        {
                            device.Estado = "Pendiente";
                        }
                    }
                }
            }

            RefreshView();
        }



        // ==================================================================================================================
        // Actualizar el numero maximo de dispositivos
        private async void UpdateDimensionInfo()
        {

            if (SelectedCategory == null || _tiaPlcService == null || _deviceSettings == null || _engineeringCache == null)
            {
                DimensionColor = "Transparent";
                DimensionInfo = "Seleccione un PLC y Categoría";
                return;
            }

            try
            {
                // Obtener valor del Excel
                var env = new DevicesEnvironment(SelectedCategory, _deviceSettings, _engineeringCache, _tiaPlcService, validatePlc: false);
                int excelVal = env.ExcelNMax;

                DimensionInfo = $"Dimensión: Excel ({excelVal}) | PLC (Consultando...)";
                DimensionColor = "LightGray";

                await Task.Delay(50);

                // Obtener valor del PLC (Consultar TIA o usar Caché)
                if (!_plcCache.TryGetValue(SelectedCategory.Name, out _currentPlcNMax))
                {
                    _currentPlcNMax = _tiaPlcService.ReadGlobalConstant(_deviceSettings.ConfigTableName, SelectedCategory.PlcCountConstant);
                    _plcCache[SelectedCategory.Name] = _currentPlcNMax;
                }

                // Actualizar UI
                DimensionInfo = $"Dimensión: Excel ({excelVal}) | PLC ({_currentPlcNMax})";
                DimensionColor = (excelVal == _currentPlcNMax) ? "#A5D6A7" : "#EF9A9A";
            }
            catch (Exception ex)
            {
                LogService.Write($"[DEVICE-VM] [UpdateDimensionInfo] Error leyendo dimensiones: {ex.Message}", true);
                DimensionInfo = "Dimensión: Error de lectura";
                DimensionColor = "#EF9A9A";
            }
        }



        // ==================================================================================================================
        // Metodo para ejecutar la sincronizacion
        private async void ExecuteSync()
        {
            if (SelectedCategory == null) return;

            var confirm = MessageBox.Show($"¿Deseas sincronizar {SelectedCategory.Name}?\nEsto modificará constantes y bloques en el PLC.",
                                          "Confirmar Sincronización", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            StatusService.SetBusy(true);

            bool okNMax = true, okConst = false, okComp = false, okDb = false;

            try
            {

                var env = new DevicesEnvironment(SelectedCategory, _deviceSettings, _engineeringCache, _tiaPlcService, validatePlc: true);
                if (!env.IsValid) return;

                StatusService.Set($"Inicio sincronización: {SelectedCategory.Name} ---", StatusType.Ok);
                LogService.Write($"[DEVICE-VM] [ExecuteSync] Inicio sincronización: {SelectedCategory.Name} ---");

                okNMax = _tiaPlcService.SyncGlobalConstant(_deviceSettings.ConfigTableName, SelectedCategory.PlcCountConstant, env.ExcelNMax);

                SelectedCategory.NMaxStatus = okNMax ? SynchronizationStatus.Ok : SynchronizationStatus.Error;

                if (!okNMax)
                {
                    MessageBox.Show("Error crítico al sincronizar N_MAX. Se aborta el proceso.");
                    return;
                }

                // Actualizamos caché de PLC y UI para que la barra se ponga verde
                _plcCache[SelectedCategory.Name] = env.ExcelNMax;
                _currentPlcNMax = env.ExcelNMax;
                UpdateDimensionInfo();


                StatusService.Set("Sincronizando constantes de usuario...", StatusType.Ok);
                await Task.Delay(50);

                // CONSTANTES DE USUARIO
                var deviceList = CurrentDevices.Cast<IDevice>()
                                      .Where(d => d.Estado != "Eliminar")
                                      .ToList();

                okConst = await _tiaPlcService.SyncDispUserConstants(SelectedCategory.TiaTable, deviceList);
                SelectedCategory.ConstantsStatus = okConst ? SynchronizationStatus.Ok : SynchronizationStatus.Error;

                // COMPILACIÓN DEL DB
                StatusService.Set("Compilando DB tras redimensionado...", StatusType.Ok);
                await Task.Delay(50);

                okComp = _tiaPlcService.CompileBlock(SelectedCategory.TiaDbName);

                if (okComp)
                {
                    StatusService.Set("Actualizando comentarios del DB...", StatusType.Ok);
                    await Task.Delay(50);

                    okDb = await _tiaPlcService.SyncDispDbComments(SelectedCategory.TiaDbName, SelectedCategory.TiaDbArrayName, deviceList);
                    SelectedCategory.DbStatus = okDb ? SynchronizationStatus.Ok : SynchronizationStatus.Error;

                    StatusService.Set("Compilando DB tras actualizacion de comentarios...", StatusType.Ok);
                    await Task.Delay(50);
                    _tiaPlcService.CompileBlock(SelectedCategory.TiaDbName);
                }
                else
                {
                    SelectedCategory.DbStatus = SynchronizationStatus.Error;
                    LogService.Write("[DEVICE-VM] [ExecuteSync] Fallo en compilación: no se pueden sincronizar comentarios.", true);
                }

                await Task.Delay(100);

                // Tras sincronizar, ejecutamos la comparación para verificar que todo ha quedado bien
                await ExecuteCompare(keepDbStatus: true); // No reseteamos el estado DB porque acabamos de escribirlo

                // Resumen final
                if (okNMax && okConst && okComp && okDb)
                {

                    // 1. Buscamos si el usuario ha marcado algún HMI en el panel lateral
                    var hmisSeleccionados = HmiTargets?.Where(h => h.IsChecked).ToList() ?? new List<TiaTarget>();

                    if (hmisSeleccionados.Any() && (SyncHmiVariables || SyncHmiTextLists || SyncHmiAlarms))
                    {
                        StatusService.Set("Sincronizando HMIs seleccionados...", StatusType.Ok);
                        await Task.Delay(10);

                        foreach (var hmi in hmisSeleccionados)
                        {
                            LogService.Write($"[DEVICE-VM] [ExecuteSync] --- INICIANDO SYNC HMI: {hmi.Name} ---");

                            if (SyncHmiVariables)
                                _tiaHmiService.SyncHmiVariables(hmi.SoftwareObject, ActivePlcName, SelectedCategory, deviceList);

                            if (SyncHmiTextLists)
                                _tiaHmiService.SyncHmiTextLists(hmi.SoftwareObject, SelectedCategory, deviceList);

                            if (SyncHmiAlarms)
                                _tiaHmiService.SyncHmiAlarms(hmi.SoftwareObject, SelectedCategory, deviceList);
                        }
                    }

                    StatusService.Set("Sincronización completada con éxito.", StatusType.Ok);
                }
                else
                {
                    string msg = "Proceso finalizado con errores:\n";
                    if (!okConst) msg += "- Fallo en constantes\n";
                    if (!okComp) msg += "- Fallo en compilación\n";
                    if (!okDb) msg += "- Fallo en comentarios DB\n";

                    StatusService.Set("Sincronización finalizada con errores.", StatusType.Error);
                }

            }
            catch (Exception ex)
            {
                LogService.Write($"[DEVICE-VM] [ExecuteSync] Error Crítico: {ex.Message}", true);
                StatusService.Set($"Error Crítico: {ex.Message}", StatusType.Error);
                SelectedCategory.DbStatus = SynchronizationStatus.Error;
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }



        // ==================================================================================================================
        // Metodo para comparar el dispositivo seleccionado con el PLC
        private async Task ExecuteCompare(bool keepDbStatus)
        {
            if (SelectedCategory == null || _deviceSettings == null || _engineeringCache == null) return;


            try
            {
                StatusService.SetBusy(true);

                RefreshView();

                var env = new DevicesEnvironment(SelectedCategory, _deviceSettings, _engineeringCache, _tiaPlcService, validatePlc: true);
                if (!env.IsValid) return;

                LogService.Write($"[DEVICE-VM] [ExecuteCompare] Iniciando comparación: {SelectedCategory.Name} ---");
                StatusService.Set("Comparando datos con TIA Portal...", StatusType.Ok);

                await Task.Delay(50);

                // Reset de estados
                SelectedCategory.NMaxStatus = SynchronizationStatus.Pending;
                SelectedCategory.ConstantsStatus = SynchronizationStatus.Pending;
                if (!keepDbStatus) SelectedCategory.DbStatus = SynchronizationStatus.Pending;

                // Sincronizar info de N_MAX
                StatusService.Set("Comprobando dimensión N_MAX...", StatusType.Ok);
                UpdateDimensionInfo();

                bool nMaxMatch = (env.ExcelNMax == _currentPlcNMax);
                SelectedCategory.NMaxStatus = nMaxMatch ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                LogService.Write($"[DEVICE-VM] [ExecuteCompare] N_MAX -> Excel: {env.ExcelNMax} | PLC: {_currentPlcNMax} ({(nMaxMatch ? "OK" : "ERROR")})");

                // Exportar y Parsear PLC
                string tempXmlPath = Path.Combine(AppConfigService.TempPath, "plc_export.xml");
                StatusService.Set($"Exportando tabla '{SelectedCategory.TiaTable}' desde TIA...", StatusType.Ok);
                LogService.Write($"[DEVICE-VM] [ExecuteCompare] Exportando tabla '{SelectedCategory.TiaTable}' a XML temporal...");

                bool exportOk = _tiaPlcService.ExportDispTagTable(SelectedCategory.TiaTable, tempXmlPath);

                if (!exportOk)
                {
                    LogService.Write("[DEVICE-VM] [ExecuteCompare] ERROR: No se pudo exportar la tabla desde TIA Portal.", true);
                    StatusService.Set($"No se pudo exportar la tabla '{SelectedCategory.TiaTable}' desde TIA Portal.", StatusType.Error);
                    SelectedCategory.ConstantsStatus = SynchronizationStatus.Error;
                    return;
                }

                StatusService.Set("Cruzando datos Excel vs PLC...", StatusType.Ok);
                await Task.Delay(10);


                var plcDict = XmlParserService.ParseDispTableXml(tempXmlPath);

                // Obtenemos los dispositivos del Excel (los que ya están en la tabla)
                // Obtenemos los dispositivos del Excel
                var excelList = CurrentDevices.Cast<IDevice>().ToList();

                // Llamamos al motor genérico de comparación
                var result = ComparisonService.Compare(
                    excelList, plcDict,
                    d => d.Numero,                   // ID del dispositivo
                    d => d.CPTag,                    // En dispositivos comparamos contra el CPTag
                    d => d.Estado,                   // Leer estado
                    (d, est) => d.Estado = est,      // Escribir estado
                    (id, txt) =>                     // Cómo fabricar un fantasma de dispositivo
                    {
                        IDevice ghost = DataService.CreateEmptyDispData(SelectedCategory);
                        ghost.Numero = id;
                        ghost.Tag = txt;
                        ghost.Descripcion = "--- NO EXISTE EN EXCEL (Se borrará) ---";
                        ghost.Estado = "Eliminar";
                        return ghost;
                    }
                );

                // Inyectar fantasmas en la tabla
                foreach (var ghost in result.Ghosts)
                {
                    CurrentDevices.Add(ghost);
                    LogService.Write($"[DEVICE-VM] [ExecuteCompare] Sobrante en PLC -> ID {ghost.Numero}: {ghost.Tag}", true);
                }

                // Actualizar estado del semáforo
                SelectedCategory.ConstantsStatus = result.AllMatch ? SynchronizationStatus.Ok : SynchronizationStatus.Error;

                LogService.Write($"[DEVICE-VM] [ExecuteCompare] RESUMEN: {result.MatchCount} OK, {result.MismatchCount} Diferentes, {result.NewCount} Nuevos, {result.GhostCount} Sobrantes.");
                LogService.Write("[DEVICE-VM] [ExecuteCompare] COMPARACIÓN FINALIZADA");

                // Resultado final en la barra de estado
                if (result.AllMatch)
                {
                    StatusService.Set("Comparación finalizada: Todo OK.", StatusType.Ok);
                }
                else
                {
                    StatusService.Set("Comparación finalizada: Se detectaron diferencias.", StatusType.Warning);
                }
                
                
            }
            catch (Exception ex)
            {
                LogService.Write($"[DEVICE-VM] [ExecuteCompare] ERROR CRÍTICO EN COMPARACIÓN: {ex.Message}", true);
                SelectedCategory.ConstantsStatus = SynchronizationStatus.Error;
                StatusService.Set("Error durante la comparación. Revisa el Log.", StatusType.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }



        


        // ==================================================================================================================
        // Metodo para habilitar botones
        private bool CanExecuteAction()
        {
            // Solo habilitamos si:
            // 1. Tenemos servicio de TIA conectado
            // 2. Tenemos una categoría seleccionada en el combo
            // 3. Hay dispositivos en la lista (no está vacía)
            return _tiaPlcService != null && SelectedCategory != null && CurrentDevices.Count > 0;
        }

    }

}
