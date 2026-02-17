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
    public class DispositivosViewModel : ObservableObject
    {
        // =================================================================================================================
        // --- PROPIEDADES PRIVADAS ---

        private Dictionary<string, List<object>> _datosInyectados;
        private Dictionary<string, int> _datosGlobales;
        private Dictionary<string, int> _cacheNMaxPlc = new Dictionary<string, int>();
        private TiaService _tiaService;
        private int _nMaxTia = 0;
        private bool _proyectoCargado;

        public bool ProyectoCargado
        {
            get => _proyectoCargado;
            set { _proyectoCargado = value; OnPropertyChanged(); }
        }

        // =================================================================================================================
        // --- BINDINGS PARA LA UI ---

        public string NMaxInfo { get; private set; }
        private string _nMaxInfo;
        public string NMaxInfoProp { get => _nMaxInfo; set { _nMaxInfo = value; OnPropertyChanged(nameof(NMaxInfoProp)); } }

        private string _nMaxColor = "Transparent";
        public string NMaxColor { get => _nMaxColor; set { _nMaxColor = value; OnPropertyChanged(); } }

        public ObservableCollection<object> ListaDispositivos { get; set; } = new ObservableCollection<object>();
        public List<DeviceCategory> Categorias { get; set; }

        private DeviceCategory _categoriaSeleccionada;
        public DeviceCategory CategoriaSeleccionada
        {
            get => _categoriaSeleccionada;
            set { _categoriaSeleccionada = value; OnPropertyChanged(); RefrescarVista(); }
        }

        // =================================================================================================================
        // --- COMANDOS ---

        public RelayCommand SyncConstantesCommand { get; set; }
        public RelayCommand ComparacionCommand { get; set; }

        public DispositivosViewModel()
        {
            SyncConstantesCommand = new RelayCommand(() => EjecutarSyncConstantes(), () => ProyectoCargado);
            ComparacionCommand = new RelayCommand(() => EjecutarComparacion(), () => ProyectoCargado);
        }

        public void SetTiaService(TiaService service) => _tiaService = service;

        public void SetDatos(Dictionary<string, List<object>> datos, Dictionary<string, int> globales)
        {
            _datosInyectados = datos;
            _datosGlobales = globales;
            RefrescarVista();
        }

        private void RefrescarVista()
        {
            if (CategoriaSeleccionada == null || _datosInyectados == null) return;

            ListaDispositivos.Clear();
            if (_datosInyectados.TryGetValue(CategoriaSeleccionada.Name, out var lista))
            {
                foreach (var item in lista) ListaDispositivos.Add(item);
            }

            ActualizarLabelNMax();
        }

        private void ActualizarLabelNMax()
        {
            if (CategoriaSeleccionada == null || _datosGlobales == null) return;

            _datosGlobales.TryGetValue(CategoriaSeleccionada.GlobalConfigKey, out int nMaxExcel);

            // Consultamos al PLC o usamos caché
            if (!_cacheNMaxPlc.TryGetValue(CategoriaSeleccionada.Name, out _nMaxTia))
            {
                _nMaxTia = _tiaService.ObtenerValorConstante("000_Config_Dispositivos", CategoriaSeleccionada.PlcCountConstant);
                _cacheNMaxPlc[CategoriaSeleccionada.Name] = _nMaxTia;
            }

            NMaxInfoProp = $"Dimensión: Excel ({nMaxExcel}) | PLC ({_nMaxTia})";
            NMaxColor = (nMaxExcel == _nMaxTia) ? "#A5D6A7" : "#EF9A9A";
        }

        // =================================================================================================================
        // --- PROCESO DE SINCRONIZACIÓN ---

        private void EjecutarSyncConstantes()
        {
            if (CategoriaSeleccionada == null) return;

            var result = MessageBox.Show("¿Deseas sincronizar dimensiones, constantes y comentarios?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            bool exitoNMax = true, exitoConst = false, exitoComp = false, exitoDB = false;

            try
            {
                LogService.Write($"--- INICIO SINCRONIZACIÓN TOTAL: {CategoriaSeleccionada.Name} ---");

                // 1. PASO A: N_MAX
                if (_datosGlobales.TryGetValue(CategoriaSeleccionada.GlobalConfigKey, out int valorExcel))
                {
                    exitoNMax = _tiaService.SincronizarDimensionGlobal("000_Config_Dispositivos", CategoriaSeleccionada.PlcCountConstant, valorExcel);
                    CategoriaSeleccionada.EstadoNMax = exitoNMax ? EstadoSincronizacion.Ok : EstadoSincronizacion.Error;

                    if (!exitoNMax)
                    {
                        MessageBox.Show("Error al sincronizar N_MAX. Abortando.");
                        return;
                    }
                    _cacheNMaxPlc[CategoriaSeleccionada.Name] = valorExcel;
                    _nMaxTia = valorExcel;
                    ActualizarLabelNMax();
                }

                var listaParaSincronizar = _datosInyectados[CategoriaSeleccionada.Name].Cast<IDispositivo>().ToList();

                // 2. PASO B: CONSTANTES
                exitoConst = _tiaService.SincronizarConstantesConExcel(CategoriaSeleccionada.TiaGroup, CategoriaSeleccionada.TiaTable, listaParaSincronizar);
                CategoriaSeleccionada.EstadoConstantes = exitoConst ? EstadoSincronizacion.Ok : EstadoSincronizacion.Error;

                // 3. PASO C: COMPILACIÓN
                exitoComp = _tiaService.CompilarBloque(CategoriaSeleccionada.TiaDbName);

                // 4. PASO D: CIRUGÍA XML
                if (exitoComp)
                {
                    exitoDB = _tiaService.SincronizarComentariosDB(CategoriaSeleccionada.TiaDbName, CategoriaSeleccionada.TiaDbArrayName, listaParaSincronizar);
                    CategoriaSeleccionada.EstadoDB = exitoDB ? EstadoSincronizacion.Ok : EstadoSincronizacion.Error;
                }
                else
                {
                    CategoriaSeleccionada.EstadoDB = EstadoSincronizacion.Error;
                }

                EjecutarComparacion(mantenerEstadoDB: true);

                // RESUMEN FINAL
                if (exitoNMax && exitoConst && exitoComp && exitoDB)
                    MessageBox.Show("Sincronización total completada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                {
                    string msg = "Sincronización finalizada con errores:\n";
                    if (!exitoConst) msg += "- Error en constantes.\n";
                    if (!exitoComp) msg += "- Error en compilación.\n";
                    if (!exitoDB) msg += "- Error en cirugía DB.\n";
                    MessageBox.Show(msg, "Incompleto", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"CRITICAL SYNC ERROR: {ex.Message}", true);
                CategoriaSeleccionada.EstadoDB = EstadoSincronizacion.Error;
            }
        }

        // =================================================================================================================
        // --- PROCESO DE COMPARACIÓN ---

        private void EjecutarComparacion(bool mantenerEstadoDB = false)
        {
            if (CategoriaSeleccionada == null) return;

            try
            {
                CategoriaSeleccionada.EstadoNMax = EstadoSincronizacion.Pendiente;
                CategoriaSeleccionada.EstadoConstantes = EstadoSincronizacion.Pendiente;
                if (!mantenerEstadoDB) CategoriaSeleccionada.EstadoDB = EstadoSincronizacion.Pendiente;

                ActualizarLabelNMax();
                _datosGlobales.TryGetValue(CategoriaSeleccionada.GlobalConfigKey, out int nMaxExcel);
                CategoriaSeleccionada.EstadoNMax = (nMaxExcel == _nMaxTia) ? EstadoSincronizacion.Ok : EstadoSincronizacion.Error;

                string rutaTemp = Path.Combine(AppConfigManager.TempPath, "plc_export.xml");
                if (!_tiaService.ExportarTablaVariables(CategoriaSeleccionada.TiaGroup, CategoriaSeleccionada.TiaTable, rutaTemp))
                {
                    CategoriaSeleccionada.EstadoConstantes = EstadoSincronizacion.Error;
                    return;
                }

                var dicPlc = LeerDiccionarioDelPlc(rutaTemp);
                var listaExcel = _datosInyectados[CategoriaSeleccionada.Name].Cast<IDispositivo>().ToList();
                bool todasOk = true;

                foreach (var disp in listaExcel)
                {
                    if (dicPlc.TryGetValue(disp.Numero, out string tagPlc))
                    {
                        bool coincide = tagPlc == disp.CPTag;
                        disp.Estado = coincide ? "Sincronizado" : $"{tagPlc} -> {disp.CPTag}";
                        if (!coincide) todasOk = false;
                        dicPlc.Remove(disp.Numero);
                    }
                    else
                    {
                        disp.Estado = "Nuevo";
                        todasOk = false;
                    }
                }

                if (dicPlc.Count > 0) todasOk = false;
                CategoriaSeleccionada.EstadoConstantes = todasOk ? EstadoSincronizacion.Ok : EstadoSincronizacion.Error;
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR COMPARACIÓN: {ex.Message}", true);
                CategoriaSeleccionada.EstadoConstantes = EstadoSincronizacion.Error;
            }
        }

        private Dictionary<int, string> LeerDiccionarioDelPlc(string ruta)
        {
            var dic = new Dictionary<int, string>();
            XDocument doc = XDocument.Load(ruta);
            var constantes = doc.Descendants().Where(x => x.Name.LocalName == "PlcUserConstant");

            foreach (var con in constantes)
            {
                var attr = con.Element(con.Name.Namespace + "AttributeList");
                var name = attr?.Element(attr.Name.Namespace + "Name")?.Value;
                var val = attr?.Element(attr.Name.Namespace + "Value")?.Value;

                if (int.TryParse(val, out int id) && !string.IsNullOrEmpty(name))
                {
                    if (!dic.ContainsKey(id)) dic.Add(id, name);
                }
            }
            return dic;
        }
    }
}