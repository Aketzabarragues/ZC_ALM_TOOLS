using System.Linq;
using System.Windows;
using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Views;
using ZC_ALM_TOOLS.Views.Vci;

namespace ZC_ALM_TOOLS
{


    public class AddIn : ContextMenuAddIn
    {


        private const string s_DisplayNameOfAddIn = "ZC ALM TOOLS";
        private readonly TiaPortal _tiaPortal;
        private static bool _isAnyToolOpen = false;




        public AddIn(TiaPortal tiaportal) : base(s_DisplayNameOfAddIn)
        {
            _tiaPortal = tiaportal;          
        }

        


        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {

            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "ZC Generador de Proyectos",
                StartGeneratorTool,
                OnCheckIfContextIsValid);

            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "ZC Auditoría VCI",
                StartVciTool,
                OnCheckIfContextIsValid);

        }




        private MenuStatus OnCheckIfContextIsValid(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            return _tiaPortal.Projects.Any() ? MenuStatus.Enabled : MenuStatus.Hidden;
        }




        // =====================================================================================
        // GENERADOR DE PROYECTOS
        private void StartGeneratorTool(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            if (!TryAcquireLock()) return;

            try
            {
                var project = _tiaPortal.Projects.FirstOrDefault();
                if (project == null) return;

                GeneratorMainWindow window = new GeneratorMainWindow(_tiaPortal, project);
                window.ShowDialog();
            }
            finally
            {
                ReleaseLock();
            }
        }




        // =====================================================================================
        // AUDITOR VCI
        private void StartVciTool(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            if (!TryAcquireLock()) return;

            try
            {
                var project = _tiaPortal.Projects.FirstOrDefault();
                if (project == null) return;

                // ATENCIÓN: Esta ventana aún no existe, deberás crearla en el siguiente paso
                VciMainWindow window = new VciMainWindow(_tiaPortal, project);
                window.ShowDialog();
            }
            finally
            {
                ReleaseLock();
            }
        }




        // =====================================================================================
        // MÉTODOS DE CONTROL DE CONCURRENCIA
        private bool TryAcquireLock()
        {
            if (_isAnyToolOpen)
            {
                MessageBox.Show("Ya hay una herramienta de ZC Tools abierta en este momento.\n\nPor favor, ciérrala antes de abrir otra para evitar conflictos.",
                                "Herramienta en uso", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            _isAnyToolOpen = true;
            return true;
        }

        private void ReleaseLock()
        {
            _isAnyToolOpen = false;
        }


    }
}