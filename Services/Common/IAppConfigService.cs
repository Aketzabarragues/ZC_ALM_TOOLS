using System.Collections.Generic;
using ZC_ALM_TOOLS.Models.Common;

namespace ZC_ALM_TOOLS.Services.Common
{
    /// <summary>
    /// Interfaz del servicio de configuración de la aplicación. Proporciona métodos para acceder a las distintas secciones de configuración
    /// </summary>
    public interface IAppConfigService
    {
        void InitializeEnvironment();
        void Reload();
        ConfigGlobalSettings GetGlobalSettings();
        ConfigNetworkSettings GetNetworkConfig();
        ConfigProcessSettings GetProcessConfig();
        ConfigDeviceSettings GetDeviceSettings();
        List<ConfigDeviceCategory> GetDeviceCategories();
        ConfigPRealSettings GetPRealConfig();
        ConfigPIntSettings GetPIntConfig();
        ConfigAlarmSettings GetAlarmConfig();
    }
}