using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using ZC_ALM_TOOLS.Models;

namespace ZC_ALM_TOOLS.Services
{
    // Servicio encargado exclusivamente de leer los XML generados por Python
    // y convertirlos en objetos C# usando los Modelos definidos.
    public static class DataService
    {
        // Carga la configuración global (N_MAX) desde config_disp.xml
        public static Dictionary<string, int> LoadGlobalConfig(string path)
        {
            var config = new Dictionary<string, int>();

            if (!File.Exists(path))
            {
                LogService.Write($"[DATA] No se encuentra config global: {path}", true);
                return config;
            }

            try
            {
                XDocument doc = XDocument.Load(path);
                // Usamos Nombre y Valor porque así lo definimos en el modelo Disp_Config
                config = doc.Descendants("Item")
                            .Select(x => Disp_Config.FromXml(x))
                            .ToDictionary(c => c.Nombre, c => c.Valor);
            }
            catch (Exception ex)
            {
                LogService.Write($"[DATA] Error leyendo config global: {ex.Message}", true);
            }

            return config;
        }

        // Carga una lista de dispositivos usando Reflexión para instanciar la clase correcta (Disp_V, Disp_M, etc.)
        public static List<object> LoadDispCategoryData(string path, DeviceCategory category)
        {
            var list = new List<object>();

            if (!File.Exists(path)) return list;

            try
            {
                XDocument doc = XDocument.Load(path);

                // Construimos el nombre de la clase dinámicamente
                string className = $"ZC_ALM_TOOLS.Models.{category.ModelClass}";
                Type modelType = Type.GetType(className);

                if (modelType == null)
                {
                    LogService.Write($"[DATA] Error: No se encuentra la clase {className}", true);
                    return list;
                }

                // Buscamos el método estático "FromXml"
                var method = modelType.GetMethod("FromXml", BindingFlags.Public | BindingFlags.Static);

                if (method == null)
                {
                    LogService.Write($"[DATA] Error: La clase {category.ModelClass} no tiene método FromXml", true);
                    return list;
                }

                foreach (var element in doc.Root.Elements())
                {
                    // Invocamos el método estático para crear el objeto
                    var instance = method.Invoke(null, new object[] { element });
                    if (instance != null)
                    {
                        list.Add(instance);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"[DATA] Error cargando {category.ModelClass}: {ex.Message}", true);
            }

            return list;
        }

        // Crea una instancia vacía de un modelo (Disp_ED, Disp_V, etc.) basado en su categoría
        public static IDevice CreateEmptyDispData(DeviceCategory category)
        {
            try
            {
                string className = $"ZC_ALM_TOOLS.Models.{category.ModelClass}";
                Type modelType = Type.GetType(className);

                if (modelType == null)
                {
                    // Intento de búsqueda profunda en los ensamblados si el GetType básico falla
                    modelType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.Name == category.ModelClass);
                }

                if (modelType == null) throw new Exception($"Tipo no encontrado: {category.ModelClass}");

                // Creamos la instancia y la devolvemos como la interfaz común
                return (IDevice)Activator.CreateInstance(modelType);
            }
            catch (Exception ex)
            {
                LogService.Write($"[DATA] Error creando instancia de {category.ModelClass}: {ex.Message}", true);
                // Devolvemos un objeto básico para no romper la ejecución
                return new ZC_ALM_TOOLS.Models.Disp_ED();
            }
        }
    }
}