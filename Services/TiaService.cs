using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;

public class TiaService
{
    private readonly PlcSoftware _plcSoftware;

    // Este es nuestro "altavoz". Cualquier cosa que se suscriba recibirá los mensajes.
    public Action<string, bool> OnStatusChanged { get; set; }

    public TiaService(PlcSoftware plcSoftware)
    {
        _plcSoftware = plcSoftware;
    }

    public void ExportarTablaVariables(string nombreCarpeta, string nombreTabla, string rutaDestino)
    {
        try
        {
            // 1. Asegurar limpieza del archivo destino para evitar conflictos de acceso
            if (File.Exists(rutaDestino))
            {
                File.Delete(rutaDestino);
            }

            string directorio = Path.GetDirectoryName(rutaDestino);
            if (!Directory.Exists(directorio)) Directory.CreateDirectory(directorio);

            // 2. Navegación
            var grupo = _plcSoftware.TagTableGroup.Groups.Find(nombreCarpeta);
            if (grupo == null) throw new Exception($"Carpeta {nombreCarpeta} no encontrada.");

            var tabla = grupo.TagTables.Find(nombreTabla);
            if (tabla == null) throw new Exception($"Tabla {nombreTabla} no encontrada.");

            // 3. Exportación
            EnviarEstado($"Exportando {nombreTabla} a XML...");
            tabla.Export(new FileInfo(rutaDestino), ExportOptions.WithDefaults);
        }
        catch (Exception ex)
        {
            EnviarEstado($"Error en exportación limpia: {ex.Message}", true);
            throw;
        }
    }


    public void SincronizarConstantesConExcel(string nombreCarpeta, string nombreTabla, List<object> listaExcel)
    {
        try
        {
            int totalExcel = listaExcel.Count;
            EnviarEstado($"Iniciando sincronización: {totalExcel} dispositivos en Excel.");

            var grupo = _plcSoftware.TagTableGroup.Groups.Find(nombreCarpeta);
            var tabla = grupo.TagTables.Find(nombreTabla);

            // --- FASE 1: IGUALAR CANTIDAD (Eliminar sobrantes) ---
            // Buscamos constantes cuyo valor sea mayor al número de elementos en Excel
            var constantesAEliminar = new List<PlcUserConstant>();

            foreach (var constante in tabla.UserConstants)
            {
                if (int.TryParse(constante.Value, out int valorActual))
                {
                    if (valorActual > totalExcel)
                    {
                        constantesAEliminar.Add(constante);
                    }
                }
            }

            if (constantesAEliminar.Count > 0)
            {
                EnviarEstado($"Eliminando {constantesAEliminar.Count} variables sobrantes en TIA...");
                foreach (var c in constantesAEliminar) c.Delete();
            }

            // --- FASE 2: ACTUALIZAR NOMBRES EXISTENTES ---
            foreach (var item in listaExcel)
            {
                // Casteamos al tipo base (usamos dynamic o interfaces si prefieres)
                // Aquí asumo que todos los modelos tienen 'Numero' y 'Tag'
                dynamic dispositivo = item;
                int num = dispositivo.Numero;
                string nuevoTag = dispositivo.Tag;

                // Buscamos la constante que corresponde a este número de dispositivo
                PlcUserConstant constanteTIA = null;
                foreach (var c in tabla.UserConstants)
                {
                    if (int.TryParse(c.Value, out int v) && v == num)
                    {
                        constanteTIA = c;
                        break;
                    }
                }

                if (constanteTIA != null)
                {
                    if (constanteTIA.Name != nuevoTag)
                    {
                        constanteTIA.Name = nuevoTag;
                    }
                }
                else
                {
                    // --- FASE 3: CREAR SI NO EXISTE ---
                    // Si el Excel tiene 15 y TIA tenía 10, aquí creamos las 5 nuevas
                    tabla.UserConstants.Create(nuevoTag, "Int", num.ToString());
                }
            }

            EnviarEstado("Sincronización finalizada con éxito.");
        }
        catch (Exception ex)
        {
            EnviarEstado($"Error sincronizando: {ex.Message}", true);
            throw;
        }
    }


    public Dictionary<int, string> LeerDiccionarioDesdeXml(string rutaXml)
    {
        var diccionarioPlc = new Dictionary<int, string>();

        if (!File.Exists(rutaXml)) return diccionarioPlc;

        try
        {
            XDocument doc = XDocument.Load(rutaXml);

            // Buscamos los nodos de constantes de usuario
            // Usamos LocalName para evitar problemas con los namespaces de Siemens
            var constantes = doc.Descendants().Where(x => x.Name.LocalName == "PlcUserConstant");

            foreach (var con in constantes)
            {
                var attrList = con.Element(con.Name.Namespace + "AttributeList");
                if (attrList != null)
                {
                    string nombre = attrList.Element(con.Name.Namespace + "Name")?.Value;
                    string valorStr = attrList.Element(con.Name.Namespace + "Value")?.Value;

                    if (int.TryParse(valorStr, out int valor))
                    {
                        // Guardamos: Llave = Valor (Número), Contenido = Nombre (Tag)
                        if (!diccionarioPlc.ContainsKey(valor))
                            diccionarioPlc.Add(valor, nombre);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception("Error al procesar el XML de TIA Portal: " + ex.Message);
        }

        return diccionarioPlc;
    }



    private void EnviarEstado(string msj, bool esError = false)
    {
        // Si alguien está escuchando (suscríbete en el VM), le mandamos el mensaje
        OnStatusChanged?.Invoke(msj, esError);
    }
}