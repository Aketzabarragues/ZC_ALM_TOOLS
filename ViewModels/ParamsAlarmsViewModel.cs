using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
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
                    LogService.Write($"[PROCESS-VM] [SelectedProcess] Proceso seleccionado: {_selectedProcess.Nombre}");
                }

                RefreshView();
            }
        }

        // ==============================================================================
        // COMANDOS
        public RelayCommand CompareCommand { get; set; }





        public ParamsAlarmsViewModel()
        {

            CompareCommand = new RelayCommand(ExecuteCompareCommand, () => _tiaPlcService != null && SelectedProcess != null);
        }



        private async void ExecuteCompareCommand()
        {
            await ExecuteCompare();
        }




        // Carga los datos provenientes del MainViewModel
        public void SetTiaService(TiaPlcService service)
        {
            _tiaPlcService = service;
        }



        public void LoadData(Dictionary<string, List<object>> cache, ConfigProcessSettings settings)
        {
            _engineeringCache = cache;
            _processSettings = settings;

            if (_engineeringCache == null || _processSettings == null) return;

            // 1. Extraer los procesos para el ComboBox
            if (_engineeringCache.TryGetValue(_processSettings.ProcessName, out var procList))
            {
                Processes.Clear(); // Vaciamos la lista actual
                foreach (var p in procList.Cast<Process>())
                {
                    Processes.Add(p); // Añadimos uno a uno para que la UI se entere
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





        private void RefreshView()
        {
            if (SelectedProcess == null || _engineeringCache == null || _processSettings == null) return;


            // Limpiamos los DataGrids visuales
            CurrentRealParams.Clear();
            CurrentIntParams.Clear();
            CurrentAlarms.Clear();


            // Buscamos en el almacén, castemos y filtramos sobre la marcha
            // 1. Parámetros Reales
            if (_engineeringCache.TryGetValue(_processSettings.PRealName, out var reals))
            {
                var filtradosReal = reals.Cast<Parameter>().Where(p => p.Proceso == SelectedProcess.Nombre);
                foreach (var p in filtradosReal) CurrentRealParams.Add(p);
            }

            // 2. Parámetros Enteros
            if (_engineeringCache.TryGetValue(_processSettings.PIntName, out var ints))
            {
                var filtradosInt = ints.Cast<Parameter>().Where(p => p.Proceso == SelectedProcess.Nombre);
                foreach (var p in filtradosInt) CurrentIntParams.Add(p);
            }

            // 3. Alarmas
            if (_engineeringCache.TryGetValue(_processSettings.AlarmName, out var alarms))
            {
                // Usamos el modelo correcto que descubrimos antes: "Alarma" en lugar de "Alarms" o "Parameter"
                var filtradasAlarmas = alarms.Cast<Alarms>().Where(a => a.Proceso == SelectedProcess.Nombre);
                foreach (var a in filtradasAlarmas) CurrentAlarms.Add(a);
            }

            LogService.Write($"[PROCESS-VM] [RefreshView] Tablas actualizadas. PReal: {CurrentRealParams.Count} | PInt: {CurrentIntParams.Count} | Alarmas: {CurrentAlarms.Count}");
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

        }





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

                // 1. Calcular nombres esperados de las variables N_MAX
                string tableName = $"{SelectedProcess.Id}_{SelectedProcess.Nombre}";
                string constReal = $"{SelectedProcess.Id}_N_MAX_P_REAL";
                string constInt = $"{SelectedProcess.Id}_N_MAX_P_INT";
                string constAlm = $"{SelectedProcess.Id}_N_MAX_ALM";
                string constAlmHmi = $"{SelectedProcess.Id}_N_MAX_ALM_HMI";

                // 2. Calcular los NOMBRES EXACTOS de los DBs según la norma del Excel
                int dbNumReal = CurrentRealParams.FirstOrDefault()?.DbNumber ?? -1;
                int dbNumInt = CurrentIntParams.FirstOrDefault()?.DbNumber ?? -1;
                int dbNumAlm = CurrentAlarms.FirstOrDefault()?.NumDB ?? -1;

                string dbNameReal = $"DB{dbNumReal}_P_REAL";
                string dbNameInt = $"DB{dbNumInt}_P_INT";
                string dbNameAlm = $"DB{dbNumAlm}_ALM";

                // 3. HEALTH CHECK: Búsqueda de Tablas y DBs por NOMBRE ESTRICTO
                LogService.Write($"[PARAMS-VM] [CheckHealth] Buscando tabla '{tableName}'...");
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

                // 4. COMPARACIÓN DE CAPACIDAD (N_MAX)
                LogService.Write("[PARAMS-VM] [CheckHealth] Leyendo capacidades N_MAX...");
                int plcMaxReal = _tiaPlcService.ReadGlobalConstant(tableName, constReal);
                int plcMaxInt = _tiaPlcService.ReadGlobalConstant(tableName, constInt);
                int plcMaxAlm = _tiaPlcService.ReadGlobalConstant(tableName, constAlm);
                int plcMaxAlmHmi = _tiaPlcService.ReadGlobalConstant(tableName, constAlmHmi);

                int expectedAlmHmi = ((SelectedProcess.NumAlarmas / 16) - 1);


                SelectedProcess.StatusPReal = (plcMaxReal == SelectedProcess.MaxPReal) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                SelectedProcess.StatusPInt = (plcMaxInt == SelectedProcess.MaxPInt) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                SelectedProcess.StatusAlm = (plcMaxAlm == SelectedProcess.NumAlarmas) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;
                SelectedProcess.StatusAlmHmi = (plcMaxAlmHmi == expectedAlmHmi) ? SynchronizationStatus.Ok : SynchronizationStatus.Error;


                // Tu Excel tiene propiedades PReal, PInt, Alarmas en el modelo Process
                bool needResize = (plcMaxReal != SelectedProcess.MaxPReal) ||
                                  (plcMaxInt != SelectedProcess.MaxPInt) ||
                                  (plcMaxAlm != SelectedProcess.NumAlarmas) ||
                                  (plcMaxAlmHmi != (expectedAlmHmi));

                if (needResize)
                {
                    LogService.Write($"[PARAMS-VM] [CheckHealth] Capacidad Difiere - REAL: PLC({plcMaxReal}) vs EXC({SelectedProcess.MaxPReal})");
                    LogService.Write($"[PARAMS-VM] [CheckHealth] Capacidad Difiere - INT: PLC({plcMaxInt}) vs EXC({SelectedProcess.MaxPInt})");
                    LogService.Write($"[PARAMS-VM] [CheckHealth] Capacidad Difiere - ALM: PLC({plcMaxAlm}) vs EXC({SelectedProcess.NumAlarmas})");
                    LogService.Write($"[PARAMS-VM] [CheckHealth] Capacidad Difiere - ALM HMI: PLC({plcMaxAlmHmi}) vs EXC({expectedAlmHmi})");

                    StatusService.Set("Comparación finalizada: Se detectaron diferencias en las constantes N_MAX. Se redimensionará al sincronizar.", StatusType.Warning);
                }
                else
                {
                    StatusService.Set("Comparación finalizada: Todo OK.", StatusType.Ok);
                }
            }
            catch (Exception ex)
            {
                StatusService.Set("Error durante la comparación. Revisa el Log.", StatusType.Error);
                LogService.Write($"[PARAMS-VM] [CheckHealth] Error crítico: {ex.Message}", true);
            }
            finally
            {
                StatusService.SetBusy(false);
            }
        }





    }
}
