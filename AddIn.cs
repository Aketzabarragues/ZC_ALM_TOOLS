using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using ZC_ALM_TOOLS.Views;

namespace ZC_ALM_TOOLS
{


    public class AddIn : ContextMenuAddIn
    {
        
        private const string s_DisplayNameOfAddIn = "ZC ALM TOOLS";
        private readonly TiaPortal _tiaPortal;


        // =================================================================================================================
        // CONSTRUCTOR
        public AddIn(TiaPortal tiaportal) : base(s_DisplayNameOfAddIn)
        {
            _tiaPortal = tiaportal;
        }

        // =================================================================================================================
        // Creacion de menu contextual
        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {

            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "ZC ALM TOOLS",
                StartGeneratorTool,
                OnCheckIfContextIsValid);

        }

        // =================================================================================================================
        // Chequear si el contexto es valido
        private MenuStatus OnCheckIfContextIsValid(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            //return _tiaPortal.Projects.Any() ? MenuStatus.Enabled : MenuStatus.Hidden;
            bool isProjectSelected = selectionProvider.GetSelection<Project>().Any();

            return isProjectSelected ? MenuStatus.Enabled : MenuStatus.Disabled;
        }

        // =================================================================================================================
        // Iniciar aplicacion
        private void StartGeneratorTool(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            var project = selectionProvider.GetSelection<Project>().FirstOrDefault(); // _tiaPortal.Projects.FirstOrDefault();
            if (project == null) return;
            
            MainWindow window = new MainWindow(_tiaPortal, project);
            window.ShowDialog();
            //window.Show();

        }


    }
}