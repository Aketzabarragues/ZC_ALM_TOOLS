using System.Windows.Controls;
using ZC_ALM_TOOLS.ViewModels; // Importante

namespace ZC_ALM_TOOLS.Views
{
    public partial class ViewDispositivos : UserControl
    {
        public ViewDispositivos()
        {
            InitializeComponent();
            // Asignamos el ViewModel como contexto de datos
            this.DataContext = new DispositivosViewModel();
        }
    }
}