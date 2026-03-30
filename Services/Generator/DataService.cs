using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ZC_ALM_TOOLS.Models.Common;
using ZC_ALM_TOOLS.Models.Generator;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Servicio encargado de la extracción de datos desde Excel. 
    /// Utiliza ClosedXML para una lectura robusta y flexible, con mapeo dinámico de columnas basado en encabezados normalizados.
    /// </summary>
    public class DataService : IDataService
    {

        private readonly ILogService _logService;



        // ==================================================================================================================
        /// <summary>
        /// Constructor del servicio de datos. Requiere un servicio de log para registrar errores y eventos durante la extracción.
        /// </summary>
        public DataService(ILogService logService)
        {
            _logService = logService;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo genérico para cargar datos de una tabla específica en una hoja de Excel. El método es asíncrono y devuelve una lista de objetos del tipo especificado.
        /// </summary>
        private async Task<List<T>> LoadTableDataAsync<T>(string excelPath, string sheetName, string tableName, Func<IXLRangeRow, Dictionary<string, int>, int, T> mapper)
        {
            return await Task.Run(() =>
            {
                var list = new List<T>();
                if (!File.Exists(excelPath)) return list;

                try
                {
                    using (var wb = new XLWorkbook(excelPath))
                    {
                        if (!wb.TryGetWorksheet(sheetName, out var ws)) return list;

                        var table = ws.Tables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                        if (table == null)
                        {
                            _logService.Write($"[DATA-SERVICE] Tabla '{tableName}' no encontrada en la hoja '{sheetName}'.", true);
                            return list;
                        }

                        // Mapeo seguro secuencial (Evita offsets si la tabla no empieza en la Columna A)
                        var headers = new Dictionary<string, int>();
                        int colIndex = 1;

                        foreach (var field in table.Fields)
                        {
                            string key = NormalizeKey(field.Name);
                            if (!headers.ContainsKey(key)) headers.Add(key, colIndex);
                            colIndex++;
                        }

                        int rowIndex = 1;
                        foreach (var row in table.DataRange.Rows())
                        {
                            list.Add(mapper(row, headers, rowIndex++));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.Write($"[DATA-SERVICE] Error en Tabla '{tableName}': {ex.Message}", true);
                }
                return list;
            });
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para normalizar las claves de los encabezados de columna. Elimina espacios y caracteres especiales, y 
        /// convierte a mayúsculas para permitir un mapeo flexible e insensible a formato.
        /// </summary>
        private string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            return new string(key.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para extraer un valor de cadena de una fila de tabla, dado un conjunto de posibles nombres de columna. 
        /// Utiliza el diccionario de encabezados para encontrar el índice correcto y devuelve el valor limpio o una cadena vacía si no se encuentra o está en blanco.
        /// </summary>
        private string GetStr(IXLRangeRow r, Dictionary<string, int> h, params string[] cols)
        {
            foreach (var col in cols)
            {
                if (h.TryGetValue(NormalizeKey(col), out int idx))
                {
                    string val = r.Cell(idx).GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
            return string.Empty;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para extraer un valor entero de una fila de tabla, dado un conjunto de posibles nombres de columna.
        /// </summary>
        private int GetInt(IXLRangeRow r, Dictionary<string, int> h, int defaultVal, params string[] cols)
        {
            foreach (var col in cols)
            {
                if (h.TryGetValue(NormalizeKey(col), out int idx))
                {
                    string val = r.Cell(idx).GetString();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    val = val.Trim();
                    if (val.Contains(".")) val = val.Split('.')[0];
                    if (int.TryParse(val, out int res)) return res;
                }
            }
            return defaultVal;
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para obtener un UID válido. Si el valor extraído es nulo o vacío, genera un nuevo GUID para asegurar que cada objeto tenga un identificador único.
        /// </summary>
        private string GetUidFallback(string extractedUid) =>
            string.IsNullOrEmpty(extractedUid) ? Guid.NewGuid().ToString() : extractedUid;



        // ==================================================================================================================
        /// <summary>
        /// Metodo específico para cargar los datos de Procesos desde la tabla correspondiente en Excel. 
        /// Utiliza el método genérico LoadTableDataAsync con un mapeador específico para crear objetos de tipo Process.
        /// </summary>
        public async Task<List<Process>> LoadProcessAsync(string path, string sheet, string table) =>
            await LoadTableDataAsync(path, sheet, table, (r, h, i) => new Process
            {
                Id = GetUidFallback(GetStr(r, h, "UID", "ID", "GUID")),
                Nombre = GetStr(r, h, "NOMBRE", "PROCESO", "PROCESS"),
                Codigo = GetStr(r, h, "CODIGO", "CODE"),
                NumEtapas = GetInt(r, h, 0, "NUMETAPAS", "ETAPAS"),
                MaxPReal = GetInt(r, h, 0, "PREAL", "MAXPREAL"),
                MaxPInt = GetInt(r, h, 0, "PINT", "MAXPINT"),
                NumAlarmas = GetInt(r, h, 0, "ALARMAS", "NUMALARMAS", "ALARMS")
            });



        // ==================================================================================================================
        /// <summary>
        /// Metodo específico para cargar los datos de Parámetros desde la tabla correspondiente en Excel. 
        /// Utiliza el método genérico LoadTableDataAsync con un mapeador específico para crear objetos de tipo Parameter.
        /// </summary>
        public async Task<List<Parameter>> LoadParametersAsync(string path, string sheet, string table) =>
            await LoadTableDataAsync(path, sheet, table, (r, h, i) => new Parameter
            {
                Uid = GetUidFallback(GetStr(r, h, "UID", "ID", "GUID")),
                Numero = GetInt(r, h, i, "NUMERO", "NUM", "NO"),
                Proceso = GetStr(r, h, "PROCESO", "PROCESS"),
                DbNumber = GetInt(r, h, 0, "NUMDB", "DBNUMBER", "DB"),
                Producto = GetStr(r, h, "PRODUCTO", "PRODUCT"),
                Tipo = GetStr(r, h, "TIPO", "TYPE"),
                Descripcion = GetStr(r, h, "DESCRIPCION", "DESC"),
                ComentarioDB = GetStr(r, h, "COMENTARIODB", "COMENTARIO"),
                Visibilidad = GetStr(r, h, "VISIBILIDAD", "VIS")
            });



        // ==================================================================================================================
        /// <summary>
        /// Metodo específico para cargar los datos de Alarmas desde la tabla correspondiente en Excel.
        /// </summary>
        public async Task<List<Alarms>> LoadAlarmsAsync(string path, string sheet, string table) =>
            await LoadTableDataAsync(path, sheet, table, (r, h, i) => new Alarms
            {
                UID = GetUidFallback(GetStr(r, h, "UID", "ID", "GUID")),
                Numero = GetInt(r, h, i, "NUMERO", "NUM", "NO"),
                Proceso = GetStr(r, h, "PROCESO", "PROCESS"),
                DbNumber = GetInt(r, h, 0, "NUMDB", "DBNUMBER", "DB"),
                Descripcion = GetStr(r, h, "DESCRIPCION", "DESC"),
                ComentarioDB = GetStr(r, h, "COMENTARIODB", "COMENTARIO")
            });



        // ==================================================================================================================
        /// <summary>
        /// Metodo específico para cargar los datos de Etapas de Proceso desde la tabla correspondiente en Excel.
        /// </summary>
        public async Task<List<ProcessStage>> LoadStagesAsync(string path, string sheet, string table) =>
            await LoadTableDataAsync(path, sheet, table, (r, h, i) => new ProcessStage
            {
                Uid = GetUidFallback(GetStr(r, h, "UID", "ID", "GUID")),
                ProcessUid = GetInt(r, h, 0, "PROCESSUID", "PROCUID", "PROCESOID"),
                Numero = GetInt(r, h, i, "NUMERO", "NUM", "NO"),
                Proceso = GetStr(r, h, "PROCESO", "PROCESS"),
                ValorEtapa = GetInt(r, h, 0, "VALORETAPA", "ETAPA"),
                Descripcion = GetStr(r, h, "DESCRIPCION", "DESC"),
                NombreVariable = GetStr(r, h, "NOMBREVARIABLE", "VARIABLE", "VAR"),
                CpTag = GetStr(r, h, "CPTAG"),
                CpComentario = GetStr(r, h, "CPCOMENTARIO")
            });



        // ==================================================================================================================
        /// <summary>
        /// Metodo específico para cargar los datos de Conexiones desde la tabla correspondiente en Excel.
        /// </summary>
        public async Task<List<Connection>> LoadConectionsAsync(string path, string sheet, string table) =>
            await LoadTableDataAsync(path, sheet, table, (r, h, i) => new Connection
            {
                Equipo_Origen = GetStr(r, h, "EQUIPOORIGEN", "ORIGEN"),
                Equipo_Destino = GetStr(r, h, "EQUIPODESTINO", "DESTINO"),
                Protocolo = GetStr(r, h, "PROTOCOLO", "PROT"),
                Nombre_Conexion_TIA = GetStr(r, h, "NOMBRECONEXIONTIA", "CONEXIONTIA", "CONEXION"),
                ID_Local = GetStr(r, h, "IDLOCAL"),
                Puerto_Local = GetStr(r, h, "PUERTOLOCAL", "PUERTO")
            });



        // ==================================================================================================================
        /// <summary>
        /// Metodo específico para cargar los datos de una categoría de dispositivo genérica desde la tabla correspondiente en Excel.
        /// </summary>
        public async Task<List<object>> LoadDispCategoryDataAsync(string path, ConfigDeviceCategory cat) =>
            await LoadTableDataAsync<object>(path, cat.ExcelSheet, cat.ExcelTable, (r, h, i) => {
                IDevice d = CreateEmptyDispData(cat);

                d.UID = GetUidFallback(GetStr(r, h, "UID", "ID", "GUID"));
                d.Numero = GetInt(r, h, i, "NUMERO", "NUM", "NO");
                d.Tag = GetStr(r, h, "TAG");
                d.Descripcion = GetStr(r, h, "DESCRIPCION", "DESC");
                d.CPTag = GetStr(r, h, "CPTAG");
                d.CPComentario = GetStr(r, h, "CPCOMENTARIO");

                foreach (var prop in d.GetType().GetProperties().Where(p => p.CanWrite))
                {
                    if (new[] { "UID", "Numero", "Tag", "Descripcion", "CPTag", "CPComentario", "Estado" }.Contains(prop.Name)) continue;

                    string val = GetStr(r, h, prop.Name);
                    if (!string.IsNullOrEmpty(val))
                    {
                        if (prop.PropertyType == typeof(string)) prop.SetValue(d, val);
                        else if (prop.PropertyType == typeof(int))
                        {
                            if (val.Contains(".")) val = val.Split('.')[0];
                            if (int.TryParse(val, out int res)) prop.SetValue(d, res);
                        }
                    }
                }
                return d;
            });



        // ==================================================================================================================
        /// <summary>
        /// Metodo específico para cargar los valores de configuración N_MAX de dispositivos desde rangos definidos en Excel.
        /// </summary>
        public async Task<List<Disp_Config>> LoadDeviceNMaxAsync(string path, ConfigDeviceSettings settings)
        {
            return await Task.Run(() => {
                var list = new List<Disp_Config>();
                if (!File.Exists(path) || settings?.ConfigCells == null) return list;
                try
                {
                    using (var wb = new XLWorkbook(path))
                    {
                        if (!wb.TryGetWorksheet(settings.ExcelSheet, out var ws)) return list;
                        foreach (var kvp in settings.ConfigCells)
                        {
                            var range = wb.DefinedNames.FirstOrDefault(nr => nr.Name.Equals(kvp.Value, StringComparison.OrdinalIgnoreCase))?.Ranges.FirstOrDefault();
                            if (range != null) list.Add(new Disp_Config { Nombre = kvp.Key, Valor = (int)range.FirstCell().GetDouble() });
                        }
                    }
                }
                catch (Exception ex) { _logService.Write($"[DATA-SERVICE] Error leyendo límites N_MAX: {ex.Message}", true); }
                return list;
            });
        }



        // ==================================================================================================================
        /// <summary>
        /// Metodo para crear una instancia vacía de un dispositivo genérico basado en la categoría de dispositivo. 
        /// Utiliza reflexión para encontrar la clase de modelo correspondiente y crear una instancia de ella.
        /// </summary>
        public IDevice CreateEmptyDispData(ConfigDeviceCategory cat)
        {
            Type t = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(x => x.Name == cat.ModelClass);
            if (t == null) throw new Exception($"Clase de Modelo '{cat.ModelClass}' no encontrada en el ensamblado.");
            return (IDevice)Activator.CreateInstance(t);
        }


    }


}