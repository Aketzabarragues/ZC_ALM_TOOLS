using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Xml.Linq;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models; // Importante para DispositivoModel

namespace ZC_ALM_TOOLS.ViewModels
{
    public class DispositivosViewModel : ObservableObject
    {
        // --- PROPIEDADES ---
        // Almacén local de lo que el Main nos inyectó
        private Dictionary<string, List<object>> _datosInyectados;
        private TiaService _tiaService;
        // Delegado para pedirle al Main que actualice la barra de estado
        public Action<string, bool> StatusRequest { get; set; }

        // Método para que el Main le pase el servicio
        public void SetTiaService(TiaService service) => _tiaService = service;

        // Lista tipada con tu modelo
        private ObservableCollection<object> _listaDispositivos = new ObservableCollection<object>();
        public ObservableCollection<object> ListaDispositivos
        {
            get => _listaDispositivos;
            set { _listaDispositivos = value; OnPropertyChanged(); }
        }

        private string _familiaSeleccionada;

        public ObservableCollection<string> FamiliasDispositivos { get; }

        public string FamiliaSeleccionada
        {
            get => _familiaSeleccionada;
            set
            {
                _familiaSeleccionada = value;
                OnPropertyChanged();
                RefrescarVista(); // Al cambiar el ComboBox, refrescamos la tabla
            }
        }

        // --- COMANDOS ---
        public RelayCommand SyncConstantesCommand { get; set; }
        public RelayCommand ComparacionCommand { get; set; }



        // EL NUEVO MÉTODO DE ENTRADA DE DATOS
        public void SetDatos(Dictionary<string, List<object>> datos)
        {
            _datosInyectados = datos;
            RefrescarVista();
        }


        private void RefrescarVista()
        {
            if (_datosInyectados == null || string.IsNullOrEmpty(FamiliaSeleccionada)) return;

            // Identificamos la clave según tu lista FamiliasDispositivos
            string clave = "disp_v";
            if (FamiliaSeleccionada.ToLower().Contains("disp_ed")) clave = "disp_ed";
            else if (FamiliaSeleccionada.ToLower().Contains("disp_ea")) clave = "disp_ea";

            if (_datosInyectados.ContainsKey(clave))
            {
                // Limpiamos y cargamos en la UI
                ListaDispositivos.Clear();
                foreach (var item in _datosInyectados[clave])
                {
                    ListaDispositivos.Add(item);
                }
            }
        }

        // --- CONSTRUCTOR ---
        public DispositivosViewModel()
        {
            // Inicializar datos
            FamiliasDispositivos = new ObservableCollection<string>
            {
                "Válvulas (disp_v)",
                "Entradas Digitales (disp_ed)",
                "Entradas Analógicas (disp_ea)"
                // Añade aquí Motores cuando tengas el json
            };

            // Selección por defecto
            FamiliaSeleccionada = "Válvulas (disp_v)";
            ListaDispositivos = new ObservableCollection<object>();

            // Configurar comandos (Binds a los botones)
            SyncConstantesCommand = new RelayCommand(EjecutarSyncConstantes);
            ComparacionCommand = new RelayCommand(EjecutarComparacion);
        }



        
        private void EjecutarSyncConstantes()
        {
            MessageBox.Show("TODO: Comparar N_MAX con TIA Portal");

            // 1. Verificación de seguridad: ¿Hay datos cargados?
            if (ListaDispositivos == null || ListaDispositivos.Count == 0)
            {
                MessageBox.Show("No hay datos cargados. Por favor, carga primero el archivo Excel de definición.",
                                "Aviso de Seguridad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_tiaService == null)
            {
                MessageBox.Show("El servicio TIA no está inicializado.");
                return;
            }

            try
            {
                // 1. Exportación previa para asegurar que tenemos los datos frescos del PLC
                string rutaXml = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS", "temp", "002_Disp_V.xml");
                _tiaService.ExportarTablaVariables("002_Dispositivos", "002_Disp_V", rutaXml);

                // 2. Sincronización Real
                // Usamos la lista de dispositivos que ya tenemos cargada en memoria desde el Excel
                _tiaService.SincronizarConstantesConExcel(
                    "002_Dispositivos",
                    "002_Disp_V",
                    ListaDispositivos.Cast<IDispositivo>().ToList()
                    );

                MessageBox.Show("Sincronización completada.\nLa tabla de TIA Portal ahora es idéntica al Excel.", "Sync OK");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en el proceso de sincronización: {ex.Message}");
            }

        }

        private void EjecutarComparacion()
        {
            StringBuilder log = new StringBuilder();
            log.AppendLine("=== REPORTE DE DEPURACIÓN DE COMPARACIÓN ===");
            log.AppendLine($"Fecha: {DateTime.Now}");
            log.AppendLine("-------------------------------------------");

            try
            {
                // 1. Exportación
                string rutaXml = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS", "temp", "check_comp.xml");
                log.AppendLine($"[PASO 1] Exportando XML a: {rutaXml}");
                _tiaService.ExportarTablaVariables("002_Dispositivos", "002_Disp_V", rutaXml);

                // 2. Lectura del XML (Pasamos el log para que el servicio también escriba en él)
                log.AppendLine("[PASO 2] Leyendo XML del PLC...");
                var dicPlc = LeerDiccionarioConLog(rutaXml, log);

                // 3. Comparación
                log.AppendLine($"[PASO 3] Comparando {ListaDispositivos.Count} elementos del Excel con {dicPlc.Count} del PLC...");

                foreach (var item in ListaDispositivos)
                {
                    if (item is IDispositivo d)
                    {
                        log.Append($"Fila Excel: ID={d.Numero}, PLC_Tag_Esperado='{d.CPTag}' -> ");

                        // 'tagEnPlc' es el string que viene del diccionario (nNode.Value del XML)
                        if (dicPlc.TryGetValue(d.Numero, out string tagEnPlc))
                        {
                            // Comparamos el string del PLC directamente contra el CPTag del Excel
                            if (tagEnPlc == d.CPTag)
                            {
                                d.Estado = "Sincronizado";
                                log.AppendLine("RESULTADO: OK.");
                            }
                            else
                            {
                                // Si el PLC tiene "VA-103" y el Excel CPTag tiene "VA_103", entrará aquí
                                d.Estado = $"RENOMBRAR: {tagEnPlc} -> {d.CPTag}";
                                log.AppendLine($"RESULTADO: Necesita renombre en PLC (PLC: {tagEnPlc}).");
                            }
                        }
                        else
                        {
                            d.Estado = "NUEVO";
                            log.AppendLine("RESULTADO: No existe.");
                        }
                    }
                }

                log.AppendLine("-------------------------------------------");
                log.AppendLine("=== FIN DEL REPORTE ===");

                // 4. Guardar y Abrir el archivo
                string rutaLog = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS", "log_comparacion.txt");
                File.WriteAllText(rutaLog, log.ToString());

                // Abrir el bloc de notas automáticamente
                Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", rutaLog);

                MessageBox.Show("Comparación finalizada. Revisa el informe que se ha abierto en el Bloc de Notas.");
            }
            catch (Exception ex)
            {
                log.AppendLine($"\nERROR CRÍTICO: {ex.Message}");
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "error_log.txt"), log.ToString());
                MessageBox.Show("Error durante la comparación. Revisa el log de error.");
            }
        }


