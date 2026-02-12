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
using ZC_ALM_TOOLS.Models;

namespace ZC_ALM_TOOLS.ViewModels
{
    public class MainViewModel : ObservableObject
    {


        private Dictionary<string, List<object>> _cacheIngenieria = new Dictionary<string, List<object>>();


        public DispositivosViewModel DispositivosViewModel { get; set; }

        private TiaService _tiaService;
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
        public RelayCommand ConfigurarRutaExeCommand { get; set; }







        public MainViewModel(TiaPortal tiaPortal, PlcSoftware plcSoftware)
        {

            _tiaPortal = tiaPortal;
            _plcSoftware = plcSoftware;

            // 1. Creamos el servicio
            _tiaService = new TiaService(plcSoftware);

            // 2. Inicializamos el sub-viewmodel
            DispositivosViewModel = new DispositivosViewModel();

            // 3. CONEXIONES (Cables)
            // Pasamos el servicio al sub-viewmodel
            DispositivosViewModel.SetTiaService(_tiaService);

            // Suscribimos el sub-viewmodel a nuestra función de ActualizarEstado
            DispositivosViewModel.StatusRequest = (msg, error) => ActualizarEstado(msg, error);

            // Suscribimos también el servicio para que él pueda hablar solo
            _tiaService.OnStatusChanged = (msg, error) => ActualizarEstado(msg, error);


            InicializarConfiguracion();

            MensajeEstado = $"Conectado a PLC: {plcSoftware.Name}";

            CargarDatosCommand = new RelayCommand(CargarExcelYGenerarJson);
            ConfigurarRutaExeCommand = new RelayCommand(EjecutarConfiguracionRuta);
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
                    ActualizarEstadoDuranteCiclo();
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
                ActualizarEstado("Sincronizando base de datos interna...");


                CargarTodoDesdeCarpeta(carpetaTrabajo);


                // --- INYECCIÓN A LOS SUB-VIEWMODELS ---
                DispositivosViewModel.SetDatos(_cacheIngenieria);

                // En el futuro inyectarás aquí otros módulos:
                // ProcesosViewModel.SetDatos(listaProcesos);

                ActualizarEstado("Listo. Todos los módulos cargados.");
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


        // ==========================================================================================================================
        // Carga de todos los archivos en diccionario
        private void CargarTodoDesdeCarpeta(string carpeta)
        {
            _cacheIngenieria.Clear();

            // Cargamos cada familia usando tus funciones de mapeo existentes
            _cacheIngenieria["disp_v"] = LeerArchivoEspecifico(Path.Combine(carpeta, "disp_v.txt"), "disp_v");
            _cacheIngenieria["disp_ed"] = LeerArchivoEspecifico(Path.Combine(carpeta, "disp_ed.txt"), "disp_ed");
            _cacheIngenieria["disp_ea"] = LeerArchivoEspecifico(Path.Combine(carpeta, "disp_ea.txt"), "disp_ea");

            // Aquí podrás añadir: 
            // var procesos = LeerArchivoProcesos(Path.Combine(carpeta, "procesos.txt"));
        }



        // ==========================================================================================================================
        // Funcion para leer archivo especifico
        private List<object> LeerArchivoEspecifico(string ruta, string tipo)
        {
            var lista = new List<object>();
            if (!File.Exists(ruta)) return lista;

            string[] lineas = File.ReadAllLines(ruta);
            for (int i = 1; i < lineas.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lineas[i])) continue;
                string[] c = lineas[i].Split('|');
                try
                {
                    if (tipo == "disp_v") lista.Add(Disp_V.FromCsv(c));
                    else if (tipo == "disp_ed") lista.Add(Disp_ED.FromCsv(c));
                    else if (tipo == "disp_ea") lista.Add(Disp_EA.FromCsv(c));
                }
                catch { /* Omitir líneas con error */ }
            }
            return lista;
        }



        // ==========================================================================================================================
        // Funcion para editar el archivo de configuracion
        private void EjecutarConfiguracionRuta()
        {
            try
            {
                string carpetaConfig = Path.Combine(Path.GetTempPath(), "_ZC_ALM_TOOLS");
                string archivoConfig = Path.Combine(carpetaConfig, "config_path.txt");

                // Si por algún motivo no existe, lo creamos con la ruta por defecto antes de abrirlo
                if (!File.Exists(archivoConfig))
                {
                    InicializarConfiguracion();
                }

                // Abrimos el Notepad con el archivo
                Siemens.Engineering.AddIn.Utilities.Process.Start("notepad.exe", $"\"{archivoConfig}\"");

                ActualizarEstado("Editando configuración en Notepad...");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir el editor: {ex.Message}");
            }
        }



        // ==========================================================================================================================
        // Método auxiliar para actualizar el estado con color opcional
        private void ActualizarEstado(string mensaje, bool esError = false)
        {
            MensajeEstado = mensaje;
            ColorEstado = esError ? "Red" : "Black";
        }


        // ==========================================================================================================================
        // Método auxiliar para actualizar el estado durante un bucle
        private void ActualizarEstadoDuranteCiclo()
        {
            System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(delegate (object f)
                {
                    ((System.Windows.Threading.DispatcherFrame)f).Continue = false;
                    return null;
                }), frame);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

    }
}