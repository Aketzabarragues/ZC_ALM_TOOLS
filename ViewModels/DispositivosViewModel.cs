using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Models; // Importante para DispositivoModel

namespace ZC_ALM_TOOLS.ViewModels
{
    public class DispositivosViewModel : ObservableObject
    {
        // --- PROPIEDADES ---

        // Lista tipada con tu modelo
        public ObservableCollection<DispositivoModel> ListaDispositivos { get; set; }

        public ObservableCollection<string> FamiliasDispositivos { get; set; }

        // Guardamos la ruta temporal en memoria por si cambiamos de familia en el combo
        private string _rutaTempCache;

        private string _familiaSeleccionada;
        public string FamiliaSeleccionada
        {
            get { return _familiaSeleccionada; }
            set
            {
                _familiaSeleccionada = value;
                OnPropertyChanged();

                // Si ya tenemos datos cargados y cambiamos el combo, recargamos automáticamente
                if (!string.IsNullOrEmpty(_rutaTempCache))
                {
                    CargarDatosDesdeJson(_rutaTempCache);
                }
            }
        }

        // --- COMANDOS ---
        public RelayCommand SyncConstantesCommand { get; set; }
        public RelayCommand RefactoringCommand { get; set; }
        public RelayCommand InyectarDatosCommand { get; set; }

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

            ListaDispositivos = new ObservableCollection<DispositivoModel>();

            // Configurar comandos (Binds a los botones)
            SyncConstantesCommand = new RelayCommand(EjecutarSyncConstantes);
            RefactoringCommand = new RelayCommand(EjecutarRefactoring);
            InyectarDatosCommand = new RelayCommand(EjecutarInyeccion);
        }

        // --- MÉTODOS DE CARGA DE DATOS ---

        public void CargarDatosDesdeJson(string carpetaTemp)
        {
            _rutaTempCache = carpetaTemp; // Guardamos la ruta para futuros refrescos

            // 1. Determinar qué archivo abrir según el ComboBox
            string archivoJson = "";

            if (FamiliaSeleccionada.Contains("disp_v")) archivoJson = "disp_v.json";
            else if (FamiliaSeleccionada.Contains("disp_ed")) archivoJson = "disp_ed.json";
            else if (FamiliaSeleccionada.Contains("disp_ea")) archivoJson = "disp_ea.json";
            // else if ...

            if (string.IsNullOrEmpty(archivoJson)) return;

            string rutaCompleta = Path.Combine(carpetaTemp, archivoJson);

            if (File.Exists(rutaCompleta))
            {
                try
                {
                    string contenido = File.ReadAllText(rutaCompleta);

                    // 2. Usar TU helper manual sin dependencias
                    var datosParseados = JsonParserHelper.ParsearDispositivos(contenido);

                    // 3. Actualizar la UI (Dispatcher asegura que se hace en el hilo visual)
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ListaDispositivos.Clear();
                        foreach (var item in datosParseados)
                        {
                            ListaDispositivos.Add(item);
                        }
                    });
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Error al procesar {archivoJson}: {ex.Message}");
                }
            }
            else
            {
                // Si el archivo no existe (ej. no hay motores en el proyecto), limpiamos la lista
                Application.Current.Dispatcher.Invoke(() => ListaDispositivos.Clear());
                // Opcional: Mostrar aviso
                // MessageBox.Show("No se han generado datos para: " + archivoJson); 
            }
        }

        // --- WORKFLOW (Pendiente de implementar con Openness) ---

        private void EjecutarSyncConstantes()
        {
            MessageBox.Show("TODO: Comparar N_MAX con TIA Portal");
        }

        private void EjecutarRefactoring()
        {
            MessageBox.Show("TODO: Renombrar tags en TIA Portal");
        }

        private void EjecutarInyeccion()
        {
            MessageBox.Show("TODO: Inyectar datos en DBs");
        }
    }
}