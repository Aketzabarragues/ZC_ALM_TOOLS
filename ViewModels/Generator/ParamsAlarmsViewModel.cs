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
                    _logService.Write($"[PARAMS-VM] [SelectedProcess] Proceso seleccionado: {_selectedProcess.Nombre}");
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

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;
        private readonly IAppConfigService _appConfigService;

        // Comandos
        public AsyncRelayCommand SyncCommand { get; set; }
        public AsyncRelayCommand CompareCommand { get; set; }


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public ParamsAlarmsViewModel(TiaPlcService tiaPlcService, ILogService logService, IStatusService statusService, IAppConfigService appConfigService)
        {
            _tiaPlcService = tiaPlcService;
            _logService = logService;
            _statusService = statusService;
            _appConfigService = appConfigService;

            // Enlazamos directamente a las tareas asíncronas
            SyncCommand = new AsyncRelayCommand(ExecuteSync, CanExecuteAction);
            CompareCommand = new AsyncRelayCommand(ExecuteCompare, CanExecuteAction);
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
            if (_engineeringCache.TryGetValue(_processSettings.Name, out var procList))
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
            string pRealKey = _appConfigService.GetPRealConfig()?.Name;
            if (_engineeringCache.TryGetValue(pRealKey, out var reals))
            {
                var filtradosReal = reals.Cast<Parameter>().Where(p => p.Proceso == SelectedProcess.Nombre);
                foreach (var p in filtradosReal) CurrentRealParams.Add(p);
            }

            // Parámetros Enteros
            string pIntKey = _appConfigService.GetPIntConfig()?.Name ?? "P_INT";
            if (_engineeringCache.TryGetValue(pIntKey, out var ints))
            {
                var filtradosInt = ints.Cast<Parameter>().Where(p => p.Proceso == SelectedProcess.Nombre);
                foreach (var p in filtradosInt) CurrentIntParams.Add(p);
            }

            // Alarmas
            string alarmKey = _appConfigService.GetAlarmConfig()?.Name ?? "ALM";
            if (_engineeringCache.TryGetValue(alarmKey, out var alarms))
            {
                var filtradasAlarmas = alarms.Cast<Alarms>().Where(a => a.Proceso == SelectedProcess.Nombre);
                foreach (var a in filtradasAlarmas) CurrentAlarms.Add(a);
            }

            _logService.Write($"[PARAMS-VM] [RefreshView] Tablas actualizadas. PReal: {CurrentRealParams.Count} | PInt: {CurrentIntParams.Count} | Alarmas: {CurrentAlarms.Count}");
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para actualizar que la selección del PLC ha cambiado
        /// </summary>
        public void NotifyPlcChanged(string plcName)
        {
            ActivePlcName = plcName;
            _logService.Write($"[PARAMS-VM] [NotifyPlcChanged] El PLC de origen ha cambiado. Reiniciando estados de comparación...");

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

            RefreshView();

            _statusService.Set("[PARAMS-VM] [ExecuteCompare] Comparando datos con TIA Portal...", StatusType.Ok);

            SelectedProcess.StatusPReal = SynchronizationStatus.Pending;
            SelectedProcess.StatusPInt = SynchronizationStatus.Pending;
            SelectedProcess.StatusAlm = SynchronizationStatus.Pending;
            SelectedProcess.StatusAlmHmi = SynchronizationStatus.Pending;

            try
            {
                var env = new ParamsAlarmsEnvironment(
                    SelectedProcess, _processSettings,
                    CurrentRealParams, CurrentIntParams, CurrentAlarms,
                    _tiaPlcService, forSync: false);

                if (!env.IsValid) return;

                // COMPARACIÓN DE CONSTANTES (N_MAX)
                _logService.Write("[PARAMS-VM] [ExecuteCompare] Leyendo capacidades N_MAX...");
                int plcMaxReal = _tiaPlcService.ReadGlobalConstant(env.TableName, env.ConstReal);
                int plcMaxInt = _tiaPlcService.ReadGlobalConstant(env.TableName, env.ConstInt);
                int plcMaxAlm = _tiaPlcService.ReadGlobalConstant(env.TableName, env.ConstAlm);
                int plcMaxAlmHmi = _tiaPlcService.ReadGlobalConstant(env.TableName, env.ConstAlmHmi);

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
                    _logService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - REAL: PLC({plcMaxReal}) vs EXC({SelectedProcess.MaxPReal})");
                    _logService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - INT: PLC({plcMaxInt}) vs EXC({SelectedProcess.MaxPInt})");
                    _logService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - ALM: PLC({plcMaxAlm}) vs EXC({SelectedProcess.NumAlarmas})");
                    _logService.Write($"[PARAMS-VM] [ExecuteCompare] Capacidad Difiere - ALM HMI: PLC({plcMaxAlmHmi}) vs EXC({expectedAlmHmi})");

                    _statusService.Set("[PARAMS-VM] [ExecuteCompare] Se detectaron diferencias en las constantes N_MAX. Se redimensionará al sincronizar.", StatusType.Warning);
                }

                // Compilamos los DB antes de exportar para que no de fallos
                _statusService.Set("[PARAMS-VM] [ExecuteCompare] Compilando Bloques de Datos en TIA Portal...", StatusType.Ok);

                if (env.DbNumReal != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameReal);
                if (env.DbNumInt != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameInt);
                if (env.DbNumAlm != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameAlm);


                // ==============================================================================
                // Exportacion y cruce de comentarios
                _statusService.Set("[PARAMS-VM] [ExecuteCompare] Exportando Bloques de Datos desde TIA Portal...", StatusType.Ok);

                string tempDir = AppConfigService.TempExportPathXml;
                string tempReal = Path.Combine(tempDir, "db_real.xml");
                string tempInt = Path.Combine(tempDir, "db_int.xml");
                string tempAlm = Path.Combine(tempDir, "db_alm.xml");

                bool exportOk = true;
                if (env.DbNumReal != -1) exportOk &= await _tiaPlcService.ExportBlockToXmlAsync(env.DbNameReal, tempReal);
                if (env.DbNumInt != -1) exportOk &= await _tiaPlcService.ExportBlockToXmlAsync(env.DbNameInt, tempInt);
                if (env.DbNumAlm != -1) exportOk &= await _tiaPlcService.ExportBlockToXmlAsync(env.DbNameAlm, tempAlm);

                if (!exportOk)
                {
                    _statusService.Set("[PARAMS-VM] [ExecuteCompare] Error: Fallo al exportar los DBs para leer los comentarios.", StatusType.Error);
                    return;
                }

                _statusService.Set("[PARAMS-VM] [ExecuteCompare] Cruzando comentarios Excel vs PLC...", StatusType.Ok);

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
                        Proceso = SelectedProcess.Nombre,
                        Estado = "Eliminar"
                    }
                );
                foreach (var ghost in resReal.Ghosts)
                {
                    CurrentRealParams.Add(ghost);
                    _logService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (REAL) -> ID {ghost.Numero}: {ghost.ComentarioDB}", true);
                }

                _logService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN REALES: {resReal.MatchCount} OK, {resReal.MismatchCount} Diferencias, {resReal.NewCount} Nuevos, {resReal.GhostCount} Sobrantes.");
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
                    _logService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (INT) -> ID {ghost.Numero}: {ghost.ComentarioDB}", true);
                }

                _logService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN ENTEROS: {resInt.MatchCount} OK, {resInt.MismatchCount} Diferencias, {resInt.NewCount} Nuevos, {resInt.GhostCount} Sobrantes.");
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
                        Proceso = SelectedProcess.Nombre,
                        Estado = "Eliminar"
                    }
                );
                foreach (var ghost in resAlm.Ghosts)
                {
                    CurrentAlarms.Add(ghost);
                    _logService.Write($"[PARAMS-VM] [ExecuteCompare] Sobrante en PLC (ALM) -> ID {ghost.Numero}: {ghost.ComentarioDB}", true);
                }

                _logService.Write($"[PARAMS-VM] [ExecuteCompare] RESUMEN ALARMAS: {resAlm.MatchCount} OK, {resAlm.MismatchCount} Diferencias, {resAlm.NewCount} Nuevos, {resAlm.GhostCount} Sobrantes.");
                if (!resAlm.AllMatch) allCommentsMatch = false;

                // Resultado final
                if (needResize || !allCommentsMatch)
                {
                    _statusService.Set("[PARAMS-VM] [ExecuteCompare] Comparación finalizada: Se detectaron diferencias.", StatusType.Warning);
                }
                else
                {
                    _statusService.Set("[PARAMS-VM] [ExecuteCompare] Comparación finalizada: Todo OK.", StatusType.Ok);
                }
            }
            catch (Exception ex)
            {
                _statusService.Set($"[PARAMS-VM] [ExecuteCompare] Error crítico: {ex.Message}", StatusType.Error);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Método para ejecutar la sincronización
        /// </summary>
        private async Task ExecuteSync()
        {
            // Comprobar que el usuario ha seleccionado algo para sincronizar
            if (!SelectSyncReales && !SelectSyncEnteros && !SelectSyncAlarmas)
            {
                _statusService.Set("[PARAMS-VM] [ExecuteCompare] Seleccione al menos un grupo de parámetros para sincronizar.", StatusType.Warning);
                return;
            }

            if (SelectedProcess == null || _tiaPlcService == null) return;

            _statusService.Set($"[PARAMS-VM] [ExecuteSync] Iniciando sincronización del proceso: {SelectedProcess.Nombre}", StatusType.Ok);

            try
            {
                // Preparacion y validacion del entorno
                var env = new ParamsAlarmsEnvironment(
                    SelectedProcess, _processSettings,
                    CurrentRealParams, CurrentIntParams, CurrentAlarms,
                    _tiaPlcService, forSync: true,
                    SelectSyncReales, SelectSyncEnteros, SelectSyncAlarmas);

                if (!env.IsValid) return;

                // Escritura de constantes N_MAX
                _statusService.Set("[PARAMS-VM] [ExecuteCompare] Comprobando y actualizando límites N_MAX...", StatusType.Ok);

                bool needsCompile = false;
                int expectedAlmHmi = ((SelectedProcess.NumAlarmas / 16) - 1);

                // Reales
                if (SelectSyncReales && await _tiaPlcService.SyncGlobalConstantAsync(env.TableName, env.ConstReal, SelectedProcess.MaxPReal))
                {
                    SelectedProcess.StatusPReal = SynchronizationStatus.Ok;
                    needsCompile = true;
                }

                // Enteros
                if (SelectSyncEnteros && await _tiaPlcService.SyncGlobalConstantAsync(env.TableName, env.ConstInt, SelectedProcess.MaxPInt))
                {
                    SelectedProcess.StatusPInt = SynchronizationStatus.Ok;
                    needsCompile = true;
                }

                // Alarmas y Alarmas HMI
                if (SelectSyncAlarmas)
                {
                    if (await _tiaPlcService.SyncGlobalConstantAsync(env.TableName, env.ConstAlm, SelectedProcess.NumAlarmas))
                    {
                        SelectedProcess.StatusAlm = SynchronizationStatus.Ok;
                        needsCompile = true;
                    }
                    if (await _tiaPlcService.SyncGlobalConstantAsync(env.TableName, env.ConstAlmHmi, expectedAlmHmi))
                    {
                        SelectedProcess.StatusAlmHmi = SynchronizationStatus.Ok;
                    }
                }

                // Si se ha tocado alguna constante, forzamos compilación para que los Arrays se estiren/encoja internamente
                if (needsCompile)
                {
                    _statusService.Set("[PARAMS-VM] [ExecuteCompare] Compilando DBs tras redimensionado...", StatusType.Ok);

                    if (SelectSyncReales && env.DbNumReal != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameReal);
                    if (SelectSyncEnteros && env.DbNumInt != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameInt);
                    if (SelectSyncAlarmas && env.DbNumAlm != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameAlm);
                }

                // Inyeccion de comentarios
                _statusService.Set("[PARAMS-VM] [ExecuteCompare] Inyectando textos y comentarios en los DBs...", StatusType.Ok);

                bool commentsOk = true;

                var validReals = CurrentRealParams.Where(p => p.Estado != "Eliminar").ToList();
                var validInts = CurrentIntParams.Where(p => p.Estado != "Eliminar").ToList();
                var validAlarms = CurrentAlarms.Where(a => a.Estado != "Eliminar").ToList();


                if (SelectSyncReales && env.DbNumReal != -1)
                {
                    commentsOk &= await _tiaPlcService.SyncParamsAlarmsDbCommentsAsync(env.DbNameReal, _processSettings.ArrayNameReal, validReals, p => p.Numero, p => p.ComentarioDB, true);
                }

                if (SelectSyncEnteros && env.DbNumInt != -1)
                {
                    commentsOk &= await _tiaPlcService.SyncParamsAlarmsDbCommentsAsync(env.DbNameInt, _processSettings.ArrayNameInt, validInts, p => p.Numero, p => p.ComentarioDB, true);
                }

                if (SelectSyncAlarmas && env.DbNumAlm != -1)
                {
                    commentsOk &= await _tiaPlcService.SyncParamsAlarmsDbCommentsAsync(env.DbNameAlm, _processSettings.ArrayNameAlm, validAlarms, a => a.Numero, a => a.ComentarioDB);
                }

                // Compilacion final tras modificar el DB
                _statusService.Set("[PARAMS-VM] [ExecuteCompare] Guardando y realizando compilación final...", StatusType.Ok);

                if (SelectSyncReales && env.DbNumReal != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameReal);
                if (SelectSyncEnteros && env.DbNumInt != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameInt);
                if (SelectSyncAlarmas && env.DbNumAlm != -1) await _tiaPlcService.CompileBlockAsync(env.DbNameAlm);

                _statusService.Set("[PARAMS-VM] [ExecuteSync] Sincronización finalizada con éxito.", StatusType.Ok);
            }
            catch (Exception ex)
            {
                _statusService.Set($"[PARAMS-VM] [ExecuteSync] Error crítico durante la sincronización: {ex.Message}", StatusType.Error);
            }
            finally
            {
                // Refrescamos el estado visual para comprobar cómo ha quedado tras la sincronización
                await ExecuteCompare();
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
            // 2. Tenemos un proceso seleccionado en el combo
            return _tiaPlcService != null && SelectedProcess != null;
        }

    }
}