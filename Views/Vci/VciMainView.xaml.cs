using System.Windows;
using Siemens.Engineering;
using ZC_ALM_TOOLS.ViewModels.Vci;

namespace ZC_ALM_TOOLS.Views.Vci
{

    public partial class VciMainView : Window
    {
        public VciMainView(TiaPortal tiaPortal, Project project)
        {
            InitializeComponent();
            this.DataContext = new VciMainViewModel(tiaPortal, project);
        }
    }
}
