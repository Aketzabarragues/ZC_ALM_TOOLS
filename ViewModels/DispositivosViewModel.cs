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
    /* * RESUMEN DE PASOS DEL DISPOSITIVOS VIEW MODEL:
     * 1. GESTIÓN DE VISTA: Controla qué datos se muestran en el DataGrid según la selección del usuario.
     * 2. COMUNICACIÓN CON TIA: Utiliza el TiaService para realizar operaciones de lectura/escritura en el PLC.
     * 3. COMPARACIÓN DINÁMICA: Cruza la información del Excel (XML local) con la del PLC (XML exportado).
     * 4. SINCRONIZACIÓN: Permite inyectar los datos del Excel directamente en las tablas de variables de TIA Portal.
     * 5. LOG CENTRALIZADO: Envía todos los hallazgos y errores al LogService unificado.
     */

    public class DispositivosViewModel : ObservableObject
    {
        // --- PROPIEDADES PRIVADAS ---
        private Dictionary<string, List<object>> _datosInyectados; // Almacén de datos por categoría
        private Dictionary<string, int> _datosGlobales;

        private Dictionary<string, int> _cacheNMaxPlc = new Dictionary<string, int>();

        private TiaService _tiaService;
        public Action<string, bool> StatusRequest { get; set; } // Delegado para mensajes en el Main

        // --- BINDINGS PARA LA INTERFAZ (UI) ---

        // Valor de constante N_MAX de dispositivo
        private string _nMaxInfo = "Dimensiones: Excel (-) | PLC (-)";
        public string NMaxInfo
        {
            get => _nMaxInfo;
            set { _nMaxInfo = value; OnPropertyChanged(); }
        }

        // Color de comparacion N_MAX
        private string _nMaxColor = "#E8F4FD";
        public string NMaxColor
        {
            get => _nMaxColor;
            set { _nMaxColor = value; OnPropertyChanged(); }
        }

        // Valores internos para construir el string
        private int _nMaxExcel = 0;
        private int _nMaxTia = 0;


        // Lista de objetos que se enlaza al ItemsSource del DataGrid
        private ObservableCollection<object> _listaDispositivos = new ObservableCollection<object>();
        public ObservableCollection<object> ListaDispositivos
        {
            get => _listaDispositivos;
            set { _listaDispositivos = value; OnPropertyChanged(); }
        }

        // Lista de categorías que llena el ComboBox principal
        private List<DeviceCategory> _categorias;
        public List<DeviceCategory> Categorias
        {
            get => _categorias;
            set { _categorias = value; OnPropertyChanged(); }
        }

        // Representa el objeto completo seleccionado en el ComboBox
        private DeviceCategory _categoriaSeleccionada;
        public DeviceCategory CategoriaSeleccionada
        {
            get => _categoriaSeleccionada;
            set
            {
                if (_categoriaSeleccionada == value) return;
                _categoriaSeleccionada = value;
                OnPropertyChanged();
                RefrescarVista(); // Actualiza el DataGrid al cambiar la selección
            }
        }

        // --- COMANDOS ---
        public RelayCommand SyncConstantesCommand { get; set; }
        public RelayCommand ComparacionCommand { get; set; }

        // --- CONSTRUCTOR ---
        public DispositivosViewModel()
        {
            ListaDispositivos = new ObservableCollection<object>();
            SyncConstantesCommand = new RelayCommand(EjecutarSyncConstantes);
            ComparacionCommand = new RelayCommand(EjecutarComparacion);
        }

        // --- MÉTODOS DE INICIALIZACIÓN ---

        public void SetTiaService(TiaService service) => _tiaService = service;

        // Recibe el diccionario de datos desde el MainViewModel tras la carga
        public void SetDatos(Dictionary<string, List<object>> datos, Dictionary<string, int> globales)
        {
            _datosInyectados = datos;
            _datosGlobales = globales;
            RefrescarVista();
        }

        // Actualiza la colección ListaDispositivos buscando en el diccionario la categoría activa
        private void RefrescarVista()
        {
            if (_datosInyectados == null || CategoriaSeleccionada == null) return;

            if (_datosInyectados.TryGetValue(CategoriaSeleccionada.Name, out var lista))
            {
                ListaDispositivos = new ObservableCollection<object>(lista);
                LogService.Write($"Cargada categoría: {CategoriaSeleccionada.Name} ({lista.Count} elementos)");

                // Actualizamos el numero maximo de constante dispositivo
                if (_datosGlobales != null && _datosGlobales.TryGetValue(CategoriaSeleccionada.GlobalConfigKey, out int valor))
                {
                    _nMaxExcel = valor;
                    if (_cacheNMaxPlc.TryGetValue(CategoriaSeleccionada.Name, out int valorGuardadoPlc))
                    {
                        _nMaxTia = valorGuardadoPlc;
                    }
                    else
                    {
                        _nMaxTia = 0; // Solo aquí ponemos 0 si nunca se ha pulsado comparar
                    }
                    ActualizarLabelNMax();
                }
            }


        }

        // Actualizar el numero maximo de dispositivo, para mostrar en la UI
        private void ActualizarLabelNMax()
        {
            string plcText = _nMaxTia > 0 ? _nMaxTia.ToString() : "-";
            string aviso = "";

            // LÓGICA DE COLOR Y AVISO
            if (_nMaxTia == 0)
            {
                NMaxColor = "#F5F5F5"; // Gris (no comparado aún)
                aviso = "";
            }
            else if (_nMaxTia != _nMaxExcel)
            {
                NMaxColor = "#FFF3E0"; // Naranja suave (alerta de desincronización)
                aviso = "  ⚠️ REQUIERE SYNC";
            }
            else
            {
                NMaxColor = "#E8F5E9"; // Verde suave (sincronizado)
                aviso = "  ✅ OK";
            }

            NMaxInfo = $"Dimensiones ({CategoriaSeleccionada?.PlcCountConstant}): Excel ({_nMaxExcel}) | PLC ({plcText}){aviso}";
        }

        // --- LÓGICA DE NEGOCIO (TIA PORTAL) ---

        // PASO: Sincronización (Escribir del Excel al PLC)
        private void EjecutarSyncConstantes()
        {
            if (CategoriaSeleccionada == null) return;

            // Pregunta de seguridad extra
            var resultado = MessageBox.Show(
                "¿Deseas sincronizar? Los elementos que existan en el PLC pero NO en el Excel serán eliminados para que ambas listas sean idénticas.",
                "Confirmar Sincronización Total",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (resultado != MessageBoxResult.Yes) return;

            try
            {
                LogService.Write($"--- INICIO SINCRONIZACIÓN TOTAL: {CategoriaSeleccionada.Name} ---");

                
                // SINCRONIZAR DIMENSIÓN GLOBAL (N_MAX)
                if (!string.IsNullOrEmpty(CategoriaSeleccionada.GlobalConfigKey) &&
                    !string.IsNullOrEmpty(CategoriaSeleccionada.PlcCountConstant))
                {
                    // Buscamos el valor en nuestro diccionario cargado del XML global
                    if (_datosGlobales != null && _datosGlobales.TryGetValue(CategoriaSeleccionada.GlobalConfigKey, out int valorExcel))
                    {
                        LogService.Write($"[PASO A] Ajustando constante de dimensión: {CategoriaSeleccionada.PlcCountConstant} -> {valorExcel}");

                        // Escribimos en la tabla 000_Config_Dispositivos del PLC
                        _tiaService.SincronizarDimensionGlobal("000_Config_Dispositivos", CategoriaSeleccionada.PlcCountConstant, valorExcel);

                        _nMaxTia = valorExcel;
                        _cacheNMaxPlc[CategoriaSeleccionada.Name] = valorExcel;
                        ActualizarLabelNMax();
                    }
                }

                // FILTRADO CRÍTICO: 
                // Solo mandamos al PLC los objetos que NO sean "fantasmas" (es decir, los que están en el Excel)
                // Usamos el diccionario original de datos inyectados para estar seguros.
                if (_datosInyectados.TryGetValue(CategoriaSeleccionada.Name, out var listaObjetos))
                {
                    var listaParaSincronizar = listaObjetos.Cast<IDispositivo>().ToList();

                    _tiaService.SincronizarConstantesConExcel(
                        CategoriaSeleccionada.TiaGroup,
                        CategoriaSeleccionada.TiaTable,
                        listaParaSincronizar);

                    LogService.Write("Sincronización finalizada. PLC y Excel son ahora idénticos.");

                    // Volvemos a comparar para limpiar los mensajes de la pantalla
                    EjecutarComparacion();

                    MessageBox.Show("Sincronización total completada.");
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR CRÍTICO EN SYNC: {ex.Message}", true);
                MessageBox.Show("Error durante la sincronización.");
            }
        }






        // PASO: Comparación (Leer PLC y cruzar con Excel)
        private void EjecutarComparacion()
        {
            if (CategoriaSeleccionada == null) return;

            // 1. IMPORTANTE: Reseteamos la lista a los datos del Excel para limpiar "fantasmas" de comparaciones previas
            RefrescarVista();

            LogService.Write($"--- INICIO COMPARACIÓN: {CategoriaSeleccionada.Name} ---");



            bool dimensionCorrecta = true;

            try
            {

                if (!string.IsNullOrEmpty(CategoriaSeleccionada.PlcCountConstant))
                {
                    LogService.Write($"[PASO 0] Consultando {CategoriaSeleccionada.PlcCountConstant} en el PLC...");

                    // Usamos el servicio para obtener el valor real de la tabla de configuración
                    _nMaxTia = _tiaService.ObtenerValorConstante("000_Config_Dispositivos", CategoriaSeleccionada.PlcCountConstant);


                    _cacheNMaxPlc[CategoriaSeleccionada.Name] = _nMaxTia;

                    // Refrescamos el texto de la UI (Excel vs PLC)
                    ActualizarLabelNMax();

                    // Comparamos los dos valores
                    if (_nMaxExcel != _nMaxTia)
                    {
                        dimensionCorrecta = false;
                        LogService.Write($"[DIFERENCIA] La dimensión global no coincide: Excel({_nMaxExcel}) != PLC({_nMaxTia})", true);
                    }
                }


                string rutaXml = Path.Combine(AppConfigManager.BasePath, "temp", "check_comp.xml");
                LogService.Write($"[PASO 1] Exportando XML desde TIA Portal...");
                _tiaService.ExportarTablaVariables(CategoriaSeleccionada.TiaGroup, CategoriaSeleccionada.TiaTable, rutaXml);

                LogService.Write($"[PASO 2] Procesando constantes del PLC...");
                var dicPlc = LeerDiccionarioDelPlc(rutaXml);

                
                // Usaremos un HashSet para saber qué IDs del PLC ya hemos procesado
                HashSet<int> idsProcesados = new HashSet<int>();
                int ok = 0, cambios = 0, nuevos = 0, sobrantes = 0;

                // PARTE A: De Excel a PLC (Lo que ya tenías)
                LogService.Write($"[PASO 3] Buscando diferencias y nuevos dispositivos...");
                foreach (var item in ListaDispositivos)
                {
                    if (item is IDispositivo d)
                    {
                        idsProcesados.Add(d.Numero); // Marcamos este ID como "visto" en el Excel

                        if (dicPlc.TryGetValue(d.Numero, out string tagEnPlc))
                        {
                            if (tagEnPlc == d.CPTag)
                            {
                                d.Estado = "Sincronizado";
                                ok++;
                            }
                            else
                            {
                                d.Estado = $"CAMBIO: {tagEnPlc} -> {d.CPTag}";
                                LogService.Write($"[DIFERENCIA] ID {d.Numero}: PLC='{tagEnPlc}', Excel='{d.CPTag}'");
                                cambios++;
                            }
                        }
                        else
                        {
                            d.Estado = "NUEVO";
                            LogService.Write($"[NUEVO] ID {d.Numero}: No existe en PLC.");
                            nuevos++;
                        }
                    }
                }

                // PARTE B: De PLC a Excel (Detectar lo que sobra en el PLC)
                LogService.Write($"[PASO 4] Buscando dispositivos sobrantes en el PLC...");

                // Preparamos la reflexión para crear objetos del tipo correcto (Disp_V, Disp_ED...)
                string nombreCompletoClase = $"ZC_ALM_TOOLS.Models.{CategoriaSeleccionada.ModelClass}";
                Type tipoClase = Type.GetType(nombreCompletoClase);

                foreach (var kvp in dicPlc)
                {
                    // Si el ID del PLC no ha sido procesado, es que no está en el Excel
                    if (!idsProcesados.Contains(kvp.Key))
                    {
                        sobrantes++;
                        LogService.Write($"[SOBRANTE] ID {kvp.Key}: '{kvp.Value}' está en el PLC pero NO en el Excel.");

                        // Creamos un "Fantasma" para mostrarlo en la tabla de la interfaz
                        if (tipoClase != null)
                        {
                            var ghost = Activator.CreateInstance(tipoClase);
                            if (ghost is IDispositivo dGhost)
                            {
                                dGhost.Numero = kvp.Key;
                                dGhost.CPTag = kvp.Value;
                                dGhost.Estado = "ELIMINAR / NO EN EXCEL";

                                // Lo añadimos a la lista observable para que el usuario lo vea en el DataGrid
                                ListaDispositivos.Add(ghost);
                            }
                        }
                    }
                }

                LogService.Write($"--- COMPARACIÓN FINALIZADA | OK: {ok}, Cambios: {cambios}, Nuevos: {nuevos}, Sobrantes: {sobrantes} ---");

                if (cambios > 0 || nuevos > 0 || sobrantes > 0 || !dimensionCorrecta)
                {
                    string motivo = !dimensionCorrecta ? "La dimensión global (N_MAX) no coincide. " : "";
                    motivo += sobrantes > 0 ? $"Hay {sobrantes} elementos de más en el PLC." : "Existen diferencias en los datos.";                    
                    MessageBox.Show(motivo, "Atención: Diferencias detectadas", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show("Todo está perfectamente sincronizado (Dimensiones y Dispositivos).", "Resultado OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }

            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR EN COMPARACIÓN: {ex.Message}", true);
                MessageBox.Show("Error durante la comparación.");
            }
        }

        // Helper: Lee el XML exportado por TIA y extrae los nodos 'PlcUserConstant'
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