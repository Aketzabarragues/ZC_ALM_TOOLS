using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
        private ObservableCollection<object> _listaDispositivos;
        public ObservableCollection<object> ListaDispositivos
        {
            get => _listaDispositivos;
            set { _listaDispositivos = value; OnPropertyChanged(); }
        }

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
                    CargarDatosDesdeTxt(_rutaTempCache);
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


            
            ListaDispositivos = new ObservableCollection<object>();

            // Configurar comandos (Binds a los botones)
            SyncConstantesCommand = new RelayCommand(EjecutarSyncConstantes);
            RefactoringCommand = new RelayCommand(EjecutarRefactoring);
            InyectarDatosCommand = new RelayCommand(EjecutarInyeccion);
        }



        // --- MÉTODOS DE CARGA DE DATOS ---
        public void CargarDatosDesdeTxt(string carpetaTemp)
        {
            try
            {
                if (string.IsNullOrEmpty(carpetaTemp)) return;
                _rutaTempCache = carpetaTemp;

                // 1. Determinar qué archivo y familia cargar
                string familia = FamiliaSeleccionada ?? "";
                string nombreArchivo = "disp_v.txt";
                if (familia.ToLower().Contains("disp_ed")) nombreArchivo = "disp_ed.txt";
                else if (familia.ToLower().Contains("disp_ea")) nombreArchivo = "disp_ea.txt";

                string rutaCompleta = Path.Combine(carpetaTemp, nombreArchivo);
                if (!File.Exists(rutaCompleta)) return;

                // 2. Leer líneas
                string[] lineas = File.ReadAllLines(rutaCompleta);
                var listaNueva = new List<object>();

                for (int i = 1; i < lineas.Length; i++)
                {
                    string linea = lineas[i];
                    if (string.IsNullOrWhiteSpace(linea)) continue;
                    string[] c = linea.Split('|');

                    try
                    {
                        // 3. Mapeo condicional según el archivo
                        if (nombreArchivo == "disp_ed.txt")
                            listaNueva.Add(MapearDigital(c));
                        else if (nombreArchivo == "disp_ea.txt")
                            listaNueva.Add(MapearAnalogica(c));
                        else
                            listaNueva.Add(MapearValvula(c));
                    }
                    catch (Exception ex) { Debug.WriteLine($"Error en línea {i}: {ex.Message}"); }
                }

                // 4. Actualización de UI (mantenemos tu lógica de Dispatcher)
                if (Application.Current == null)
                    ActualizarListaUI(listaNueva);
                else
                    Application.Current.Dispatcher.Invoke(() => ActualizarListaUI(listaNueva));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"FALLO CRÍTICO: {ex.Message}");
            }
        }


        private void ActualizarListaUI(List<object> datos)
        {
            if (ListaDispositivos == null)
                ListaDispositivos = new ObservableCollection<object>();

            ListaDispositivos.Clear();
            foreach (var item in datos) ListaDispositivos.Add(item);
        }

















        // Métodos auxiliares para evitar errores de índice o formato
        private string GetVal(string[] array, int index) => index < array.Length ? array[index] : "";
        private int ParseInt(string val) { int.TryParse(val, out int r); return r; }
        private Disp_ED MapearDigital(string[] c) => new Disp_ED
        {
            Numero = ParseInt(GetVal(c, 0)),
            Tag = GetVal(c, 1),
            Descripcion = GetVal(c, 2),
            FAT = GetVal(c, 3),
            EByte = GetVal(c, 4),
            EBit = GetVal(c, 5),
            GrAlarma = GetVal(c, 6),
            Cuadro = GetVal(c, 7),
            Observaciones = GetVal(c, 8),
            CPTag = GetVal(c, 9),
            CPTipo = GetVal(c, 10),
            CPNum = ParseInt(GetVal(c, 11)),
            CPComentario = GetVal(c, 12)
        };

        private Disp_EA MapearAnalogica(string[] c) => new Disp_EA
        {
            UID = GetVal(c, 0),
            Numero = ParseInt(GetVal(c, 1)),
            Tag = GetVal(c, 2),
            Descripcion = GetVal(c, 3),
            FAT = GetVal(c, 4),
            EByte = GetVal(c, 5),
            Unidades = GetVal(c, 6),
            RII = GetVal(c, 7),
            RSI = GetVal(c, 8),
            GrAlarma = GetVal(c, 9),
            Cuadro = GetVal(c, 10),
            Observaciones = GetVal(c, 11),
            CPTag = GetVal(c, 12),
            CPTipo = GetVal(c, 13),
            CPNum = ParseInt(GetVal(c, 14)),
            CPComentario = GetVal(c, 15)
        };

        private Disp_V MapearValvula(string[] c) => new Disp_V
        {
            UID = GetVal(c, 0),
            Numero = ParseInt(GetVal(c, 1)),
            Tag = GetVal(c, 2),
            Descripcion = GetVal(c, 3),
            FAT = GetVal(c, 4),
            SByte = GetVal(c, 5),
            SBit = GetVal(c, 6),
            RRByte = GetVal(c, 7),
            RRBit = GetVal(c, 8),
            RTByte = GetVal(c, 9),
            RTBit = GetVal(c, 10),
            GrAlarma = GetVal(c, 11),
            Cuadro = GetVal(c, 12),
            Observaciones = GetVal(c, 13),
            CPTag = GetVal(c, 14),
            CPTipo = GetVal(c, 15),
            CPNum = ParseInt(GetVal(c, 16)),
            CPComentario = GetVal(c, 17)
        };




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