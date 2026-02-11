using System;
using System.Collections.Generic;
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
        public ObservableCollection<Disp_V> ListaDispositivos { get; set; }

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


            
            ListaDispositivos = new ObservableCollection<Disp_V>();

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
                // DEBUG 1: ¿Qué ruta llega?
                //MessageBox.Show($"DEBUG 1: Ruta recibida: '{carpetaTemp}'", "Carga TXT");

                if (string.IsNullOrEmpty(carpetaTemp))
                {
                    MessageBox.Show("DEBUG ERROR: La ruta recibida está VACÍA.");
                    return;
                }

                _rutaTempCache = carpetaTemp;

                // DEBUG 2: Familia seleccionada
                //MessageBox.Show($"DEBUG 2: Familia actual: '{FamiliaSeleccionada}'", "Carga TXT");

                string nombreArchivo = "disp_v.txt";
                if (FamiliaSeleccionada != null && FamiliaSeleccionada.ToLower().Contains("disp_ed")) nombreArchivo = "disp_ed.txt";
                else if (FamiliaSeleccionada != null && FamiliaSeleccionada.ToLower().Contains("disp_ea")) nombreArchivo = "disp_ea.txt";

                string rutaCompleta = Path.Combine(carpetaTemp, nombreArchivo);

                // DEBUG 3: Archivo final
                //MessageBox.Show($"DEBUG 3: Buscando archivo en:\n{rutaCompleta}", "Carga TXT");

                if (!File.Exists(rutaCompleta))
                {
                    MessageBox.Show("DEBUG ERROR: El archivo NO EXISTE en esa ruta.");
                    return;
                }

                // DEBUG 4: Lectura
                string[] lineas = File.ReadAllLines(rutaCompleta);
                //MessageBox.Show($"DEBUG 4: Leídas {lineas.Length} líneas.", "Carga TXT");

                var listaNueva = new List<Disp_V>();

                for (int i = 1; i < lineas.Length; i++)
                {
                    string linea = lineas[i];
                    if (string.IsNullOrWhiteSpace(linea)) continue;

                    string[] c = linea.Split('|');

                    // Aquí solemos tener problemas si el array 'c' no tiene los elementos esperados
                    try
                    {
                        var d = new Disp_V
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
                        listaNueva.Add(d);
                    }
                    catch (Exception exItem)
                    {
                        MessageBox.Show($"ERROR parseando línea {i}: {exItem.Message}");
                    }
                }

                // DEBUG 5: Actualización de UI
                //MessageBox.Show("DEBUG 5: Intentando actualizar ListaDispositivos...", "Carga TXT");

                // IMPORTANTE: En Add-ins de TIA Portal a veces Application.Current es null
                if (Application.Current == null)
                {
                    // Si no hay Application, actualizamos directo (estamos en el hilo principal probablemente)
                    ActualizarListaUI(listaNueva);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        ActualizarListaUI(listaNueva);
                    });
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show($"FALLO CRÍTICO en CargarDatosDesdeTxt:\n{ex.Message}\n\nStack:\n{ex.StackTrace}");
            }
        }


        private void ActualizarListaUI(List<Disp_V> datos)
        {
            if (ListaDispositivos == null)
            {                
                ListaDispositivos = new ObservableCollection<Disp_V>();
            }
            ListaDispositivos.Clear();
            foreach (var item in datos) ListaDispositivos.Add(item);
        }

















        // Métodos auxiliares para evitar errores de índice o formato
        private string GetVal(string[] array, int index) => index < array.Length ? array[index] : "";
        private int ParseInt(string val) { int.TryParse(val, out int r); return r; }





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