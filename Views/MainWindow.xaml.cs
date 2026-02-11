using System.Windows;
using Siemens.Engineering;
using Siemens.Engineering.SW; // Necesario para 'PlcSoftware'
using ZC_ALM_TOOLS.ViewModels;

namespace ZC_ALM_TOOLS.Views // O el namespace donde esté tu MainWindow
{
    public partial class MainWindow : Window
    {
        // Actualizamos el constructor para recibir AMBOS objetos
        public MainWindow(TiaPortal tiaPortal, PlcSoftware plcSoftware)
        {
            InitializeComponent();

            // Se los pasamos al ViewModel
            this.DataContext = new MainViewModel(tiaPortal, plcSoftware);
        }
    }
}