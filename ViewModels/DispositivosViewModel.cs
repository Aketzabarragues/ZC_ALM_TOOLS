using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
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
        public RelayCommand RefactoringCommand { get; set; }
        public RelayCommand InyectarDatosCommand { get; set; }





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
            RefactoringCommand = new RelayCommand(EjecutarRefactoring);
            InyectarDatosCommand = new RelayCommand(EjecutarInyeccion);
        }



        // --- WORKFLOW (Pendiente de implementar con Openness) ---
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
                    ListaDispositivos.ToList()
                );

                MessageBox.Show("Sincronización completada.\nLa tabla de TIA Portal ahora es idéntica al Excel.", "Sync OK");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en el proceso de sincronización: {ex.Message}");
            }

        }

        private void EjecutarRefactoring()
        {
            MessageBox.Show("TODO: Renombrar tags en TIA Portal");
            if (ListaDispositivos == null || ListaDispositivos.Count == 0)
            {
                MessageBox.Show("Carga primero los datos del Excel.");
                return;
            }

            try
            {
                StatusRequest?.Invoke("Exportando datos actuales del PLC para comparar...", false);

                // 1. Exportación fresca del PLC
                string rutaXml = Path.Combine(Path.GetTempPath(), "_ZC_AL_TOOLS", "temp", "check_comp.xml");
                _tiaService.ExportarTablaVariables("002_Dispositivos", "002_Disp_V", rutaXml);

                // 2. Obtener lo que hay en el PLC en un diccionario
                var dicPlc = _tiaService.LeerDiccionarioDesdeXml(rutaXml);

                // 3. COMPARACIÓN
                int contadorCambios = 0;
                int contadorNuevos = 0;
                int maxValorEnPlc = dicPlc.Keys.Count > 0 ? dicPlc.Keys.Max() : 0;

                foreach (var item in ListaDispositivos)
                {
                    // Usamos dynamic para acceder a .Numero y .Tag sin importar el modelo específico
                    dynamic d = item;

                    if (dicPlc.TryGetValue(d.Numero, out string nombreEnPlc))
                    {
                        if (nombreEnPlc == d.Tag)
                        {
                            d.Estado = "OK (Sincronizado)";
                        }
                        else
                        {
                            d.Estado = $"CAMBIO: '{nombreEnPlc}' -> '{d.Tag}'";
                            contadorCambios++;
                        }
                    }
                    else
                    {
                        d.Estado = "NUEVO (No existe en PLC)";
                        contadorNuevos++;
                    }
                }

                // 4. Informe de SOBRANTES (Variables en PLC que no están en el Excel)
                int totalExcel = ListaDispositivos.Count;
                int sobrantes = dicPlc.Keys.Count(k => k > totalExcel);

                StatusRequest?.Invoke("Comparación finalizada.", false);

                string mensaje = $"Análisis completado:\n\n" +
                                 $"- {contadorNuevos} elementos nuevos a crear.\n" +
                                 $"- {contadorCambios} elementos a renombrar.\n";

                if (sobrantes > 0)
                    mensaje += $"- {sobrantes} elementos SOBRANTES en TIA que serán eliminados.";
                else
                    mensaje += "- No hay elementos sobrantes en TIA Portal.";

                MessageBox.Show(mensaje, "Resultado del Análisis");
            }
            catch (Exception ex)
            {
                StatusRequest?.Invoke("Error en la comparación.", true);
                MessageBox.Show(ex.Message);
            }
        }

        private void EjecutarInyeccion()
        {
            MessageBox.Show("TODO: Inyectar datos en DBs");
        }
    }
}