using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // <--- IMPRESCINDIBLE AÑADIR ESTO
using System.Text; // Para Encoding
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Models;
using Siemens.Engineering.HW.Features; // IMPRESCINDIBLE PARA SoftwareContainer

namespace ZC_ALM_TOOLS
{
    public partial class MainWindow : Window
    {
        private TiaPortal _portal;
        private PlcSoftware _plc;
        private List<ProcessConfig> _listaProcesosExcel = new List<ProcessConfig>();
        private string _rutaJsonTemp = Path.Combine(Path.GetTempPath(), "output_tia_portal.json");
        // VARIABLE NUEVA: Aquí guardaremos TODOS los parámetros del Excel en memoria
        private List<ParameterConfigReal> _cacheTodosLosParametrosReales = new List<ParameterConfigReal>();
        private List<ParameterConfigInt> _cacheTodosLosParametrosEnteros = new List<ParameterConfigInt>();

        public MainWindow(TiaPortal portal, PlcSoftware plc)
        {
            InitializeComponent();
            _portal = portal;
            _plc = plc;

            // CARGAR LISTA NADA MÁS ABRIR
            CargarDispositivosDelProyecto();
        }


        // 1. BOTÓN EXAMINAR: Carga los procesos iniciales
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "Excel|*.xlsm;*.xlsx" };
            if (ofd.ShowDialog() == true)
            {
                TxtPath.Text = ofd.FileName;

                // 1. Procesos
                EjecutarPython(ofd.FileName, "leer_procesos", "procesos.json");

                // 2. Parámetros Reales (Caché)
                EjecutarPython(ofd.FileName, "leer_preal", "preal.json");

                // 3. Parámetros Internos (Caché) --> NUEVO
                EjecutarPython(ofd.FileName, "leer_pint", "pint.json");



            }
        }



        // 2. SELECCIÓN DE FILA: Carga los parámetros filtrados
        // ASEGÚRATE DE AÑADIR SelectionChanged="DgProcesos_SelectionChanged" EN EL XAML AL DGPROCESOS
        private void DgProcesos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var proceso = DgProcesos.SelectedItem as ProcessConfig;

            if (proceso != null)
            {
                // FILTRADO INSTANTÁNEO EN MEMORIA (C# LINQ)
                // "Dame todos los parámetros donde la columna 'Proceso' coincida con el nombre seleccionado"


                // Asignamos al DataGrid
                if (DgParametros != null)
                {

                    var prealFiltrados = _cacheTodosLosParametrosReales
                            .Where(p => p.Proceso == proceso.Nombre)
                            .ToList();
                    DgParametros.ItemsSource = prealFiltrados;

                    // 2. Filtrar Internos --> NUEVO
                    var pintFiltrados = _cacheTodosLosParametrosEnteros
                            .Where(p => p.Proceso == proceso.Nombre)
                            .ToList();
                    if (DgPInt != null) DgPInt.ItemsSource = pintFiltrados;
                }


            }
        }




        // 3. FUNCIÓN MAESTRA DE EJECUCIÓN
        private void EjecutarPython(string rutaExcel, string accion, string nombreJsonEsperado, string filtro = null)
        {
            try
            {
                // 1. Preparar rutas
                string tempRaiz = Path.GetTempPath();
                string carpetaTrabajo = Path.Combine(tempRaiz, "_ZC_ALM_TOOLS");
                if (!Directory.Exists(carpetaTrabajo)) Directory.CreateDirectory(carpetaTrabajo);

                string rutaJsonDestino = Path.Combine(carpetaTrabajo, nombreJsonEsperado);
                if (File.Exists(rutaJsonDestino)) File.Delete(rutaJsonDestino);

                // 2. Construir Argumentos
                string scriptPath = @"C:\Users\ABH\Desktop\ZC_ALM_TOOLS\Nueva carpeta\main.py";
                string argumentos = $"\"{scriptPath}\" --path \"{rutaExcel}\" --action \"{accion}\"";

                if (!string.IsNullOrEmpty(filtro))
                {
                    argumentos += $" --filter \"{filtro}\"";
                }

                // 3. Ejecutar
                Siemens.Engineering.AddIn.Utilities.Process.Start("python.exe", argumentos);

                // 4. Esperar
                bool listo = false;
                for (int i = 0; i < 50; i++)
                {
                    if (File.Exists(rutaJsonDestino)) { listo = true; break; }
                    Thread.Sleep(200);
                }

                if (!listo)
                {
                    MessageBox.Show($"Timeout: Python no creó {nombreJsonEsperado}");
                    return;
                }

                // 5. Leer y Parsear
                string jsonContent = File.ReadAllText(rutaJsonDestino, Encoding.UTF8);

                if (accion == "leer_procesos")
                {
                    // PROCESOS: Se muestran directamente en la tabla de arriba
                    var lista = JsonParserHelper.ParsearProcesos(jsonContent);
                    DgProcesos.ItemsSource = lista;
                }
                else if (accion == "leer_preal")
                {

                    // 1. Guardamos TODOS los datos en la variable de memoria (Caché)
                    _cacheTodosLosParametrosReales = JsonParserHelper.ParsearParametros(jsonContent);

                    // 2. Vaciamos la tabla visualmente (para que no muestre 5000 filas mezcladas)
                    //    Se llenará solo cuando el usuario haga clic en un proceso.
                    if (DgParametros != null) DgParametros.ItemsSource = null;
                }
                else if (accion == "leer_pint") // <--- NUEVO BLOQUE
                {
                    _cacheTodosLosParametrosEnteros = JsonParserHelper.ParsearPInt(jsonContent);
                    if (DgPInt != null) DgPInt.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error Ejecución: " + ex.Message);
            }
        }


        private void CargarDispositivosDelProyecto()
        {
            var listaDispositivos = new List<ProjectDevice>();

            try
            {
                if (_portal.Projects.Count == 0) return;
                var proyecto = _portal.Projects[0];

                // Recorremos todos los dispositivos hardware
                foreach (Device device in proyecto.Devices)
                {
                    // Un Device puede tener varios DeviceItems (ej: Rack, CPU, Tarjetas)
                    // Buscamos recursivamente o iteramos los items principales
                    foreach (DeviceItem item in device.DeviceItems)
                    {
                        BuscarSoftwareEnDeviceItem(item, listaDispositivos);
                    }
                }

                // Asignamos al nuevo DataGrid (que crearemos en el paso 3)
                DgDispositivos.ItemsSource = listaDispositivos;

                // Opcional: Seleccionar automáticamente el PLC con el que abriste el Add-In
                if (_plc != null && DgDispositivos.Items.Count > 0)
                {
                    // Lógica simple para intentar pre-seleccionar
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error cargando dispositivos: " + ex.Message);
            }
        }

        // Función auxiliar para buscar PlcSoftware o HmiTarget dentro del hardware
        private void BuscarSoftwareEnDeviceItem(DeviceItem item, List<ProjectDevice> lista)
        {
            // 1. Intentamos obtener el CONTENEDOR DE SOFTWARE
            // (Esto funciona para CPUs y HMIs, pero devuelve null para Racks o tarjetas tontas)
            var container = item.GetService<SoftwareContainer>();

            if (container != null && container.Software != null)
            {
                // 2. Comprobamos si lo que hay dentro es un PLC
                if (container.Software is PlcSoftware plc)
                {
                    lista.Add(new ProjectDevice
                    {
                        Name = item.Name, // Nombre del dispositivo (ej: "PLC_1")
                        Type = "PLC",
                        DeviceObject = plc
                    });
                }
                // 3. Comprobamos si es un HMI (Unified, Comfort, etc.)
                else if (container.Software is HmiTarget hmi)
                {
                    lista.Add(new ProjectDevice
                    {
                        Name = item.Name,
                        Type = "HMI",
                        DeviceObject = hmi
                    });
                }
            }

            // 4. Recursividad: Miramos dentro de los sub-elementos (hijos)
            // Esto es necesario porque a veces el software está anidado dentro de carpetas de hardware
            foreach (DeviceItem subItem in item.DeviceItems)
            {
                BuscarSoftwareEnDeviceItem(subItem, lista);
            }
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            // Lógica futura de generación
        }
    }
}


   
