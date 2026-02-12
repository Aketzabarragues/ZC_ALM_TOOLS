using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using ZC_ALM_TOOLS.Models;

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


    public void SincronizarConstantesConExcel(string nombreCarpeta, string nombreTabla, List<IDispositivo> listaExcel)
    {
        try
        {
            int totalExcel = listaExcel.Count;
            EnviarEstado($"Iniciando sincronización completa: {totalExcel} dispositivos.");

            var grupo = _plcSoftware.TagTableGroup.Groups.Find(nombreCarpeta);
            var tabla = grupo.TagTables.Find(nombreTabla);

            // --- FASE 1: IGUALAR CANTIDAD (Eliminar sobrantes) ---
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
                EnviarEstado($"Eliminando {constantesAEliminar.Count} variables sobrantes...");
                foreach (var c in constantesAEliminar) c.Delete();
            }

            // --- FASE 2: ACTUALIZAR O CREAR Y AÑADIR COMENTARIOS ---
            foreach (var disp in listaExcel)
            {
                // Buscamos la constante por su valor numérico
                PlcUserConstant constanteTIA = tabla.UserConstants.FirstOrDefault(c => c.Value == disp.Numero.ToString());

                if (constanteTIA == null)
                {
                    // Si no existe, la creamos
                    constanteTIA = tabla.UserConstants.Create(disp.CPTag, "Int", disp.Numero.ToString());
                }

                // Actualizamos el nombre si es diferente
                if (constanteTIA.Name != disp.CPTag)
                {
                    constanteTIA.Name = disp.CPTag;
                }


                // 3. Sincronizar Comentarios (Solución al error get_Culture)
                // Recorremos todos los idiomas que existan para esta constante (es-ES, en-GB, etc.)
                // y les asignamos el comentario del Excel sin preguntar qué idioma es.
                foreach (var item in constanteTIA.Comment.Items)
                {
                    try
                    {
                        // Intentamos asignar el texto directamente
                        item.Text = disp.CPComentario;
                    }
                    catch
                    {
                        // Si la propiedad .Text fallara, usamos SetAttribute como plan B
                        item.SetAttribute("Text", disp.CPComentario);
                    }
                }
            }

            EnviarEstado("Sincronización de nombres y descripciones finalizada.");
        }
        catch (Exception ex)
        {
            EnviarEstado($"Error en sincronización: {ex.Message}", true);
            throw;
        }
    }


    public Dictionary<int, string> LeerDiccionarioDesdeXml(string rutaXml, StringBuilder log)
    {
        var diccionarioPlc = new Dictionary<int, string>();
        if (!File.Exists(rutaXml))
        {
            MessageBox.Show($"ERROR: El archivo XML no existe en: {rutaXml}");
            return diccionarioPlc;
        }

        try
        {
            XDocument doc = XDocument.Load(rutaXml);
            var constantes = doc.Descendants().Where(x => x.Name.LocalName == "PlcUserConstant").ToList();

            // DEBUG 1: ¿Cuántas etiquetas ha encontrado?
            MessageBox.Show($"Debug XML: Se han encontrado {constantes.Count} nodos 'PlcUserConstant'.");

            foreach (var con in constantes)
            {
                var attrList = con.Elements().FirstOrDefault(x => x.Name.LocalName == "AttributeList");
                if (attrList != null)
                {
                    string nombre = attrList.Elements().FirstOrDefault(x => x.Name.LocalName == "Name")?.Value;
                    string valorStr = attrList.Elements().FirstOrDefault(x => x.Name.LocalName == "Value")?.Value;

                    if (int.TryParse(valorStr, out int valor))
                    {
                        if (!diccionarioPlc.ContainsKey(valor))
                            diccionarioPlc.Add(valor, nombre);
                    }
                }
            }

            // DEBUG 2: ¿Cuántos elementos hay en el diccionario final?
            MessageBox.Show($"Debug Diccionario: Se han cargado {diccionarioPlc.Count} elementos en memoria.");

            // Ver el primer elemento para confirmar formato
            if (diccionarioPlc.Count > 0)
            {
                var primero = diccionarioPlc.First();
                MessageBox.Show($"Primer elemento en PLC: ID={primero.Key}, Tag='{primero.Value}'");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error Debug XML: " + ex.Message);
        }

        return diccionarioPlc;
    }



    private void EnviarEstado(string msj, bool esError = false)
    {
        // Si alguien está escuchando (suscríbete en el VM), le mandamos el mensaje
        OnStatusChanged?.Invoke(msj, esError);
    }
}