using System.Windows;
using System.Windows.Controls;
using ZC_ALM_TOOLS.ViewModels;

namespace ZC_ALM_TOOLS.Views
{
    public partial class ProcessView : UserControl
    {
        public ProcessView()
        {
            InitializeComponent();
        }


        private void DbgButton_Click(object sender, RoutedEventArgs e)
        {
            var dc = this.DataContext;
            if (dc == null)
            {
                MessageBox.Show("¡EL DATACONTEXT ES NULO!");
            }
            else
            {
                MessageBox.Show($"Tipo: {dc.GetType().Name}\nHashCode: {dc.GetHashCode()}");

                var vm = dc as ZC_ALM_TOOLS.ViewModels.ProcessViewModel;
                if (vm != null)
                {
                    MessageBox.Show($"Comando instanciado: {vm.DumpProcessesCommand != null}\nTotal Procesos: {vm.Processes.Count}");
                }
            }
        }


    }
}
