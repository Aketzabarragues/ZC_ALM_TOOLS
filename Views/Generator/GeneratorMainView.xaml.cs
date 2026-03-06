using System.Windows;
using Siemens.Engineering;
using ZC_ALM_TOOLS.ViewModels;
using ZC_ALM_TOOLS.ViewModels.Generator;

namespace ZC_ALM_TOOLS.Views.Generator
{
    public partial class GeneratorMainView : Window
    {
        
        public GeneratorMainView(TiaPortal tiaPortal, Project project)
        {
            InitializeComponent();

            // Pasamos los objetos al ViewModel
            this.DataContext = new GeneratorMainViewModel(tiaPortal, project);
        }
    }
}