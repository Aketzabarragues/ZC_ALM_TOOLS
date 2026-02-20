using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public class ProcessViewModel : ObservableObject
    {
        // ==============================================================================
        // SERVICIOS Y CACHÉS
        private TiaPlcService _tiaPlcService;
        private ConfigProcessSettings _processSettings;
        private Dictionary<string, List<object>> _engineeringCache;




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
                    LogService.Write($"[UI] Proceso seleccionado: {_selectedProcess.Nombre}");
                }

                RefreshView();
            }
        }




        public RelayCommand DumpProcessesCommand { get; set; }





        public ProcessViewModel()
        {

            DumpProcessesCommand = new RelayCommand(() => ExecuteDumpProcesses());
        }





        private void ExecuteDumpProcesses()
        {

            LogService.Write("[UI] >>> BOTÓN DUMP PROCESOS PULSADO <<<");

            try
            {
                // Usamos la ruta base tal y como has pedido
                string filePath = Path.Combine(AppConfigManager.BasePath, "ZC_Process_Dump.txt");

                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("=================================================");
                    writer.WriteLine("       REPORTE DE PROCESOS (ProcessViewModel)    ");
                    writer.WriteLine($"       Fecha: {DateTime.Now}                     ");
                    writer.WriteLine("=================================================\n");

                    if (Processes == null || Processes.Count == 0)
                    {
                        writer.WriteLine("LA LISTA DE PROCESOS ESTÁ VACÍA.");
                    }
                    else
                    {
                        writer.WriteLine($">> TOTAL PROCESOS EN COMBOBOX: {Processes.Count}");
                        writer.WriteLine("=================================================");

                        // Bucle recorriendo todos los procesos
                        for (int i = 0; i < Processes.Count; i++)
                        {
                            var obj = Processes[i];
                            writer.WriteLine($"\n  [{i + 1}] Objeto Tipo: {obj.GetType().Name}");

                            // Reflexión para leer las propiedades automáticamente igual que en el Main
                            var properties = obj.GetType().GetProperties();
                            foreach (var prop in properties)
                            {
                                var valor = prop.GetValue(obj) ?? "NULL";
                                writer.WriteLine($"    - {prop.Name}: {valor}");
                            }
                        }
                    }
                }

                MessageBox.Show($"Volcado de Procesos generado con éxito en:\n{filePath}", "Dump OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR generando dump de procesos: {ex.Message}", true);
                MessageBox.Show($"Error generando el archivo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            // Magia LINQ Directa: Buscamos en el almacén, castemos y filtramos sobre la marcha

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

            LogService.Write($"[UI] Tablas actualizadas. PReal: {CurrentRealParams.Count} | PInt: {CurrentIntParams.Count} | Alarmas: {CurrentAlarms.Count}");
        }



    }
}
