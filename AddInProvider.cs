using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Siemens.Engineering;
using Siemens.Engineering.AddIn;
using Siemens.Engineering.AddIn.Menu;
namespace ZC_ALM_TOOLS
{
    public class AddInProvider : ProjectTreeAddInProvider //or ProjectLibraryTreeAddInProvider or GlobalLibraryTreeAddInProvider
    {
        /// <summary>
        ///The global TIA Portal Object 
        ///<para>It will be used in the TIA Add-In.</para>
        /// </summary>
        TiaPortal _tiaportal;
        /// <summary>
        /// The constructor of the AddInProvider.
        /// <para>- Creates an object of the class AddInProvider</para>
        /// <para>- Called when a right-click is performed in TIA</para>
        /// </summary>
        /// <param name="tiaportal">
        /// Represents the actual used TIA Portal process.
        /// </param>
        public AddInProvider(TiaPortal tiaportal)
        {
            /*
            * The acutal TIA Portal process is saved in the 
            * global TIA Portal variable _tiaportal
            */
            _tiaportal = tiaportal;
        }
        /// <summary>
        /// The method is supplemented to include the Add-In
        /// in the Context Menu of TIA Portal. 
        /// </summary>
        /// <typeparam name="ContextMenuAddIn">
        /// The Add-In will be displayed in 
        /// the Context Menu of TIA Portal.
        /// </typeparam>
        /// <returns>
        /// A new instance of the class AddIn will be created
        /// which contains the main functionality of the Add-In
        /// </returns>
        protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()
        {
            yield return new AddIn(_tiaportal);
        }
    }
}
