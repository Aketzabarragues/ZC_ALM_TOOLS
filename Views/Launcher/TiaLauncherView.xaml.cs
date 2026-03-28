using System.Windows;
using Siemens.Engineering;
using ZC_ALM_TOOLS.Services.TiaPortal;

namespace ZC_ALM_TOOLS.Views.Launcher
{

    // ==================================================================================================================
    /// <summary>
    /// Clase encargada de mostrar la ventana de selección de instancia de Tia Portal para conectar con ella
    /// </summary>
    public partial class TiaLauncherView : Window
    {


        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public TiaLauncherView()
        {
            InitializeComponent();
            LoadInstances();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para cargar las instancias de Tia Portal disponibles en el sistema y mostrarlas en la lista
        /// </summary>
        private void LoadInstances()
        {
            lstInstances.ItemsSource = TiaManager.GetAvailableProcesses();
            if (lstInstances.Items.Count > 0)
                lstInstances.SelectedIndex = 0;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para refrescar la lista de instancias de Tia Portal disponibles, se llama al hacer click en el botón "Refrescar"
        /// </summary>
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadInstances();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para conectar con la instancia de Tia Portal seleccionada, se llama al hacer click en el botón "Conectar"
        /// </summary>
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (lstInstances.SelectedItem is TiaPortalProcess selectedProcess)
            {
                this.IsEnabled = false;

                if (TiaManager.Attach(selectedProcess))
                {
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Error al conectar con la instancia seleccionada.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.IsEnabled = true;
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una instancia.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
