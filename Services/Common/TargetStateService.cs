using System.Collections.ObjectModel;
using ZC_ALM_TOOLS.Models.TiaPortal;

namespace ZC_ALM_TOOLS.Services.Common
{
    /// <summary>
    /// Servicio Singleton encargado de mantener el estado global de los dispositivos 
    /// escaneados en TIA Portal (PLC, HMI, SCADA) para poder inyectarlo en cualquier ViewModel.
    /// </summary>
    public class TargetStateService
    {
        public ObservableCollection<TiaTarget> PlcTargets { get; } = new ObservableCollection<TiaTarget>();
        public ObservableCollection<TiaTarget> HmiTargets { get; } = new ObservableCollection<TiaTarget>();
        public ObservableCollection<TiaTarget> ScadaTargets { get; } = new ObservableCollection<TiaTarget>();
    }
}