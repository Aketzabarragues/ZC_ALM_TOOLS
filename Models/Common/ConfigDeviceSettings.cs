using System.Collections.Generic;

namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// Modelo de datos que almacena la configuración global para el módulo de generación de dispositivos.
    /// Define parámetros generales extraídos del app_config.xml, como el nombre de la tabla de constantes 
    /// maestra y las referencias a los archivos de configuración base.
    /// </summary>
    public class ConfigDeviceSettings
    {
        public string TiaTable { get; set; }
        public string ExcelSheet { get; set; }
        public Dictionary<string, string> ConfigCells { get; set; }
    }
}