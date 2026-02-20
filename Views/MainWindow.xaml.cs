using System.Windows;
using Siemens.Engineering;
using Siemens.Engineering.SW; // <--- IMPRESCINDIBLE
using ZC_ALM_TOOLS.ViewModels;

namespace ZC_ALM_TOOLS.Views
{
    public partial class MainWindow : Window
    {
        // Este constructor DEBE coincidir con el 'new MainWindow(...)' de AddIn.cs
        public MainWindow(TiaPortal tiaPortal, Project project)
        {
            InitializeComponent();

            // Pasamos los objetos al ViewModel
            this.DataContext = new MainViewModel(tiaPortal, project);
        }
    }
}