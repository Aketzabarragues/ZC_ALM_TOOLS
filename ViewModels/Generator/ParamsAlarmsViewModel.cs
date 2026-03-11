using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.ViewModels.Generator
{

    // ==================================================================================================================
    /// <summary>
    /// ViewModel que gestiona la pestaña de parametros y alarmas
    /// </summary>
    public class ParamsAlarmsViewModel : ObservableObject
    {

        // ==============================================================================
        // Servicios y cache
        private TiaPlcService _tiaPlcService;
        private ConfigProcessSettings _processSettings;
        private Dictionary<string, List<object>> _engineeringCache;

        public string ActivePlcName { get; private set; }


        // ==============================================================================
        // Propiedades visuales
        public ObservableCollection<Process> Processes { get; } = new ObservableCollection<Process>();
        public ObservableCollection<Parameter> CurrentRealParams { get; } = new ObservableCollection<Parameter>();
        public ObservableCollection<Parameter> CurrentIntParams { get; } = new ObservableCollection<Parameter>();
        public ObservableCollection<Alarms> CurrentAlarms { get; } = new ObservableCollection<Alarms>();

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

        private bool _selectSyncReales = true;
        public bool SelectSyncReales { get => _selectSyncReales; set { _selectSyncReales = value; OnPropertyChanged(); } }

        private bool _selectSyncEnteros = true;
        public bool SelectSyncEnteros { get => _selectSyncEnteros; set { _selectSyncEnteros = value; OnPropertyChanged(); } }

        private bool _selectSyncAlarmas = true;
        public bool SelectSyncAlarmas { get => _selectSyncAlarmas; set { _selectSyncAlarmas = value; OnPropertyChanged(); } }


        // Comandos
        public RelayCommand SyncCommand { get; set; }
        public RelayCommand CompareCommand { get; set; }




        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public ParamsAlarmsViewModel(TiaPlcService tiaPlcService)
        {

            _tiaPlcService = tiaPlcService;

            SyncCommand = new RelayCommand(ExecuteSync, CanExecuteAction);
            CompareCommand = new RelayCommand(ExecuteCompareCommand, CanExecuteAction);
        }



        // ==================================================================================================================
        /// <summary>
        /// Método puente para el botón Comparar
        /// </summary>
        private async void ExecuteCompareCommand()
        {
            await ExecuteCompare();
        }



        // ==================================================================================================================
        /// <summary>
        /// Carga los datos provenientes del MainViewModel
        /// </summary>
        public void LoadData(Dictionary<string, List<object>> cache, ConfigProcessSettings settings)
        {
            _engineeringCache = cache;
            _processSettings = settings;

            if (_engineeringCache == null || _processSettings == null) return;

            // Extraer los procesos para el ComboBox
            if (_engineeringCache.TryGetValue(_processSettings.ProcessName, out var procList))
            {
                Processes.Clear(); // Vaciamos la lista actual
                foreach (var proc in procList.Cast<Process>())
                {
                    Processes.Add(proc); // Añadimos uno a uno para que la UI se entere
                }
            }

            // Seleccionar el primer proceso por defecto (esto dispara RefreshView automáticamente)
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
        /// <summary>
        /// Actualizar la vista del datagrid
        /// </summary>
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
                        
            LogService.Write($"[PARAMS-VM] [RefreshView] Tablas actualizadas. PReal: {CurrentRealParams.Count} | PInt: {CurrentIntParams.Count} | Alarmas: {CurrentAlarms.Count}");
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar que la selección del PLC ha cambiado
        /// </summary>
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
        /// <summary>
        /// Metodo para comparar con PLC
        /// </summary>
        private async Task ExecuteCompare()
        {
            if (SelectedProcess == null || _tiaPlcService == null) return;

            StatusService.SetBusy(true);

            RefreshView();

            StatusService.Set("Comparando datos con TIA Portal...", StatusType.Ok);

            SelectedProcess.StatusPReal = SynchronizationStatus.Pending;
            SelectedProcess.StatusPInt = SynchronizationStatus.Pending;
            SelectedProcess.StatusAlm = SynchronizationStatus.Pending;
            SelectedProcess.StatusAlmHmi = SynchronizationStatus.Pending;

            try
            {
                await Task.Delay(50);

                var env = new ParamsAlarmsEnvironment(
                    SelectedProcess, _processSettings,
                    CurrentRealParams, CurrentIntParams, CurrentAlarms,
                    _tiaPlcService, forSync: false);

                if (!env.IsValid) return;

                // COMPARACIÓN DE CONSTANTES (N_MAX)
                LogService.Write("[PARAMS-VM] [ExecuteCompare] Leyendo capacidades N_MAX...");
                int plcMaxReal = _tiaPlcService.ReadGlobalConstant(env.TableName, env.ConstReal);
                int plcMaxInt = _tiaPlcService.ReadGlobalConstant(env.TableName, env.ConstInt);
                int plcMaxAlm = _tiaPlcService.ReadGlobalConstant(env.TableName, env.ConstAlm);
                int plcMaxAlmHmi = _tiaPlcService.ReadGlobalConstant(env.TableName , env.ConstAlmHmi);

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

                if (env.DbNumReal != -1) _tiaPlcService.CompileBlock(env.DbNameReal);
                if (env.DbNumInt != -1) _tiaPlcService.CompileBlock(env.DbNameInt);
                if (env.DbNumAlm != -1) _tiaPlcService.CompileBlock(env.DbNameAlm);


                // ==============================================================================
                // Exportacion y cruce de comentarios
                StatusService.Set("Exportando Bloques de Datos desde TIA Portal...", StatusType.Ok);
                await Task.Delay(50);

                string tempDir = AppConfigService.TempPath;
                string tempReal = Path.Combine(tempDir, "db_real.xml");
                string tempInt = Path.Combine(tempDir, "db_int.xml");
                string tempAlm = Path.Combine(tempDir, "db_alm.xml");

                bool exportOk = true;
                if (env.DbNumReal != -1) exportOk &= _tiaPlcService.ExportBlockToXml(env.DbNameReal, tempReal);
                if (env.DbNumInt != -1) exportOk &= _tiaPlcService.ExportBlockToXml(env.DbNameInt, tempInt);
                if (env.DbNumAlm != -1) exportOk &= _tiaPlcService.ExportBlockToXml(env.DbNameAlm, tempAlm);

                if (!exportOk)
                {
                    StatusService.Set("Error: Fallo al exportar los DBs para leer los comentarios.", StatusType.Error);
                    return;
                }

                StatusService.Set("Cruzando comentarios Excel vs PLC...", StatusType.Ok);
                await Task.Delay(10);


                // Parseamos los XML
                var dicReal = new Dictionary<int, string>();
                if (env.DbNumReal != -1 && File.Exists(tempReal))
                {
                    var dbRealEditor = new XmlDataBlockEditorService(tempReal);
                    dicReal = dbRealEditor.GetArrayComments();
                }

                var dicInt = new Dictionary<int, string>();
                if (env.DbNumInt != -1 && File.Exists(tempInt))
                {
                    var dbIntEditor = new XmlDataBlockEditorService(tempInt);
                    dicInt = dbIntEditor.GetArrayComments();
                }

                var dicAlm = new Dictionary<int, string>();
                if (env.DbNumAlm != -1 && File.Exists(tempAlm))
                {
                    var dbAlmEditor = new XmlDataBlockEditorService(tempAlm);
                    dicAlm = dbAlmEditor.GetArrayComments();
                }

                bool allCommentsMatch = true;

                // Cruzar Parámetros Reales
                var resReal = ComparisonService.Compare(
                    CurrentRealParams.ToList(), 
                    dicReal,
                    p => p.Numero, 
                    p => p.ComentarioDB, 
                    p => p.Estado, 
                    (p, est) => p.Estado = est,
                    (id, txt) => new Parameter 
                    { 
                        Numero = id, 
                        ComentarioDB = txt, 
                        Descripcion = "--- NO EXISTE EN EXCEL ---", 
                        DbNumber = env.DbNumReal, 
                        Proceso = SelectedProcess.Nombre, Estado = "Eliminar" 
                    }
                );
                foreach (var ghost in resReal.Ghosts) 
                {
                    CurrentRealParams.Add(ghost); 
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (REAL) -> ID {ghost.Numero}: {ghost.ComentarioDB}", true); 
                }

                LogService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN REALES: {resReal.MatchCount} OK, {resReal.MismatchCount} Diferencias, {resReal.NewCount} Nuevos, {resReal.GhostCount} Sobrantes.");
                if (!resReal.AllMatch) allCommentsMatch = false;

                // Cruzar Parámetros Enteros
                var resInt = ComparisonService.Compare(
                    CurrentIntParams.ToList(), 
                    dicInt,
                    p => p.Numero, 
                    p => p.ComentarioDB, 
                    p => p.Estado, 
                    (p, est) => p.Estado = est,
                    (id, txt) => new Parameter 
                    { 
                        Numero = id, 
                        ComentarioDB = txt, 
                        Descripcion = "--- NO EXISTE EN EXCEL ---", 
                        DbNumber = env.DbNumInt, 
                        Proceso = SelectedProcess.Nombre, 
                        Estado = "Eliminar" 
                    }
                );
                foreach (var ghost in resInt.Ghosts) 
                { 
                    CurrentIntParams.Add(ghost); 
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (INT) -> ID {ghost.Numero}: {ghost.ComentarioDB}", true); 
                }

                LogService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN ENTEROS: {resInt.MatchCount} OK, {resInt.MismatchCount} Diferencias, {resInt.NewCount} Nuevos, {resInt.GhostCount} Sobrantes.");
                if (!resInt.AllMatch) allCommentsMatch = false;

                // Cruzar Alarmas
                var resAlm = ComparisonService.Compare(
                    CurrentAlarms.ToList(), 
                    dicAlm,
                    a => a.Numero, 
                    a => a.ComentarioDB, 
                    a => a.Estado, 
                    (a, est) => a.Estado = est,
                    (id, txt) => new Alarms 
                    { 
                        Numero = id, 
                        ComentarioDB = txt, 
                        Descripcion = "--- NO EXISTE EN EXCEL ---", 
                        DbNumber = env.DbNumAlm,
                        Proceso = SelectedProcess.Nombre, Estado = "Eliminar" 
                    }
                );
                foreach (var ghost in resAlm.Ghosts) 
                { 
                    CurrentAlarms.Add(ghost); 
                    LogService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (ALM) -> ID {ghost.Numero}: {ghost.ComentarioDB}", true); 
                }

                LogService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN ALARMAS: {resAlm.MatchCount} OK, {resAlm.MismatchCount} Diferencias, {resAlm.NewCount} Nuevos, {resAlm.GhostCount} Sobrantes.");
                if (!resAlm.AllMatch) allCommentsMatch = false;
                                
                // Resultado final
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
        /// <summary>
        /// Método para ejecutar la sincronización
        /// </summary>
        private async void ExecuteSync()
        {

            // Comprobar que el usuario ha seleccionado algo para sincronizar
            if (!SelectSyncReales && !SelectSyncEnteros && !SelectSyncAlarmas)
            {
                StatusService.Set("Seleccione al menos un grupo de parámetros para sincronizar.", StatusType.Warning);
                return;
            }


            if (SelectedProcess == null || _tiaPlcService == null) return;

            StatusService.SetBusy(true);
            StatusService.Set("Iniciando sincronización con TIA Portal...", StatusType.Ok);
            LogService.Write($"[PARAMS-VM] [ExecuteSync] Iniciando sincronización del proceso: {SelectedProcess.Nombre}");

            try
            {
                await Task.Delay(50);

                // Preparacion y validacion del entorno
                var env = new ParamsAlarmsEnvironment(
                    SelectedProcess, _processSettings,
                    CurrentRealParams, CurrentIntParams, CurrentAlarms,
                    _tiaPlcService, forSync: true,
                    SelectSyncReales, SelectSyncEnteros, SelectSyncAlarmas);

                if (!env.IsValid) return;

                // Escritura de constantes N_MAX
                StatusService.Set("Comprobando y actualizando límites N_MAX...", StatusType.Ok);
                await Task.Delay(50);

                bool needsCompile = false;
                int expectedAlmHmi = ((SelectedProcess.NumAlarmas / 16) - 1);

                // Reales
                if (SelectSyncReales && _tiaPlcService.SyncGlobalConstant(env.TableName, env.ConstReal, SelectedProcess.MaxPReal))
                {
                    SelectedProcess.StatusPReal = SynchronizationStatus.Ok;
                    needsCompile = true;
                }

                // Enteros
                if (SelectSyncEnteros && _tiaPlcService.SyncGlobalConstant(env.TableName, env.ConstInt, SelectedProcess.MaxPInt))
                {
                    SelectedProcess.StatusPInt = SynchronizationStatus.Ok;
                    needsCompile = true;
                }

                // Alarmas y Alarmas HMI
                if (SelectSyncAlarmas)
                {
                    if (_tiaPlcService.SyncGlobalConstant(env.TableName, env.ConstAlm, SelectedProcess.NumAlarmas))
                    {
                        SelectedProcess.StatusAlm = SynchronizationStatus.Ok;
                        needsCompile = true;
                    }
                    if (_tiaPlcService.SyncGlobalConstant(env.TableName, env.ConstAlmHmi, expectedAlmHmi))
                    {
                        SelectedProcess.StatusAlmHmi = SynchronizationStatus.Ok;
                    }
                }

                // Si se ha tocado alguna constante, forzamos compilación para que los Arrays se estiren/encoja internamente
                if (needsCompile)
                {
                    StatusService.Set("Compilando DBs tras redimensionado...", StatusType.Ok);
                    await Task.Delay(50);
                    if (SelectSyncReales && env.DbNumReal != -1) _tiaPlcService.CompileBlock(env.DbNameReal);
                    if (SelectSyncEnteros && env.DbNumInt != -1) _tiaPlcService.CompileBlock(env.DbNameInt);
                    if (SelectSyncAlarmas && env.DbNumAlm != -1) _tiaPlcService.CompileBlock(env.DbNameAlm);
                }

                // Inyeccion de comentarios
                StatusService.Set("Inyectando textos y comentarios en los DBs...", StatusType.Ok);
                await Task.Delay(50);

                bool commentsOk = true;

                var validReals = CurrentRealParams.Where(p => p.Estado != "Eliminar").ToList();
                var validInts = CurrentIntParams.Where(p => p.Estado != "Eliminar").ToList();
                var validAlarms = CurrentAlarms.Where(a => a.Estado != "Eliminar").ToList();

                if (SelectSyncReales && env.DbNumReal != -1)
                {
                    commentsOk &= _tiaPlcService.SyncParamsAlarmsDbComments(env.DbNameReal, "PReal", validReals, p => p.Numero, p => p.ComentarioDB, true);
                }

                if (SelectSyncEnteros && env.DbNumInt != -1)
                {
                    commentsOk &= _tiaPlcService.SyncParamsAlarmsDbComments(env.DbNameInt, "PInt", validInts, p => p.Numero, p => p.ComentarioDB, true);
                }

                if (SelectSyncAlarmas && env.DbNumAlm != -1)
                {
                    commentsOk &= _tiaPlcService.SyncParamsAlarmsDbComments(env.DbNameAlm, "ALM", validAlarms, a => a.Numero, a => a.ComentarioDB);
                }

                // Compilacion final tras modificar el DB
                StatusService.Set("Guardando y realizando compilación final...", StatusType.Ok);
                await Task.Delay(50);

                if (SelectSyncReales && env.DbNumReal != -1) _tiaPlcService.CompileBlock(env.DbNameReal);
                if (SelectSyncEnteros && env.DbNumInt != -1) _tiaPlcService.CompileBlock(env.DbNameInt);
                if (SelectSyncAlarmas && env.DbNumAlm != -1) _tiaPlcService.CompileBlock(env.DbNameAlm);

                LogService.Write("[PARAMS-VM] [ExecuteSync] SINCRONIZACIÓN FINALIZADA CORRECTAMENTE.");
                StatusService.Set("Sincronización finalizada con éxito.", StatusType.Ok);

            }
            catch (Exception ex)
            {
                StatusService.Set("Error crítico durante la sincronización. Revisa el Log.", StatusType.Error);
                LogService.Write($"[PARAMS-VM] [ExecuteSync] Error crítico: {ex.Message}", true);
            }
            finally
            {
                await ExecuteCompare();
                StatusService.SetBusy(false);
            }

        }


        // ==================================================================================================================
        /// <summary>
        /// Metodo para habilitar botones
        /// </summary>
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
