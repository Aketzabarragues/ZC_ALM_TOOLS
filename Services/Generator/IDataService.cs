using System.Collections.Generic;
using System.Threading.Tasks;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.Generator;

namespace ZC_ALM_TOOLS.Services.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Interfaz del servicio de datos para la generación de código. Proporciona métodos para cargar procesos, parámetros, alarmas, 
    /// etapas y conexiones desde archivos Excel, así como para cargar datos específicos de categorías de dispositivos y configuraciones. 
    /// También incluye un método para crear una instancia vacía de datos de dispositivo según la categoría.
    /// </summary>
    public interface IDataService
    {
        Task<List<Process>> LoadProcessAsync(string path, string sheet, string table);
        Task<List<Parameter>> LoadParametersAsync(string path, string sheet, string table);
        Task<List<Alarms>> LoadAlarmsAsync(string path, string sheet, string table);
        Task<List<ProcessStage>> LoadStagesAsync(string path, string sheet, string table);
        Task<List<Connection>> LoadConectionsAsync(string path, string sheet, string table);
        Task<List<object>> LoadDispCategoryDataAsync(string path, ConfigDeviceCategory cat);
        Task<List<Disp_Config>> LoadDeviceNMaxAsync(string path, ConfigDeviceSettings settings);
        IDevice CreateEmptyDispData(ConfigDeviceCategory cat);

    }
}
