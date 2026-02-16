using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using Siemens.Engineering.SW.Blocks;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{

    /// <summary>
    /// ViewModel encargado de la lógica de visualización, comparación y sincronización de dispositivos.
    /// Gestiona la interacción entre los datos extraídos del Excel y el proyecto de TIA Portal.
    /// </summary>
    public class DispositivosViewModel : ObservableObject
    {


        // =================================================================================================================
        // --- PROPIEDADES PRIVADAS ---

        /// <summary>Diccionario que almacena las listas de objetos de cada categoría (Key: Nombre categoría).</summary>
        private Dictionary<string, List<object>> _datosInyectados;

        /// <summary>Diccionario con parámetros de configuración global (ej. N_MAX_V).</summary>
        private Dictionary<string, int> _datosGlobales;

        /// <summary>Caché local para almacenar el valor de N_MAX leído del PLC y evitar lecturas constantes.</summary>
        private Dictionary<string, int> _cacheNMaxPlc = new Dictionary<string, int>();

        /// <summary>Servicio de comunicación con la API de Siemens Openness.</summary>
        private TiaService _tiaService;

        /// <summary>Valor de la constante de dimensionado actual en el PLC.</summary>
        private int _nMaxTia = 0;




        // =================================================================================================================
        // --- BINDINGS PARA LA UI ---

        /// <summary>Delegado para solicitar actualizaciones de estado a la ventana principal.</summary>
        public Action<string, bool> StatusRequest { get; set; }

        private string _nMaxInfo;
        /// <summary>Texto informativo sobre el estado del dimensionado (Excel vs PLC).</summary>
        public string NMaxInfo { get => _nMaxInfo; set { _nMaxInfo = value; OnPropertyChanged(); } }

        private string _nMaxColor = "Transparent";
        /// <summary>Color de fondo para el indicador de dimensionado (Verde para OK, Rojo para error).</summary>
        public string NMaxColor { get => _nMaxColor; set { _nMaxColor = value; OnPropertyChanged(); } }

        /// <summary>Colección de dispositivos que se muestra en el DataGrid de la vista.</summary>
        public ObservableCollection<object> ListaDispositivos { get; set; } = new ObservableCollection<object>();

        /// <summary>Lista de todas las categorías de dispositivos configuradas.</summary>
        public List<DeviceCategory> Categorias { get; set; }

        private DeviceCategory _categoriaSeleccionada;
        /// <summary>Categoría de dispositivo actualmente seleccionada en el ComboBox.</summary>
        public DeviceCategory CategoriaSeleccionada
        {
            get => _categoriaSeleccionada;
            set { _categoriaSeleccionada = value; OnPropertyChanged(); RefrescarVista(); }
        }




        // =================================================================================================================
        // --- COMANDOS ---

        /// <summary>Comando que dispara el proceso de sincronización total hacia el PLC.</summary>
        public RelayCommand SyncConstantesCommand { get; set; }

        /// <summary>Comando que dispara el proceso de comparación entre Excel y PLC.</summary>
        public RelayCommand ComparacionCommand { get; set; }

        /// <summary>
        /// Constructor de <see cref="DispositivosViewModel"/>. Inicializa los comandos de la vista.
        /// </summary>
        public DispositivosViewModel()
        {
            SyncConstantesCommand = new RelayCommand(EjecutarSyncConstantes);
            ComparacionCommand = new RelayCommand(EjecutarComparacion);
        }




        // =================================================================================================================

        /// <summary>
        /// Establece la instancia del servicio TIA para la comunicación con el PLC.
        /// </summary>
        /// <param name="service">Instancia de <see cref="TiaService"/>.</param>
        public void SetTiaService(TiaService service) => _tiaService = service;




        // =================================================================================================================

        /// <summary>
        /// Carga los datos procedentes de la extracción del Excel en el ViewModel.
        /// </summary>
        /// <param name="datos">Diccionario de listas de dispositivos por categoría.</param>
        /// <param name="globales">Diccionario de parámetros de configuración global.</param>
        public void SetDatos(Dictionary<string, List<object>> datos, Dictionary<string, int> globales)
        {
            _datosInyectados = datos;
            _datosGlobales = globales;
            RefrescarVista();
        }




        // =================================================================================================================

        /// <summary>
        /// Actualiza la lista visual y la información de dimensionado al cambiar de categoría.
        /// </summary>
        private void RefrescarVista()
        {
            if (CategoriaSeleccionada == null || _datosInyectados == null) return;

            LogService.Write($"Cargada categoría: {CategoriaSeleccionada.Name} ({_datosInyectados[CategoriaSeleccionada.Name].Count} elementos)");

            ListaDispositivos.Clear();
            foreach (var item in _datosInyectados[CategoriaSeleccionada.Name])
                ListaDispositivos.Add(item);

            ActualizarLabelNMax();
        }




        // =================================================================================================================

        /// <summary>
        /// Compara el valor N_MAX del Excel con el del PLC y actualiza visualmente el indicador de estado.
        /// </summary>
        private void ActualizarLabelNMax()
        {
            if (CategoriaSeleccionada == null || _datosGlobales == null) return;

            int nMaxExcel = 0;
            if (_datosGlobales.TryGetValue(CategoriaSeleccionada.GlobalConfigKey, out int val))
                nMaxExcel = val;

            // Intentamos obtener el valor del PLC desde la caché o consultando al servicio
            if (!_cacheNMaxPlc.TryGetValue(CategoriaSeleccionada.Name, out _nMaxTia))
            {
                LogService.Write($"[PASO 0] Consultando {CategoriaSeleccionada.PlcCountConstant} en el PLC...");
                _nMaxTia = _tiaService.ObtenerValorConstante("000_Config_Dispositivos", CategoriaSeleccionada.PlcCountConstant);
                _cacheNMaxPlc[CategoriaSeleccionada.Name] = _nMaxTia;
            }

            NMaxInfo = $"Dimensión: Excel ({nMaxExcel}) | PLC ({_nMaxTia})";
            NMaxColor = (nMaxExcel == _nMaxTia) ? "#A5D6A7" : "#EF9A9A"; // Verde vs Rojo suave

            if (nMaxExcel != _nMaxTia)
                LogService.Write($"[DIFERENCIA] La dimensión global no coincide: Excel({nMaxExcel}) != PLC({_nMaxTia})", true);
        }




        // =================================================================================================================

        /// <summary>
        /// Ejecuta el ciclo completo de sincronización hacia TIA Portal:
        /// 1. Sincroniza dimensionado (N_MAX).
        /// 2. Sincroniza constantes en la Tabla de Variables.
        /// 3. Compila el bloque de datos (DB).
        /// 4. Inyecta comentarios mediante cirugía XML.
        /// </summary>
        private void EjecutarSyncConstantes()
        {
            if (CategoriaSeleccionada == null) return;

            var resultado = MessageBox.Show(
                "¿Deseas sincronizar? Se actualizarán dimensiones, constantes y comentarios del DB para que coincidan con el Excel.",
                "Confirmar Sincronización Total",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (resultado != MessageBoxResult.Yes) return;

            try
            {
                LogService.Write($"--- INICIO SINCRONIZACIÓN TOTAL: {CategoriaSeleccionada.Name} ---");

                // 1. PASO A: SINCRONIZAR DIMENSIÓN GLOBAL (N_MAX)
                if (!string.IsNullOrEmpty(CategoriaSeleccionada.GlobalConfigKey) && !string.IsNullOrEmpty(CategoriaSeleccionada.PlcCountConstant))
                {
                    if (_datosGlobales != null && _datosGlobales.TryGetValue(CategoriaSeleccionada.GlobalConfigKey, out int valorExcel))
                    {
                        LogService.Write($"[PASO A] Ajustando constante de dimensión: {CategoriaSeleccionada.PlcCountConstant} -> {valorExcel}");
                        _tiaService.SincronizarDimensionGlobal("000_Config_Dispositivos", CategoriaSeleccionada.PlcCountConstant, valorExcel);

                        _nMaxTia = valorExcel;
                        _cacheNMaxPlc[CategoriaSeleccionada.Name] = valorExcel;
                        ActualizarLabelNMax();
                    }
                }

                if (_datosInyectados.TryGetValue(CategoriaSeleccionada.Name, out var listaObjetos))
                {
                    var listaParaSincronizar = listaObjetos.Cast<IDispositivo>().ToList();

                    // 2. SINCRONIZAR CONSTANTES DE VARIABLES (IDs y Tags)
                    _tiaService.SincronizarConstantesConExcel(CategoriaSeleccionada.TiaGroup, CategoriaSeleccionada.TiaTable, listaParaSincronizar);

                    // 3. PASO B: COMPILAR (Obligatorio para que el DB cambie de tamaño antes de exportar)
                    _tiaService.CompilarBloque(CategoriaSeleccionada.TiaDbName);

                    // 4. PASO C: SINCRONIZAR COMENTARIOS DEL DB (La cirugía XML)
                    if (!string.IsNullOrEmpty(CategoriaSeleccionada.TiaDbName) && !string.IsNullOrEmpty(CategoriaSeleccionada.TiaDbArrayName))
                    {
                        _tiaService.SincronizarComentariosDB(CategoriaSeleccionada.TiaDbName, CategoriaSeleccionada.TiaDbArrayName, listaParaSincronizar);
                    }

                    LogService.Write("Sincronización TOTAL finalizada. PLC y Excel son ahora idénticos.");
                    EjecutarComparacion();
                    MessageBox.Show("Sincronización total completada con éxito.", "Sincronización OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR CRÍTICO EN SYNC: {ex.Message}", true);
                MessageBox.Show($"Error durante la sincronización: {ex.Message}", "Error Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }




        // =================================================================================================================

        /// <summary>
        /// Lee la configuración actual del PLC mediante exportación XML y la compara con los datos del Excel.
        /// Marca cada dispositivo como 'Sincronizado', 'Diferente' o 'Nuevo'.
        /// </summary>
        private void EjecutarComparacion()
        {
            if (CategoriaSeleccionada == null) return;

            try
            {
                LogService.Write($"--- INICIO COMPARACIÓN: {CategoriaSeleccionada.Name} ---");
                ActualizarLabelNMax();

                // 1. Exportamos la tabla de variables del PLC a XML
                string rutaTempPlc = Path.Combine(AppConfigManager.TempPath, "plc_export.xml");
                LogService.Write("[PASO 1] Exportando XML desde TIA Portal...");
                _tiaService.ExportarTablaVariables(CategoriaSeleccionada.TiaGroup, CategoriaSeleccionada.TiaTable, rutaTempPlc);

                // 2. Leemos el PLC y creamos un diccionario ID -> Tag
                LogService.Write("[PASO 2] Procesando constantes del PLC...");
                var diccionarioPlc = LeerDiccionarioDelPlc(rutaTempPlc);

                // 3. Comparamos con los datos inyectados del Excel
                LogService.Write("[PASO 3] Buscando diferencias y nuevos dispositivos...");
                var listaExcel = _datosInyectados[CategoriaSeleccionada.Name].Cast<IDispositivo>().ToList();

                foreach (var disp in listaExcel)
                {
                    if (diccionarioPlc.TryGetValue(disp.Numero, out string tagPlc))
                    {
                        disp.Estado = (tagPlc == disp.CPTag) ? "Sincronizado" : "Diferente";
                        diccionarioPlc.Remove(disp.Numero); // Lo quitamos para saber qué queda "sobrante"
                    }
                    else
                    {
                        disp.Estado = "Nuevo";
                    }
                }

                // 4. Los que quedan en el diccionario están en el PLC pero NO en el Excel
                LogService.Write("[PASO 4] Buscando dispositivos sobrantes en el PLC...");
                foreach (var sobrante in diccionarioPlc)
                {
                    LogService.Write($"[SOBRANTE] ID {sobrante.Key}: '{sobrante.Value}' está en el PLC pero NO en el Excel.");
                }

                LogService.Write($"--- COMPARACIÓN FINALIZADA | OK: {listaExcel.Count(x => x.Estado == "Sincronizado")}, Cambios: {listaExcel.Count(x => x.Estado != "Sincronizado")} ---");
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR EN COMPARACIÓN: {ex.Message}", true);
                MessageBox.Show("Error durante la comparación.");
            }
        }




        // =================================================================================================================

        /// <summary>
        /// Parsea un archivo XML de exportación de TIA Portal para extraer las constantes de usuario.
        /// </summary>
        /// <param name="ruta">Ruta al archivo XML exportado.</param>
        /// <returns>Diccionario donde la Key es el Valor de la constante (ID) y el Value es el Nombre (Tag).</returns>
        private Dictionary<int, string> LeerDiccionarioDelPlc(string ruta)
        {
            var diccionario = new Dictionary<int, string>();
            XDocument doc = XDocument.Load(ruta);

            // Buscamos todas las constantes de usuario independientemente del namespace
            var constantes = doc.Descendants().Where(x => x.Name.LocalName.Contains("PlcUserConstant")).ToList();

            foreach (var con in constantes)
            {
                var attrList = con.Elements().FirstOrDefault(x => x.Name.LocalName == "AttributeList");
                if (attrList != null)
                {
                    var nNode = attrList.Elements().FirstOrDefault(x => x.Name.LocalName == "Name");
                    var vNode = attrList.Elements().FirstOrDefault(x => x.Name.LocalName == "Value");

                    if (nNode != null && vNode != null)
                    {
                        if (int.TryParse(vNode.Value, out int id))
                        {
                            if (!diccionario.ContainsKey(id))
                                diccionario.Add(id, nNode.Value);
                        }
                    }
                }
            }
            return diccionario;
        }
    }
}