        private Dictionary<int, string> LeerDiccionarioConLog(string ruta, StringBuilder log)
        {
            var diccionario = new Dictionary<int, string>();
            XDocument doc = XDocument.Load(ruta);

            var constantes = doc.Descendants().Where(x => x.Name.LocalName.Contains("PlcUserConstant")).ToList();
            log.AppendLine($"   - Nodos 'PlcUserConstant' encontrados en XML: {constantes.Count}");

            foreach (var con in constantes)
            {
                // 1. EXTRAER TAG Y VALOR (Como ya hacíamos)
                var attrList = con.Elements().FirstOrDefault(x => x.Name.LocalName == "AttributeList");
                if (attrList != null)
                {
                    var nNode = attrList.Elements().FirstOrDefault(x => x.Name.LocalName == "Name");
                    var vNode = attrList.Elements().FirstOrDefault(x => x.Name.LocalName == "Value");

                    if (nNode != null && vNode != null)
                    {
                        int val = int.Parse(vNode.Value);
                        if (!diccionario.ContainsKey(val))
                            diccionario.Add(val, nNode.Value);

                        // 2. EXTRAER TODOS LOS COMENTARIOS (MultilingualTextItem)
                        string todosLosComentarios = "";

                        // Buscamos la sección "Comment" dentro de la constante actual
                        var seccionComentario = con.Descendants()
                            .FirstOrDefault(x => x.Name.LocalName == "MultilingualText" &&
                                                 x.Attribute("CompositionName")?.Value == "Comment");

                        if (seccionComentario != null)
                        {
                            // Hacemos un bucle por cada idioma (MultilingualTextItem) que exista
                            var itemsIdioma = seccionComentario.Descendants().Where(x => x.Name.LocalName == "MultilingualTextItem");

                            foreach (var item in itemsIdioma)
                            {
                                var itemAttrs = item.Elements().FirstOrDefault(x => x.Name.LocalName == "AttributeList");
                                if (itemAttrs != null)
                                {
                                    string cultura = itemAttrs.Elements().FirstOrDefault(x => x.Name.LocalName == "Culture")?.Value;
                                    string texto = itemAttrs.Elements().FirstOrDefault(x => x.Name.LocalName == "Text")?.Value;

                                    // Si el texto no está vacío, lo añadimos a nuestra cadena de log
                                    if (!string.IsNullOrWhiteSpace(texto))
                                    {
                                        todosLosComentarios += $"[{cultura}: {texto}] ";
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(todosLosComentarios)) todosLosComentarios = "Vacio";

                        // 3. LOG FINAL (Ahora con comentarios)
                        log.AppendLine($"   - Leído del PLC: ID={val}, Tag='{nNode.Value}', Comentarios: {todosLosComentarios}");
                    }
                }
            }
            log.AppendLine($"   - Total diccionario PLC cargado: {diccionario.Count} elementos.");
            return diccionario;
        }


    }
}