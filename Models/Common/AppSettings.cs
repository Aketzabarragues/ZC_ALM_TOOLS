using System.Collections.Generic;

namespace ZC_ALM_TOOLS.Models.Common
{
    public class AppSettings
    {
        public ConfigGlobalSettings GlobalSettings { get; set; }
        public ConfigDeviceSettings DeviceSettings { get; set; }
        public List<ConfigDeviceCategory> Devices { get; set; }
        public ConfigProcessSettings ProcessSettings { get; set; }
        public ConfigPRealSettings PRealSettings { get; set; }
        public ConfigPIntSettings PIntSettings { get; set; }
        public ConfigAlarmSettings AlarmSettings { get; set; }
        public ConfigNetworkSettings NetworkSettings { get; set; }
    }
}
