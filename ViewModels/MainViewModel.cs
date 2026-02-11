using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        // --- VIEWMODELS HIJOS (Los que controlan cada pestaña) ---
        public DispositivosViewModel DispositivosVM { get; set; }
        // public ProcesosViewModel ProcesosVM { get; set; } // (Futuro)
        // public AlarmasViewModel AlarmasVM { get; set; }   // (Futuro)

        // --- PROPIEDADES DE ESTADO ---
        private string _archivoExcelSeleccionado;
        public string ArchivoExcelSeleccionado
        {
            get { return _archivoExcelSeleccionado; }
            set { _archivoExcelSeleccionado = value; OnPropertyChanged(); }
        }

        private string _mensajeEstado;
        public string MensajeEstado
        {
            get { return _mensajeEstado; }
            set { _mensajeEstado = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get { return _isBusy; }
            set { _isBusy = value; OnPropertyChanged(); }
        }

        // --- COMANDOS ---
        public RelayCommand CargarDatosCommand { get; set; }

        // --- CONSTRUCTOR ---
        public MainViewModel()
        {
            // Inicializamos los ViewModels de las pestañas
            DispositivosVM = new DispositivosViewModel();

            // Texto inicial
            MensajeEstado = "Esperando archivo Excel...";
            ArchivoExcelSeleccionado = "Ningún archivo seleccionado";

            CargarDatosCommand = new RelayCommand(CargarExcelYGenerarJson);
        }

        // --- LÓGICA PRINCIPAL ---
        private async void CargarExcelYGenerarJson()
        {
            // 1. Abrir diálogo de archivo
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsm;*.xlsx",
                Title = "Selecciona el archivo de Definición"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ArchivoExcelSeleccionado = openFileDialog.FileName;
                MensajeEstado = "Ejecutando script de Python...";
                IsBusy = true;

                // Ejecutamos el proceso en segundo plano para no congelar la UI
                bool exito = await Task.Run(() => EjecutarPythonExtractor(ArchivoExcelSeleccionado));

                IsBusy = false;

                if (exito)
                {
                    MensajeEstado = "Datos extraídos correctamente.";

                    // AQUI ES LA MAGIA: Avisamos al módulo de dispositivos que cargue sus datos
                    // Asumimos que el JSON está en %TEMP%/_ZC_ALM_TOOLS/disp_v.json
                    string rutaTemp = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");

                    // Llamamos al método del hijo (que crearemos a continuación)
                    DispositivosVM.CargarDatosDesdeJson(rutaTemp);
                }
                else
                {
                    MensajeEstado = "Error al ejecutar el extractor.";
                    MessageBox.Show("Hubo un error al generar los JSON. Revisa si tienes Python instalado o el EXE generado.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool EjecutarPythonExtractor(string rutaExcel)
        {
            try
            {
                // Buscamos el EXE en la misma carpeta que la DLL del Add-In
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZC_Extractor.exe");

                if (!File.Exists(exePath))
                {
                    MessageBox.Show($"No se encuentra el ejecutable en: {exePath}");
                    return false;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--path \"{rutaExcel}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
        }
    }
}