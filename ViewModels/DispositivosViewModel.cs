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
        private TiaService _tiaService;
        public Action<string, bool> StatusRequest { get; set; } // Delegado para mensajes en el Main

        // --- BINDINGS PARA LA INTERFAZ (UI) ---

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
        public void SetDatos(Dictionary<string, List<object>> datos)
        {
            _datosInyectados = datos;
            RefrescarVista();
        }

        // Actualiza la colección ListaDispositivos buscando en el diccionario la categoría activa
        private void RefrescarVista()
        {
            if (_datosInyectados == null || CategoriaSeleccionada == null) return;

            if (_datosInyectados.TryGetValue(CategoriaSeleccionada.Name, out var lista))
            {
                // Creamos una nueva instancia para disparar una sola notificación de cambio a la UI
                ListaDispositivos = new ObservableCollection<object>(lista);
                LogService.Write($"Cargada categoría: {CategoriaSeleccionada.Name} ({lista.Count} elementos)");
            }
        }

        // --- LÓGICA DE NEGOCIO (TIA PORTAL) ---

        // PASO: Sincronización (Escribir del Excel al PLC)
        private void EjecutarSyncConstantes()
        {
            if (ListaDispositivos == null || CategoriaSeleccionada == null) return;

            LogService.Write($"--- INICIO SINCRONIZACIÓN PLC: {CategoriaSeleccionada.Name} ---");

            try
            {
                string rutaXml = Path.Combine(AppConfigManager.BasePath, "temp", "sync_tmp.xml");

                // Exportación previa para asegurar que la tabla existe y está accesible
                LogService.Write($"[1/2] Exportando tabla {CategoriaSeleccionada.TiaTable}...");
                _tiaService.ExportarTablaVariables(
                    CategoriaSeleccionada.TiaGroup,
                    CategoriaSeleccionada.TiaTable,
                    rutaXml);

                // Inyección masiva de constantes usando la interfaz IDispositivo
                LogService.Write($"[2/2] Sincronizando datos con TIA Portal...");
                _tiaService.SincronizarConstantesConExcel(
                    CategoriaSeleccionada.TiaGroup,
                    CategoriaSeleccionada.TiaTable,
                    ListaDispositivos.Cast<IDispositivo>().ToList());

                LogService.Write($"Sincronización finalizada correctamente.");
                MessageBox.Show($"PLC actualizado correctamente.", "Éxito");
            }
            catch (Exception ex)
            {
                LogService.Write($"ERROR EN SYNC: {ex.Message}", true);
                MessageBox.Show($"Error en la sincronización.");
            }
        }

        // PASO: Comparación (Leer PLC y cruzar con Excel)
        private void EjecutarComparacion()
        {
            if (CategoriaSeleccionada == null) return;

            LogService.Write($"--- INICIO COMPARACIÓN: {CategoriaSeleccionada.Name} ---");

            try
            {
                // 1. Exportación XML desde TIA Portal (Uso de rutas dinámicas del config)
                string rutaXml = Path.Combine(AppConfigManager.BasePath, "temp", "check_comp.xml");
                LogService.Write($"[PASO 1] Exportando XML desde TIA Portal...");

                _tiaService.ExportarTablaVariables(
                    CategoriaSeleccionada.TiaGroup,
                    CategoriaSeleccionada.TiaTable,
                    rutaXml);

                // 2. Parseo del XML del PLC para crear un diccionario de búsqueda rápida
                LogService.Write($"[PASO 2] Procesando constantes del PLC...");
                var dicPlc = LeerDiccionarioDelPlc(rutaXml);
                LogService.Write($"Detectadas {dicPlc.Count} constantes en el PLC.");

                // 3. Cruce lógico de datos
                LogService.Write($"[PASO 3] Comparando con Excel...");
                int ok = 0, cambios = 0, nuevos = 0;

                foreach (var item in ListaDispositivos)
                {
                    if (item is IDispositivo d)
                    {
                        // Buscamos si el ID del Excel existe en el diccionario del PLC
                        if (dicPlc.TryGetValue(d.Numero, out string tagEnPlc))
                        {
                            if (tagEnPlc == d.CPTag)
                            {
                                d.Estado = "Sincronizado";
                                ok++;
                            }
                            else
                            {
                                // Si los nombres no coinciden, marcamos para renombrar
                                d.Estado = $"CAMBIO: {tagEnPlc} -> {d.CPTag}";
                                LogService.Write($"[DIFERENCIA] ID {d.Numero}: PLC='{tagEnPlc}', Excel='{d.CPTag}'");
                                cambios++;
                            }
                        }
                        else
                        {
                            // Si el ID no existe en el PLC, es un dispositivo nuevo
                            d.Estado = "NUEVO";
                            LogService.Write($"[NUEVO] ID {d.Numero}: No existe en PLC.");
                            nuevos++;
                        }
                    }
                }

                LogService.Write($"--- COMPARACIÓN FINALIZADA | OK: {ok}, Cambios: {cambios}, Nuevos: {nuevos} ---");

                if (cambios > 0 || nuevos > 0)
                    MessageBox.Show($"Comparación terminada con diferencias. Revisa el log.", "Aviso");
                else
                    MessageBox.Show($"Todo está sincronizado.", "Resultado OK");

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