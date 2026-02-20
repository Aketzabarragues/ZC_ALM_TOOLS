using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{
    // ViewModel que gestiona la pestaña de detalles (tabla de dispositivos, comparaciones y sincronización)
    public class DevicesViewModel : ObservableObject
    {
        // =================================================================================================================
        // PRIVATE FIELDS & SERVICES
        private TiaPlcService _tiaPlcService;

        // Cachés de datos
        private ConfigDeviceSettings _deviceSettings; // Configuración dinámica del XML
        private Dictionary<string, List<object>> _engineeringCache; // Almacén Central
        private Dictionary<string, int> _plcCache = new Dictionary<string, int>();

        private int _currentPlcNMax = 0;

        // =================================================================================================================
        // 2. PUBLIC PROPERTIES (BINDING)

        // Colección visual para el DataGrid. Usamos 'object' para admitir cualquier modelo (Disp_V, Disp_M, etc.)
        public ObservableCollection<object> CurrentDevices { get; set; } = new ObservableCollection<object>();

        // Lista de categorías (ComboBox)
        private List<ConfigDeviceCategory> _categories;
        public List<ConfigDeviceCategory> Categories
        {
            get => _categories;
            set { _categories = value; OnPropertyChanged(); }
        }

        // Categoría seleccionada
        private ConfigDeviceCategory _selectedCategory;
        public ConfigDeviceCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged();
                if (_selectedCategory != null)
                {
                    LogService.Write($"[UI] Cambio de categoría: {_selectedCategory.Name}");
                }
                RefreshView(); // Al cambiar, recargamos la tabla y comparamos
            }
        }

        // Texto informativo del label de dimensiones
        private string _dimensionInfo;
        public string DimensionInfo
        {
            get => _dimensionInfo;
            set { _dimensionInfo = value; OnPropertyChanged(); }
        }

        // Color del label de dimensiones (Verde/Rojo)
        private string _dimensionColor = "Transparent";
        public string DimensionColor
        {
            get => _dimensionColor;
            set { _dimensionColor = value; OnPropertyChanged(); }
        }

        // Comandos
        public RelayCommand SyncCommand { get; set; }      // Antes: SyncConstantesCommand
        public RelayCommand CompareCommand { get; set; }   // Antes: ComparacionCommand


        // =================================================================================================================
        // 3. CONSTRUCTOR
        public DevicesViewModel()
        {


            SyncCommand = new RelayCommand(ExecuteSync, CanExecuteAction);
            CompareCommand = new RelayCommand(() => ExecuteCompare(false), CanExecuteAction);
        }


        // Carga los datos provenientes del MainViewModel
        public void SetTiaService(TiaPlcService service)
        {
            _tiaPlcService = service;
        }

        public void LoadData(Dictionary<string, List<object>> cache, ConfigDeviceSettings settings)
        {
            _engineeringCache = cache;
            _deviceSettings = settings;

            if (SelectedCategory != null) RefreshView();
        }


        // =================================================================================================================
        // 4. INTERNAL LOGIC: VIEW REFRESH & DIMENSIONS

        private void RefreshView()
        {
            if (SelectedCategory == null || _engineeringCache == null) return;

            // 1. Limpiar y llenar tabla
            CurrentDevices.Clear();
            if (_engineeringCache.TryGetValue(SelectedCategory.Name, out var list))
            {
                foreach (var item in list) CurrentDevices.Add(item);
                LogService.Write($"[UI] Mostrando {list.Count} dispositivos de tipo '{SelectedCategory.Name}'.");
            }
            else
            {
                LogService.Write($"[UI] No hay datos en caché para la categoría '{SelectedCategory.Name}'.");
            }

        }

        private void UpdateDimensionInfo()
        {
            if (SelectedCategory == null) return;

            // Obtener valor del Excel
            int excelVal = 0;
            if (_engineeringCache.TryGetValue(_deviceSettings.Disp_N_Max, out var limitsList))
            {
                var limitItem = limitsList.Cast<Disp_Config>()
                                          .FirstOrDefault(x => x.Nombre == SelectedCategory.GlobalConfigKey);
                excelVal = limitItem?.Valor ?? 0;
            }

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


        // =================================================================================================================
        // 
        private void ExecuteSync()
        {
            if (SelectedCategory == null) return;

            var confirm = MessageBox.Show($"¿Deseas sincronizar {SelectedCategory.Name}?\nEsto modificará constantes y bloques en el PLC.",
                                          "Confirmar Sincronización", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            StatusService.SetBusy(true);
            UpdateStatusFrame();

            bool okNMax = true, okConst = false, okComp = false, okDb = false;

            try
            {
                StatusService.Set($"--- INICIO SINCRONIZACIÓN: {SelectedCategory.Name} ---", false);
                LogService.Write($"--- SYNC START: {SelectedCategory.Name} ---");

                // N_MAX. Buscamos la carpeta de límites ("Disp_N_Max")
                if (_engineeringCache.TryGetValue(_deviceSettings.Disp_N_Max, out var limits))
                {
                    // Buscamos el límite específico de esta categoría (ej. "Num_Disp_M")
                    var limitItem = limits.Cast<Disp_Config>()
                                          .FirstOrDefault(x => x.Nombre == SelectedCategory.GlobalConfigKey);

                    if (limitItem != null)
                    {
                        int excelVal = limitItem.Valor;

                        // Sincronizamos con la tabla dinámica definida en el XML Maestro
                        okNMax = _tiaPlcService.SyncGlobalConstant(_deviceSettings.ConfigTableName, SelectedCategory.PlcCountConstant, excelVal);

                        SelectedCategory.NMaxStatus = okNMax ? SynchronizationStatus.Ok : SynchronizationStatus.Error;

                        if (!okNMax)
                        {
                            MessageBox.Show("Error crítico al sincronizar N_MAX. Se aborta el proceso.");
                            return;
                        }

                        // Actualizamos caché de PLC y UI para que la barra se ponga verde
                        _plcCache[SelectedCategory.Name] = excelVal;
                        _currentPlcNMax = excelVal;
                        UpdateDimensionInfo();
                    }
                }

                // ---------------------------------------------------------
                // CONSTANTES DE USUARIO (DISPOSITIVOS)
                // ---------------------------------------------------------
                var deviceList = CurrentDevices.Cast<IDevice>()
                                      .Where(d => d.Estado != "Eliminar")
                                      .ToList();

                okConst = _tiaPlcService.SyncUserConstants(SelectedCategory.TiaGroup, SelectedCategory.TiaTable, deviceList);
                SelectedCategory.ConstantsStatus = okConst ? SynchronizationStatus.Ok : SynchronizationStatus.Error;


                // ---------------------------------------------------------
                // COMPILACIÓN DEL DB
                // ---------------------------------------------------------
                okComp = _tiaPlcService.CompileBlock(SelectedCategory.TiaDbName);
                UpdateStatusFrame();

                if (okComp)
                {
                    okDb = _tiaPlcService.SyncDbComments(SelectedCategory.TiaDbName, SelectedCategory.TiaDbArrayName, deviceList);
                    UpdateStatusFrame();
                    SelectedCategory.DbStatus = okDb ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                }
                else
                {
                    SelectedCategory.DbStatus = SynchronizationStatus.Error;
                    LogService.Write("Fallo en compilación: no se pueden sincronizar comentarios.", true);
                }

                // Tras sincronizar, ejecutamos la comparación para verificar que todo ha quedado bien
                ExecuteCompare(keepDbStatus: true); // No reseteamos el estado DB porque acabamos de escribirlo

                // Resumen final
                if (okNMax && okConst && okComp && okDb)
                {
                    StatusService.Set("Sincronización completada con éxito.", false);
                    MessageBox.Show("Sincronización total completada.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    string msg = "Proceso finalizado con errores:\n";
                    if (!okConst) msg += "- Fallo en constantes\n";
                    if (!okComp) msg += "- Fallo en compilación\n";
                    if (!okDb) msg += "- Fallo en comentarios DB\n";

                    StatusService.Set("Sincronización finalizada con errores.", true);
                    MessageBox.Show(msg, "Incompleto", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

            }
            catch (Exception ex)
            {
                LogService.Write($"CRITICAL SYNC ERROR: {ex.Message}", true);
                StatusService.Set($"Error Crítico: {ex.Message}", true);
                SelectedCategory.DbStatus = SynchronizationStatus.Error;
            }
            finally
            {
                StatusService.SetBusy(false);
                UpdateStatusFrame();
            }
        }


        // =================================================================================================================
        //COMPARISON LOGIC (COMPROBACIÓN)
        private void ExecuteCompare() => ExecuteCompare(false);

        private void ExecuteCompare(bool keepDbStatus)
        {
            if (SelectedCategory == null || _deviceSettings == null || _engineeringCache == null) return;


            try
            {

                StatusService.SetBusy(true);
                UpdateStatusFrame();

                LogService.Write($"--- INICIANDO COMPARACIÓN: {SelectedCategory.Name} ---");
                StatusService.Set("Comparando datos con TIA Portal...", false);

                // Reset de estados
                SelectedCategory.NMaxStatus = SynchronizationStatus.Pending;
                SelectedCategory.ConstantsStatus = SynchronizationStatus.Pending;
                if (!keepDbStatus) SelectedCategory.DbStatus = SynchronizationStatus.Pending;

                // Sincronizar info de N_MAX
                StatusService.Set("Comprobando dimensión N_MAX...");
                UpdateDimensionInfo();

                int excelVal = 0;
                if (_engineeringCache.TryGetValue(_deviceSettings.Disp_N_Max, out var limits))
                {
                    var limitItem = limits.Cast<Disp_Config>()
                                          .FirstOrDefault(x => x.Nombre == SelectedCategory.GlobalConfigKey);
                    if (limitItem != null)
                    {
                        excelVal = limitItem.Valor;
                    }
                }

                bool nMaxMatch = (excelVal == _currentPlcNMax);
                SelectedCategory.NMaxStatus = nMaxMatch ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                LogService.Write($"[COMPARE] N_MAX -> Excel: {excelVal} | PLC: {_currentPlcNMax} ({(nMaxMatch ? "OK" : "ERROR")})");

                UpdateStatusFrame();


                // Exportar y Parsear PLC
                string tempXmlPath = Path.Combine(AppConfigManager.TempPath, "plc_export.xml");
                StatusService.Set($"Exportando tabla '{SelectedCategory.TiaTable}' desde TIA...");
                LogService.Write($"[COMPARE] Exportando tabla '{SelectedCategory.TiaTable}' a XML temporal...");
                UpdateStatusFrame();

                if (!_tiaPlcService.ExportTagTable(SelectedCategory.TiaGroup, SelectedCategory.TiaTable, tempXmlPath))
                {
                    LogService.Write("[COMPARE] ERROR: No se pudo exportar la tabla desde TIA Portal.", true);
                    StatusService.Set($"No se pudo exportar la tabla '{SelectedCategory.TiaTable}' desde TIA Portal.");
                    SelectedCategory.ConstantsStatus = SynchronizationStatus.Error;
                    return;
                }
                UpdateStatusFrame();

                StatusService.Set("Analizando datos recibidos del PLC...");
                var plcDict = ParsePlcXml(tempXmlPath);
                UpdateStatusFrame();

                // Obtenemos los dispositivos del Excel (los que ya están en la tabla)
                StatusService.Set("Cruzando datos Excel vs PLC...");
                var excelList = CurrentDevices.Cast<IDevice>().ToList();
                bool allMatch = true;
                int countMatch = 0, countMismatch = 0, countNew = 0;

                foreach (var device in excelList)
                {
                    if (plcDict.TryGetValue(device.Numero, out string plcTagName))
                    {
                        bool match = plcTagName == device.CPTag;

                        if (match)
                        {
                            device.Estado = "Sincronizado";
                            countMatch++;
                        }
                        else
                        {
                            device.Estado = $"{plcTagName} -> {device.CPTag}";
                            LogService.Write($"[COMPARE] Diferencia en ID {device.Numero}: PLC '{plcTagName}' != Excel '{device.CPTag}'", true);
                            allMatch = false;
                            countMismatch++;
                        }
                        plcDict.Remove(device.Numero);
                    }
                    else
                    {
                        if (device.Estado != "Eliminar")
                        {
                            device.Estado = "Nuevo";
                            LogService.Write($"[COMPARE] ID {device.Numero} no existe en PLC (Nuevo)");
                            allMatch = false;
                            countNew++;
                        }
                    }
                }

                // Detectar sobrantes en PLC
                if (plcDict.Count > 0)
                {
                    allMatch = false;
                    LogService.Write($"[COMPARE] Se han detectado {plcDict.Count} constantes en el PLC que no están en el Excel.", true);
                    
                    foreach (var extra in plcDict)
                    {
                        // Pedimos al servicio que nos cree el objeto correcto según la categoría
                        IDevice ghost = DataService.CreateEmptyDispData(SelectedCategory);

                        // Rellenamos los datos del PLC
                        ghost.Numero = extra.Key;
                        ghost.Tag = extra.Value;
                        ghost.Descripcion = "--- NO EXISTE EN EXCEL (Se borrará) ---";
                        ghost.Estado = "Eliminar";

                        // Lo inyectamos en la lista visual
                        CurrentDevices.Add(ghost);
                        LogService.Write($"[COMPARE] Sobrante en PLC -> ID {extra.Key}: {extra.Value}", true);
                    }
                }

                // 5. Resultado final
                SelectedCategory.ConstantsStatus = allMatch ? SynchronizationStatus.Ok : SynchronizationStatus.Error;

                LogService.Write($"[COMPARE] RESUMEN: {countMatch} OK, {countMismatch} Diferentes, {countNew} Nuevos.");
                LogService.Write("--- COMPARACIÓN FINALIZADA ---");

                StatusService.Set(allMatch ? "Comparación finalizada: Todo OK." : "Comparación finalizada: Se detectaron diferencias.", !allMatch);
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR CRÍTICO EN COMPARACIÓN: {ex.Message}", true);
                SelectedCategory.ConstantsStatus = SynchronizationStatus.Error;
                StatusService.Set("Error durante la comparación. Revisa el Log.", true);
            }
            finally
            {
                StatusService.SetBusy(false);
                UpdateStatusFrame();
            }
        }


        // Método auxiliar para parsear el XML exportado por TIA Portal
        private Dictionary<int, string> ParsePlcXml(string path)
        {
            var dic = new Dictionary<int, string>();
            if (!File.Exists(path)) return dic;

            try
            {
                XDocument doc = XDocument.Load(path);
                // Buscamos nodos PlcUserConstant ignorando namespaces (LocalName)
                var constants = doc.Descendants().Where(x => x.Name.LocalName.EndsWith("PlcUserConstant"));

                foreach (var con in constants)
                {
                    XNamespace ns = con.Name.Namespace;
                    var attrList = con.Element(ns + "AttributeList");
                    if (attrList == null) continue;

                    var name = attrList.Element(ns + "Name")?.Value;
                    var val = attrList.Element(ns + "Value")?.Value;

                    if (int.TryParse(val, out int id) && !string.IsNullOrEmpty(name))
                    {
                        if (!dic.ContainsKey(id)) dic.Add(id, name);
                    }
                }

                LogService.Write($"[XML-PARSE] Se han cargado {dic.Count} constantes desde el PLC.");

            }
            catch (Exception ex)
            {
                LogService.Write($"XML PARSE ERROR: {ex.Message}", true);
            }

            return dic;
        }



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
