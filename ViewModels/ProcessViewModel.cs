using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Siemens.Engineering.HW;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;

namespace ZC_ALM_TOOLS.ViewModels
{

    // ViewModel que gestiona la pestaña de procesos
    public class ProcessViewModel : ObservableObject
    {
        // =================================================================================================================
        // PRIVATE FIELDS & SERVICES
        private TiaService _tiaService;

        // =================================================================================================================
        // Listado de procesos (el índice que viene de procesos.xml)
        private ObservableCollection<Process> _processes;
        public ObservableCollection<Process> Processes
        {
            get => _processes;
            set { _processes = value; OnPropertyChanged(); }
        }

        // El proceso seleccionado que manda sobre toda la vista
        private Process _selectedProcess;
        public Process SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                _selectedProcess = value;
                OnPropertyChanged();
                RefreshView();
            }
        }

        // Colecciones filtradas para los DataGrids
        public ObservableCollection<Parameter> RealParams { get; set; } = new ObservableCollection<Parameter>();
        public ObservableCollection<Parameter> IntParams { get; set; } = new ObservableCollection<Parameter>();


        // Campos privados para guardar TODOS los parámetros (Caché completa)
        private List<Parameter> _allRealParams = new List<Parameter>();
        private List<Parameter> _allIntParams = new List<Parameter>();



        public ProcessViewModel()
        {


        }



        // Carga los datos provenientes del MainViewModel
        public void SetTiaService(TiaService service)
        {
            _tiaService = service;
        }

        public void LoadData(Dictionary<string, List<object>> cache)
        {
            // 1. Extraer los procesos para el ComboBox
            if (cache.TryGetValue("Procesos_Config", out var procList))
            {
                Processes = new ObservableCollection<Process>(procList.Cast<Process>());
            }

            // 2. Extraer y guardar todos los parámetros en las listas de apoyo
            if (cache.TryGetValue("P_Real", out var reals))
                _allRealParams = reals.Cast<Parameter>().ToList();

            if (cache.TryGetValue("P_Int", out var ints))
                _allIntParams = ints.Cast<Parameter>().ToList();

            // 3. Selección por defecto y refresco de vista
            if (Processes?.Count > 0 && SelectedProcess == null)
                SelectedProcess = Processes[0];
            else
                RefreshView();
        }






        private void RefreshView()
        {
            if (SelectedProcess == null) return;

            // Filtramos la caché de Reales por el nombre del proceso seleccionado
            RealParams.Clear();
            var filteredReal = _allRealParams.Where(p => p.Proceso == SelectedProcess.Nombre);
            foreach (var p in filteredReal) RealParams.Add(p);

            // Filtramos la caché de Enteros
            IntParams.Clear();
            var filteredInt = _allIntParams.Where(p => p.Proceso == SelectedProcess.Nombre);
            foreach (var p in filteredInt) IntParams.Add(p);

            StatusService.Set($"Mostrando parámetros de: {SelectedProcess.Nombre}");
        }

    }
}
