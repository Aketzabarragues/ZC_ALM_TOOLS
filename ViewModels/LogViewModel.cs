using System.Collections.ObjectModel;
using ZC_ALM_TOOLS.Core;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.ViewModels
{
    public class LogViewModel : ObservableObject
    {
        public ObservableCollection<string> LogMessages => LogService.LogEntries;

        public RelayCommand ClearLogCommand { get; }

        public LogViewModel()
        {
            ClearLogCommand = new RelayCommand(() => LogService.Clear());
        }
    }
}