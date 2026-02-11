using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection; // Necesario para encontrar la ruta real
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public DispositivosViewModel DispositivosViewModel { get; set; }

        private bool _isBusy;
        public bool IsBusy
        {
            get { return _isBusy; }
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private TiaPortal _tiaPortal;
        private PlcSoftware _plcSoftware;

        private string _archivoExcelSeleccionado;
        public string ArchivoExcelSeleccionado
        {
            get { return _archivoExcelSeleccionado; }
            set { _archivoExcelSeleccionado = value; OnPropertyChanged(); }
        }


        private string _exePath;

        private string _mensajeEstado;
        public string MensajeEstado
        {
            get { return _mensajeEstado; }
            set { _mensajeEstado = value; OnPropertyChanged(); }
        }

        private string _colorEstado = "Black"; // Color por defecto
        public string ColorEstado
        {
            get { return _colorEstado; }
            set
            {
                _colorEstado = value;
                OnPropertyChanged();
            }
        }
                
        public RelayCommand CargarDatosCommand { get; set; }

        public MainViewModel(TiaPortal tiaPortal, PlcSoftware plcSoftware)
        {
            _tiaPortal = tiaPortal;
            _plcSoftware = plcSoftware;
            DispositivosViewModel = new DispositivosViewModel();


            InicializarConfiguracion();

            MensajeEstado = $"Conectado a PLC: {plcSoftware.Name}";

            CargarDatosCommand = new RelayCommand(CargarExcelYGenerarJson);
        }



        private void InicializarConfiguracion()
        {
            try
            {
                string carpetaConfig = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");
                string archivoConfig = Path.Combine(carpetaConfig, "config_path.txt");

                // 1. Crear carpeta si no existe
                if (!Directory.Exists(carpetaConfig)) Directory.CreateDirectory(carpetaConfig);

                // 2. Gestionar el archivo de ruta
                if (File.Exists(archivoConfig))
                {
                    _exePath = File.ReadAllText(archivoConfig).Trim();
                }
                else
                {
                    // Ruta por defecto si es la primera vez que se abre
                    _exePath = @"C:\Program Files\Siemens\Automation\Portal V18\AddIns\ZC_ALM_TOOLS\ZC_Extractor.exe";
                    File.WriteAllText(archivoConfig, _exePath);
                }
            }
            catch (Exception ex)
            {
                _exePath = @"C:\Program Files\Siemens\Automation\Portal V18\AddIns\ZC_ALM_TOOLS\ZC_Extractor.exe";
                // No lanzamos MessageBox aquí para no molestar al abrir el Add-in, 
                // a menos que sea un error crítico.
            }
        }


        private void CargarExcelYGenerarJson()
        {
            try
            {
                // PASO 1: Abrir Selector
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsm;*.xlsx",
                    Title = "Selecciona el archivo de definición"
                };

                ActualizarEstado("Esperando selección de archivo...");

                if (openFileDialog.ShowDialog() != true)
                {
                    ActualizarEstado("Operación cancelada.");
                    return;
                }

                ArchivoExcelSeleccionado = openFileDialog.FileName;

                // PASO 2: Configuración de rutas
                string exePath = _exePath;
                string carpetaTrabajo = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");

                // PASO 3: Verificar existencia del extractor
                if (!File.Exists(exePath))
                {
                    ActualizarEstado("Error: No se encuentra ZC_Extractor.exe", true);
                    MessageBox.Show($"El extractor no existe en la ruta configurada:\n{exePath}", "Error de configuración");
                    return;
                }

                // PASO 4: Construir Argumentos
                string argumentos = "--path \"" + ArchivoExcelSeleccionado + "\"";

                // PASO 5: Borrar archivos previos
                ActualizarEstado("Limpiando archivos temporales...");
                LimpiarCarpetaTemporal(carpetaTrabajo);

                // PASO 6: EJECUCIÓN
                try
                {
                    ActualizarEstado("Ejecutando extractor Python...");
                    Siemens.Engineering.AddIn.Utilities.Process.Start(exePath, argumentos);
                }
                catch (Exception ex)
                {
                    ActualizarEstado("Error al iniciar el proceso externo.", true);
                    MessageBox.Show($"Fallo al ejecutar Siemens.Process.Start:\n{ex.Message}", "Error Crítico");
                    return;
                }

                // --- PASO 7: ESPERA ACTIVA (Timeout 20s) ---
                ActualizarEstado("Generando archivos TXT (esperando hasta 30s)...");

                List<string> archivosEsperados = new List<string>
                {
                    Path.Combine(carpetaTrabajo, "procesos.txt"),
                    Path.Combine(carpetaTrabajo, "preal.txt"),
                    Path.Combine(carpetaTrabajo, "pint.txt"),
                    Path.Combine(carpetaTrabajo, "alarmas.txt"),
                    Path.Combine(carpetaTrabajo, "disp_ed.txt"),
                    Path.Combine(carpetaTrabajo, "disp_ea.txt"),
                    Path.Combine(carpetaTrabajo, "disp_v.txt")
                };

                bool todosListos = false;
                for (int i = 0; i < 150; i++)
                {
                    bool faltanArchivos = false;
                    foreach (string ruta in archivosEsperados)
                    {
                        if (!File.Exists(ruta))
                        {
                            faltanArchivos = true;
                            break;
                        }
                    }

                    if (!faltanArchivos)
                    {
                        todosListos = true;
                        break;
                    }
                    Thread.Sleep(200);
                }

                if (!todosListos)
                {
                    string faltantes = "";
                    foreach (string ruta in archivosEsperados)
                    {
                        if (!File.Exists(ruta)) faltantes += Path.GetFileName(ruta) + " ";
                    }
                    ActualizarEstado($"Error: Tiempo de espera agotado.", true);
                    MessageBox.Show($"Timeout: No se crearon todos los archivos.\nFaltan: {faltantes}", "Error de Extracción");
                    return;
                }

                // PASO 8: Cargar Datos
                if (Directory.Exists(carpetaTrabajo))
                {
                    if (DispositivosViewModel == null)
                    {
                        ActualizarEstado("Error interno: ViewModel nulo.", true);
                        return;
                    }

                    ActualizarEstado("Cargando datos en la tabla...");
                    DispositivosViewModel.CargarDatosDesdeTxt(carpetaTrabajo);
                    ActualizarEstado("Listo. Datos cargados correctamente.");
                }
                else
                {
                    ActualizarEstado("Error: Carpeta de trabajo desaparecida.", true);
                }
            }
            catch (Exception ex)
            {
                ActualizarEstado("Crash general en el proceso.", true);
                MessageBox.Show($"CRASH GENERAL:\n{ex.Message}\n{ex.StackTrace}", "Debug Fatal");
            }
        }




        private void LimpiarCarpetaTemporal(string ruta)
        {
            try
            {
                if (Directory.Exists(ruta))
                {
                    string[] archivos = Directory.GetFiles(ruta);
                    foreach (string archivo in archivos)
                    {
                        // OBTENER SOLO EL NOMBRE DEL ARCHIVO (sin la ruta completa)
                        string nombreArchivo = Path.GetFileName(archivo);

                        // SEGURIDAD: Si es el archivo de configuración, NO lo borramos
                        if (nombreArchivo.Equals("config_path.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Salta a la siguiente iteración del bucle
                        }

                        try
                        {
                            File.Delete(archivo);
                        }
                        catch (IOException)
                        {
                            Debug.WriteLine($"No se pudo borrar (está en uso): {archivo}");
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(ruta);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al limpiar carpeta temporal: {ex.Message}");
            }
        }














        // Método auxiliar para actualizar el estado con color opcional
        private void ActualizarEstado(string mensaje, bool esError = false)
        {
            MensajeEstado = mensaje;
            ColorEstado = esError ? "Red" : "Black";
        }

    }
}