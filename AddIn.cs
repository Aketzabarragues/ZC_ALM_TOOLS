using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Views;

namespace ZC_ALM_TOOLS
{


    public class AddIn : ContextMenuAddIn
    {


        private const string s_DisplayNameOfAddIn = "ZC ALM TOOLS";
        private readonly TiaPortal _tiaPortal;
                
        public AddIn(TiaPortal tiaportal) : base(s_DisplayNameOfAddIn)
        {
            _tiaPortal = tiaportal;          
        }

        
        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {
            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "ZC ALM TOOLS",
                StartApplication,
                OnCheckIfContextIsValid);
        }

        private MenuStatus OnCheckIfContextIsValid(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            return _tiaPortal.Projects.Any() ? MenuStatus.Enabled : MenuStatus.Hidden;
        }

        private void StartApplication(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            // 1. Obtenemos el proyecto activo
            var project = _tiaPortal.Projects.FirstOrDefault();
            if (project == null) return;

            // 2. Opcional: Obtenemos lo que el usuario tenía seleccionado 
            // por si queremos usarlo como "favorito" más tarde.
            var selection = selectionProvider.GetSelection().FirstOrDefault();

            // 3. Lanzamos la MainWindow pasando el PROYECTO completo
            // Nota: Esto te dará error de compilación hasta que ajustemos el constructor de MainWindow en la Fase 3/4.
            MainWindow window = new MainWindow(_tiaPortal, project);
            window.ShowDialog();
        }


    }
}