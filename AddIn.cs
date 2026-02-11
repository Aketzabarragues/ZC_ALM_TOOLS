using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.MC.Drives;
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
            object rawSelection = selectionProvider.GetSelection().FirstOrDefault();
            if (rawSelection == null || !(rawSelection is IEngineeringObject))
                return MenuStatus.Hidden;

            if (FindPlcSoftware((IEngineeringObject)rawSelection) != null)
                return MenuStatus.Enabled;

            return MenuStatus.Hidden;
        }

        private void StartApplication(MenuSelectionProvider<IEngineeringObject> selectionProvider)
        {
            var selection = (IEngineeringObject)selectionProvider.GetSelection().FirstOrDefault();
            var plcSoftware = FindPlcSoftware(selection);

            if (plcSoftware != null)
            {
                MainWindow window = new MainWindow(_tiaPortal, plcSoftware);
                window.ShowDialog();
            }
        }

        private PlcSoftware FindPlcSoftware(IEngineeringObject obj)
        {
            if (obj == null) return null;
            if (obj is PlcSoftware software) return software;
            if (obj.Parent != null && obj.Parent is IEngineeringObject parentObj)
                return FindPlcSoftware(parentObj);

            return null;
        }



    }
}