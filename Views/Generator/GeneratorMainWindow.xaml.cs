using System.Windows;
using Siemens.Engineering;
using ZC_ALM_TOOLS.ViewModels;

namespace ZC_ALM_TOOLS.Views
{
    public partial class GeneratorMainWindow : Window
    {
        
        public GeneratorMainWindow(TiaPortal tiaPortal, Project project)
        {
            InitializeComponent();

            // Pasamos los objetos al ViewModel
            this.DataContext = new GeneratorMainViewModel(tiaPortal, project);
        }
    }
}