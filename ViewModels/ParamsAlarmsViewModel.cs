using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Siemens.Engineering.HW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{

    // ViewModel que gestiona la pestaña de procesos
    public class ParamsAlarmsViewModel : ObservableObject
    {
        // ==============================================================================
        // SERVICIOS Y CACHÉS
        private TiaPlcService _tiaPlcService;
        private ConfigProcessSettings _processSettings;
        private Dictionary<string, List<object>> _engineeringCache;

        public string ActivePlcName { get; private set; }


        // ==============================================================================
        // PROPIEDADES VISUALES (Binding al UI)
        public ObservableCollection<Process> Processes { get; set; } = new ObservableCollection<Process>();

        public ObservableCollection<Parameter> CurrentRealParams { get; set; } = new ObservableCollection<Parameter>();
        public ObservableCollection<Parameter> CurrentIntParams { get; set; } = new ObservableCollection<Parameter>();
        public ObservableCollection<Alarms> CurrentAlarms { get; set; } = new ObservableCollection<Alarms>();

        private Process _selectedProcess;
        public Process SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                _selectedProcess = value;
                OnPropertyChanged();

                if (_selectedProcess != null)
                {
                    LogService.Write($"[PARAMS-VM] [SelectedProcess] Proceso seleccionado: {_selectedProcess.Nombre}");
                }

                RefreshView();
            }
        }

        // ==============================================================================
        // COMANDOS
        public RelayCommand SyncCommand { get; set; }
        public RelayCommand CompareCommand { get; set; }



        // =============
        public ParamsAlarmsViewModel()
        {
            SyncCommand = new RelayCommand(ExecuteSync, CanExecuteAction);
            CompareCommand = new RelayCommand(ExecuteCompareCommand, CanExecuteAction);
        }



        // ==================================================================================================================
        // Método puente para el botón Comparar
        private async void ExecuteCompareCommand()
        {
            await ExecuteCompare();
        }



        // ==================================================================================================================
        // Asigna la instancia de Tia Portal
        public void SetTiaService(TiaPlcService service)
        {
            _tiaPlcService = service;
        }



        // ==================================================================================================================
        // Carga los datos provenientes del MainViewModel
        public void LoadData(Dictionary<string, List<object>> cache, ConfigProcessSettings settings)
        {
            _engineeringCache = cache;
            _processSettings = settings;

            if (_engineeringCache == null || _processSettings == null) return;

            // 1. Extraer los procesos para el ComboBox
            if (_engineeringCache.TryGetValue(_processSettings.ProcessName, out var procList))
            {
                Processes.Clear(); // Vaciamos la lista actual
                foreach (var proc in procList.Cast<Process>())
                {
                    Processes.Add(proc); // Añadimos uno a uno para que la UI se entere
                }
            }

            // 2. Seleccionar el primer proceso por defecto (esto dispara RefreshView automáticamente)
            if (Processes.Count > 0 && SelectedProcess == null)
            {
                SelectedProcess = Processes[0];
            }
            else
            {
                RefreshView();
            }
        }



        // ==================================================================================================================
        // Actualizar la vista del datagrid
        private void RefreshView()
        {
            if (SelectedProcess == null || _engineeringCache == null || _processSettings == null) return;


            // Limpiamos los DataGrids visuales
            CurrentRealParams.Clear();
            CurrentIntParams.Clear();
            CurrentAlarms.Clear();


            // Buscamos en la cache, castemos y filtramos sobre la marcha
            // Parámetros Reales
            if (_engineeringCache.TryGetValue(_processSettings.PRealName, out var reals))
            {
                var filtradosReal = reals.Cast<Parameter>().Where(p => p.Proceso == SelectedProcess.Nombre);
                foreach (var p in filtradosReal) CurrentRealParams.Add(p);
            }

            // Parámetros Enteros
            if (_engineeringCache.TryGetValue(_processSettings.PIntName, out var ints))
            {
                var filtradosInt = ints.Cast<Parameter>().Where(p => p.Proceso == SelectedProcess.Nombre);
                foreach (var p in filtradosInt) CurrentIntParams.Add(p);
            }

            // Alarmas
            if (_engineeringCache.TryGetValue(_processSettings.AlarmName, out var alarms))
            {
                // Usamos el modelo correcto que descubrimos antes: "Alarma" en lugar de "Alarms" o "Parameter"
                var filtradasAlarmas = alarms.Cast<Alarms>().Where(a => a.Proceso == SelectedProcess.Nombre);
                foreach (var a in filtradasAlarmas) CurrentAlarms.Add(a);
            }

            // Reseteamos el estado de los modelos en caché para que vuelvan a salir grises ("Pendiente")
            foreach (var list in _engineeringCache.Values)
            {
                foreach (var item in list)
                {
                    if (item is Parameter p) p.Estado = "Pendiente";
                    if (item is Alarms a) a.Estado = "Pendiente";
                }
            }

            LogService.Write($"[PARAMS-VM] [RefreshView] Tablas actualizadas. PReal: {CurrentRealParams.Count} | PInt: {CurrentIntParams.Count} | Alarmas: {CurrentAlarms.Count}");
        }



        // ==================================================================================================================
        // Método para actualizar que la selección del PLC ha cambiado
        public void NotifyPlcChanged(string plcName)
        {
            ActivePlcName = plcName;
            LogService.Write($"[PARAMS-VM] [NotifyPlcChanged] El PLC de origen ha cambiado. Reiniciando estados de comparación...");

            // Ponemos todos los indicadores de todos los procesos en "Pendiente"
            foreach (var proc in Processes)
            {
                proc.StatusPReal = SynchronizationStatus.Pending;
                proc.StatusPInt = SynchronizationStatus.Pending;
                proc.StatusAlm = SynchronizationStatus.Pending;
                proc.StatusAlmHmi = SynchronizationStatus.Pending;
            }

            // Reseteamos los datos de comparacion de las tablas
            if (_engineeringCache != null)
            {
                foreach (var list in _engineeringCache.Values)
                {
                    foreach (var item in list)
                    {
                        if (item is Parameter p) p.Estado = "Pendiente";
                        if (item is Alarms a) a.Estado = "Pendiente";
                    }
                }
            }

            RefreshView();
        }



        // ==================================================================================================================
        // Metodo para comparar con PLC
        private async Task ExecuteCompare()
        {
            if (SelectedProcess == null || _tiaPlcService == null) return;

            StatusService.SetBusy(true);
            StatusService.Set("Comparando datos con TIA Portal...", StatusType.Ok);

            SelectedProcess.StatusPReal = SynchronizationStatus.Pending;
            SelectedProcess.StatusPInt = SynchronizationStatus.Pending;
            SelectedProcess.StatusAlm = SynchronizationStatus.Pending;
            SelectedProcess.StatusAlmHmi = SynchronizationStatus.Pending;

            try
            {
                await Task.Delay(50);

                // Calcular nombres esperados de las variables N_MAX
                string tableName = $"{SelectedProcess.Id}_{SelectedProcess.Nombre}";
                string constReal = $"{SelectedProcess.Id}{_processSettings.SuffixConstReal}";
                string constInt = $"{SelectedProcess.Id}{_processSettings.SuffixConstInt}";
                string constAlm = $"{SelectedProcess.Id}{_processSettings.SuffixConstAlm}";
                string constAlmHmi = $"{SelectedProcess.Id}{_processSettings.SuffixConstAlmHmi}";

                // Calcular los NOMBRES EXACTOS de los DBs según la norma del Excel
                int dbNumReal = CurrentRealParams.FirstOrDefault()?.DbNumber ?? -1;
                int dbNumInt = CurrentIntParams.FirstOrDefault()?.DbNumber ?? -1;
                int dbNumAlm = CurrentAlarms.FirstOrDefault()?.NumDB ?? -1;

                string dbNameReal = $"DB{dbNumReal}{_processSettings.SuffixDbReal}";
                string dbNameInt = $"DB{dbNumInt}{_processSettings.SuffixDbInt}";
                string dbNameAlm = $"DB{dbNumAlm}{_processSettings.SuffixDbAlm}";

                // Búsqueda de Tablas y DBs por NOMBRE ESTRICTO
                LogService.Write($"[PARAMS-VM] [ExecuteCompare] Buscando tabla '{tableName}'...");
                var table = _tiaPlcService.FindTagTableRecursively(tableName);

                if (table == null)
                {
                    StatusService.Set($"Error: No se encuentra la tabla '{tableName}' en el PLC.", StatusType.Error);
                    return;
                }

                await Task.Delay(10);

                if (dbNumReal != -1 && _tiaPlcService.FindBlockByName(dbNameReal) == null)
                {
                    StatusService.Set($"Error: No se encuentra el bloque '{dbNameReal}' en el PLC.", StatusType.Error);
                    return;
                }

                await Task.Delay(10);

                if (dbNumInt != -1 && _tiaPlcService.FindBlockByName(dbNameInt) == null)
                {
                    StatusService.Set($"Error: No se encuentra el bloque '{dbNameInt}' en el PLC.", StatusType.Error);
                    return;
                }

                await Task.Delay(10);

                if (dbNumAlm != -1 && _tiaPlcService.FindBlockByName(dbNameAlm) == null)
                {
                    StatusService.Set($"Error: No se encuentra el bloque '{dbNameAlm}' en el PLC.", StatusType.Error);
                    return;
                }

                // COMPARACIÓN DE CONSTANTES (N_MAX)
                LogService.Write("[PARAMS-VM] [ExecuteCompare] Leyendo capacidades N_MAX...");
                int plcMaxReal = _tiaPlcService.ReadGlobalConstant(tableName, constReal);
                int plcMaxInt = _tiaPlcService.ReadGlobalConstant(tableName, constInt);
                int plcMaxAlm = _tiaPlcService.ReadGlobalConstant(tableName, constAlm);
                int plcMaxAlmHmi = _tiaPlcService.ReadGlobalConstant(tableName, constAlmHmi);

                // Calculo de constante para alarmas HMI
                int expectedAlmHmi = ((SelectedProcess.NumAlarmas / 16) - 1);

                // Actualizamos los estados de los N_MAX
                SelectedProcess.StatusPReal = (plcMaxReal == SelectedProcess.MaxPReal) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                SelectedProcess.StatusPInt = (plcMaxInt == SelectedProcess.MaxPInt) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                SelectedProcess.StatusAlm = (plcMaxAlm == SelectedProcess.NumAlarmas) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                SelectedProcess.StatusAlmHmi = (plcMaxAlmHmi == expectedAlmHmi) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;


                // Revisamos si necesitamos redimensionar los DB
                bool needResize = (plcMaxReal != SelectedProcess.MaxPReal) ||
                                  (plcMaxInt != SelectedProcess.MaxPInt) ||
                                  (plcMaxAlm != SelectedProcess.NumAlarmas) ||
                                  (plcMaxAlmHmi != (expectedAlmHmi));

                if (needResize)
                {
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - REAL: PLC({plcMaxReal}) vs EXC({SelectedProcess.MaxPReal})");
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - INT: PLC({plcMaxInt}) vs EXC({SelectedProcess.MaxPInt})");
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - ALM: PLC({plcMaxAlm}) vs EXC({SelectedProcess.NumAlarmas})");
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - ALM HMI: PLC({plcMaxAlmHmi}) vs EXC({expectedAlmHmi})");

                    StatusService.Set("Comparación finalizada: Se detectaron diferencias en las constantes N_MAX. Se redimensionará al sincronizar.", StatusType.Warning);
                }



                // Compilamos los DB antes de exportar para que no de fallos
                StatusService.Set("Compilando Bloques de Datos en TIA Portal...", StatusType.Ok);
                await Task.Delay(50);

                if (dbNumReal != -1) _tiaPlcService.CompileBlock(dbNameReal);
                if (dbNumInt != -1) _tiaPlcService.CompileBlock(dbNameInt);
                if (dbNumAlm != -1) _tiaPlcService.CompileBlock(dbNameAlm);


                // ==============================================================================
                // EXPORTACIÓN Y CRUCE DE COMENTARIOS
                // ==============================================================================
                StatusService.Set("Exportando Bloques de Datos desde TIA Portal...", StatusType.Ok);
                await Task.Delay(50);

                string tempDir = AppConfigService.TempPath;
                string tempReal = Path.Combine(tempDir, "db_real.xml");
                string tempInt = Path.Combine(tempDir, "db_int.xml");
                string tempAlm = Path.Combine(tempDir, "db_alm.xml");

                bool exportOk = true;
                if (dbNumReal != -1) exportOk &= _tiaPlcService.ExportBlockToXml(dbNameReal, tempReal);
                if (dbNumInt != -1) exportOk &= _tiaPlcService.ExportBlockToXml(dbNameInt, tempInt);
                if (dbNumAlm != -1) exportOk &= _tiaPlcService.ExportBlockToXml(dbNameAlm, tempAlm);

                if (!exportOk)
                {
                    StatusService.Set("Error: Fallo al exportar los DBs para leer los comentarios.", StatusType.Error);
                    return;
                }

                StatusService.Set("Cruzando comentarios Excel vs PLC...", StatusType.Ok);
                await Task.Delay(10);


                // Parseamos los XML
                var dicReal = ParseDbCommentsXml(tempReal);
                var dicInt = ParseDbCommentsXml(tempInt);
                var dicAlm = ParseDbCommentsXml(tempAlm);

                bool allCommentsMatch = true;
                int countMatch = 0, countMismatch = 0, countNew = 0;

                // =========================================================
                // 1. Cruzar Parámetros Reales
                var excelRealList = CurrentRealParams.ToList();
                foreach (var param in excelRealList)
                {
                    if (dicReal.TryGetValue(param.Numero, out string plcComment))
                    {
                        if (plcComment == param.ComentarioDB)
                        {
                            param.Estado = "Sincronizado";
                            countMatch++;
                        }
                        else
                        {
                            param.Estado = $"{plcComment} -> {param.ComentarioDB}";
                            allCommentsMatch = false;
                            countMismatch++;
                        }
                        dicReal.Remove(param.Numero);
                    }
                    else
                    {
                        if (param.Estado != "Eliminar")
                        {
                            param.Estado = "Nuevo";
                            allCommentsMatch = false;
                            countNew++;
                        }
                    }
                }

                if (dicReal.Count > 0)
                {
                    allCommentsMatch = false;
                    foreach (var extra in dicReal)
                    {
                        CurrentRealParams.Add(new Parameter
                        {
                            Numero = extra.Key,
                            ComentarioDB = extra.Value,
                            Descripcion = "--- NO EXISTE EN EXCEL (Se borrará) ---",
                            DbNumber = dbNumReal,
                            Proceso = SelectedProcess.Nombre,
                            Estado = "Eliminar"
                        });
                        LogService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (REAL) -> ID {extra.Key}: {extra.Value}", true);
                    }
                }

                LogService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN REALES: {countMatch} OK, {countMismatch} Diferentes, {countNew} Nuevos, {dicReal.Count} Sobrantes.");

                // Reset de contadores para el siguiente grupo
                countMatch = 0; countMismatch = 0; countNew = 0;

                // =========================================================
                // 2. Cruzar Parámetros Enteros
                var excelIntList = CurrentIntParams.ToList();
                foreach (var param in excelIntList)
                {
                    if (dicInt.TryGetValue(param.Numero, out string plcComment))
                    {
                        if (plcComment == param.ComentarioDB)
                        {
                            param.Estado = "Sincronizado";
                            countMatch++;
                        }
                        else
                        {
                            param.Estado = $"{plcComment} -> {param.ComentarioDB}";
                            allCommentsMatch = false;
                            countMismatch++;
                        }
                        dicInt.Remove(param.Numero);
                    }
                    else
                    {
                        if (param.Estado != "Eliminar")
                        {
                            param.Estado = "Nuevo";
                            allCommentsMatch = false;
                            countNew++;
                        }
                    }
                }

                if (dicInt.Count > 0)
                {
                    allCommentsMatch = false;
                    foreach (var extra in dicInt)
                    {
                        CurrentIntParams.Add(new Parameter
                        {
                            Numero = extra.Key,
                            ComentarioDB = extra.Value,
                            Descripcion = "--- NO EXISTE EN EXCEL (Se borrará) ---",
                            DbNumber = dbNumInt,
                            Proceso = SelectedProcess.Nombre,
                            Estado = "Eliminar"
                        });
                        LogService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (INT) -> ID {extra.Key}: {extra.Value}", true);
                    }
                }

                LogService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN ENTEROS: {countMatch} OK, {countMismatch} Diferentes, {countNew} Nuevos, {dicInt.Count} Sobrantes.");

                // Reset de contadores para el siguiente grupo
                countMatch = 0; countMismatch = 0; countNew = 0;

                // =========================================================
                // 3. Cruzar Alarmas
                var excelAlmList = CurrentAlarms.ToList();
                foreach (var alm in excelAlmList)
                {
                    if (dicAlm.TryGetValue(alm.Numero, out string plcComment))
                    {
                        if (plcComment == alm.ComentarioDB)
                        {
                            alm.Estado = "Sincronizado";
                            countMatch++;
                        }
                        else
                        {
                            alm.Estado = $"{plcComment} -> {alm.ComentarioDB}";
                            allCommentsMatch = false;
                            countMismatch++;
                        }
                        dicAlm.Remove(alm.Numero);
                    }
                    else
                    {
                        if (alm.Estado != "Eliminar")
                        {
                            alm.Estado = "Nuevo";
                            allCommentsMatch = false;
                            countNew++;
                        }
                    }
                }

                if (dicAlm.Count > 0)
                {
                    allCommentsMatch = false;
                    foreach (var extra in dicAlm)
                    {
                        CurrentAlarms.Add(new Alarms
                        {
                            Numero = extra.Key,
                            ComentarioDB = extra.Value,
                            Descripcion = "--- NO EXISTE EN EXCEL (Se borrará) ---",
                            NumDB = dbNumAlm,
                            Proceso = SelectedProcess.Nombre,
                            Estado = "Eliminar"
                        });
                        LogService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (ALM) -> ID {extra.Key}: {extra.Value}", true);
                    }
                }

                LogService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN ALARMAS: {countMatch} OK, {countMismatch} Diferentes, {countNew} Nuevos, {dicAlm.Count} Sobrantes.");


                // ==============================================================================
                // RESULTADO FINAL EN LA BARRA DE ESTADO
                if (needResize || !allCommentsMatch)
                {
                    StatusService.Set("Comparación finalizada: Se detectaron diferencias.", StatusType.Warning);
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Comparación finalizada: Se detectaron diferencias.", false);
                }
                else
                {
                    StatusService.Set("Comparación finalizada: Todo OK.", StatusType.Ok);
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Comparación finalizada: Todo OK.", false);
                }
            }
            catch (Exception ex)
            {
                StatusService.Set("Error durante la comparación. Revisa el Log.", StatusType.Error);
                LogService.Write($"[PARAMS-VM] [ExecuteCompare] Error crítico: {ex.Message}", true);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }






        // ==================================================================================================================
        // Método para ejecutar la sincronización
        private async void ExecuteSync()
        {
            if (SelectedProcess == null || _tiaPlcService == null) return;

            var confirm = MessageBox.Show($"¿Deseas sincronizar los parámetros de {SelectedProcess.Nombre}?\n(Modo Prueba: Solo exportará los DBs a XML)",
                                          "Confirmar Sincronización", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (confirm != MessageBoxResult.Yes) return;

            StatusService.SetBusy(true);

            try
            {
                StatusService.Set($"Inicio exportación de prueba: {SelectedProcess.Nombre} ---", StatusType.Ok);
                LogService.Write($"[PARAMS-VM] [ExecuteSync] Inicio exportación de DBs: {SelectedProcess.Nombre} ---");

                await Task.Delay(50);

                // 1. Calcular nombres esperados de las variables N_MAX
                string tableName = $"{SelectedProcess.Id}_{SelectedProcess.Nombre}";

                // 2. Calcular los NOMBRES EXACTOS de los DBs según la norma
                int dbNumReal = CurrentRealParams.FirstOrDefault()?.DbNumber ?? -1;
                int dbNumInt = CurrentIntParams.FirstOrDefault()?.DbNumber ?? -1;
                int dbNumAlm = CurrentAlarms.FirstOrDefault()?.NumDB ?? -1;

                string dbNameReal = $"DB{dbNumReal}{_processSettings.SuffixDbReal}";
                string dbNameInt = $"DB{dbNumInt}{_processSettings.SuffixDbInt}";
                string dbNameAlm = $"DB{dbNumAlm}{_processSettings.SuffixDbAlm}";

                // 3. Exportar a Temp
                string tempDir = AppConfigService.TempPath;
                string tempReal = Path.Combine(tempDir, "TEST_db_real.xml");
                string tempInt = Path.Combine(tempDir, "TEST_db_int.xml");
                string tempAlm = Path.Combine(tempDir, "TEST_db_alm.xml");

                bool exportOk = true;

                StatusService.Set($"Exportando Bloques de Datos a {tempDir}...", StatusType.Ok);

                if (dbNumReal != -1) exportOk &= _tiaPlcService.ExportBlockToXml(dbNameReal, tempReal);
                if (dbNumInt != -1) exportOk &= _tiaPlcService.ExportBlockToXml(dbNameInt, tempInt);
                if (dbNumAlm != -1) exportOk &= _tiaPlcService.ExportBlockToXml(dbNameAlm, tempAlm);

                if (exportOk)
                {
                    StatusService.Set("Exportación de prueba completada. Revisa la carpeta temporal.", StatusType.Ok);
                    MessageBox.Show($"DBs exportados con éxito en la ruta:\n{tempDir}", "Exportación OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusService.Set("Error al exportar los bloques. Asegúrate de que existan en el PLC.", StatusType.Error);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"[PARAMS-VM] [ExecuteSync] Error Crítico: {ex.Message}", true);
                StatusService.Set("Error durante la sincronización. Revisa el Log.", StatusType.Error);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }

        // ==================================================================================================================
        // Método para parsear los DBs exportados y sacar los comentarios de los Arrays
        private Dictionary<int, string> ParseDbCommentsXml(string path)
        {
            var dic = new Dictionary<int, string>();
            if (!File.Exists(path)) return dic;

            try
            {
                XDocument doc = XDocument.Load(path);

                // Buscamos todos los Subelement que tengan atributo Path (índices de los Arrays)
                var subelements = doc.Descendants().Where(x => x.Name.LocalName == "Subelement" && x.Attribute("Path") != null);

                foreach (var sub in subelements)
                {
                    if (int.TryParse(sub.Attribute("Path").Value, out int id))
                    {
                        var commentNode = sub.Descendants().FirstOrDefault(x => x.Name.LocalName == "MultiLanguageText" && x.Attribute("Lang")?.Value == "es-ES");

                        if (commentNode != null)
                        {
                            string comment = commentNode.Value;

                            // Usamos ContainsKey por si hay otros arrays (como 'Vis') que repiten los índices. 
                            // Solo nos interesa quedarnos con el primero que encuentre.
                            if (!dic.ContainsKey(id))
                            {
                                dic.Add(id, comment);
                            }
                        }
                    }
                }
                LogService.Write($"[PARAMS-VM] [ParseDbXml] Leídos {dic.Count} comentarios del archivo {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                LogService.Write($"[PARAMS-VM] [ParseDbXml] XML PARSE ERROR en {Path.GetFileName(path)}: {ex.Message}", true);
            }

            return dic;
        }



        // ==================================================================================================================
        // Metodo para habilitar botones
        private bool CanExecuteAction()
        {
            // Solo habilitamos si:
            // 1. Tenemos servicio de TIA conectado
            // 2. Tenemos una categoría seleccionada en el combo
            // 3. Hay dispositivos en la lista (no está vacía)
            return _tiaPlcService != null && SelectedProcess != null;
        }


    }
}
