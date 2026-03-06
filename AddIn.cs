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


        }




        private MenuStatus OnCheckIfContextIsValid(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            return _tiaPortal.Projects.Any() ? MenuStatus.Enabled : MenuStatus.Hidden;
        }




        // =====================================================================================
        // GENERADOR DE PROYECTOS
        private void StartGeneratorTool(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            var project = _tiaPortal.Projects.FirstOrDefault();
            if (project == null) return;
            
            MainWindow window = new MainWindow(_tiaPortal, project);
            window.ShowDialog();

        }


    }
}