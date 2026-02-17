using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;


namespace ZC_ALM_TOOLS.Core
{
    /* * RESUMEN DE FUNCIONAMIENTO - TIA SERVICE:
     * 1. INTERFAZ CON OPENNESS: Conexión directa con la API de Siemens.
     * 2. GESTIÓN DE CONFIGURACIÓN: Sincroniza constantes globales (N_MAX) que definen tamaños de arrays.
     * 3. COMPILACIÓN: Fuerza al PLC a regenerar bloques tras cambios estructurales.
     * 4. SINCRONIZACIÓN QUIRÚRGICA: Gestión de constantes de dispositivos (ID -> Nombre).
     */

    /// <summary>
    /// Servicio encargado de la comunicación directa con la API de Siemens Openness.
    /// Proporciona métodos para leer/escribir constantes, compilar bloques, exportar tablas
    /// y realizar modificaciones avanzadas en bloques de datos mediante XML.
    /// </summary>
    public class TiaService
    {
        /// <summary>Referencia al software del PLC seleccionado en el proyecto de TIA Portal.</summary>
        private readonly PlcSoftware _plcSoftware;

        /// <summary>Acción que se dispara para notificar cambios de estado o errores a la capa superior (UI).</summary>
        public Action<string, bool> OnStatusChanged { get; set; }

        /// <summary>
        /// Inicializa una nueva instancia de <see cref="TiaService"/>.
        /// </summary>
        /// <param name="plcSoftware">Objeto PlcSoftware del PLC activo.</param>
        public TiaService(PlcSoftware plcSoftware)
        {
            _plcSoftware = plcSoftware;
        }

        #region GESTIÓN DE CONSTANTES GLOBALES (DIMENSIONADO)

        /// <summary>
        /// Busca una constante de usuario en una tabla específica y devuelve su valor entero.
        /// </summary>
        /// <param name="nombreTabla">Nombre de la tabla de variables donde buscar.</param>
        /// <param name="nombreConstante">Nombre de la constante a leer.</param>
        /// <returns>El valor de la constante como entero, o 0 si no se encuentra.</returns>
        public int ObtenerValorConstante(string nombreTabla, string nombreConstante)
        {
            try
            {
                var tabla = BuscarTabla(nombreTabla);
                if (tabla == null) return -1;

                var constante = tabla.UserConstants.Find(nombreConstante);
                if (constante != null && int.TryParse(constante.Value, out int valor))
                {
                    return valor;
                }
                return 0;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Sincroniza el valor de una constante de dimensión (ej. N_MAX) en el PLC.
        /// Si el valor es diferente al actual, lo actualiza.
        /// </summary>
        public bool SincronizarDimensionGlobal(string nombreTabla, string nombreConstante, int nuevoValor)
        {
            try
            {
                LogService.Write($"[TIA] Verificando constante de dimensionado: {nombreConstante}...");

                var tabla = BuscarTabla(nombreTabla);
                if (tabla == null) throw new Exception($"No se encontró la tabla '{nombreTabla}'");

                var constante = tabla.UserConstants.Find(nombreConstante);
                if (constante == null) throw new Exception($"No existe la constante '{nombreConstante}' en el PLC.");

                if (int.TryParse(constante.Value, out int valorActual))
                {
                    if (valorActual != nuevoValor)
                    {
                        LogService.Write($"[TIA-CONFIG] Modificando {nombreConstante}: {valorActual} -> {nuevoValor}");
                        constante.Value = nuevoValor.ToString();
                        EnviarEstado($"{nombreConstante} actualizado a {nuevoValor}.");
                    }
                    else
                    {
                        LogService.Write($"[TIA] La dimensión {nombreConstante} ya coincide ({nuevoValor}).");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Fallo en Sincronización Global: {ex.Message}", true);
                return false;
            }
        }

        #endregion

        #region COMPILACIÓN (PASO B)

        public void CompilarSoftware()
        {
            // Implementación futura
        }


        /// <summary>
        /// Ejecuta la compilación de un bloque específico mediante el servicio ICompilable.
        /// </summary>
        public bool CompilarBloque(string nombreBloque)
        {
            try
            {
                LogService.Write($"[TIA] Buscando bloque '{nombreBloque}' para compilación...");

                var bloque = _plcSoftware.BlockGroup.Blocks.Find(nombreBloque)
                             ?? _plcSoftware.BlockGroup.Groups.SelectMany(g => g.Blocks).FirstOrDefault(b => b.Name == nombreBloque);

                if (bloque == null)
                {
                    LogService.Write($"[TIA-ERROR] No se encontró el bloque '{nombreBloque}' para compilar.", true);
                    return false;
                }

                ICompilable compileService = bloque.GetService<ICompilable>();

                if (compileService != null)
                {
                    LogService.Write($"[TIA] Iniciando compilación de: {nombreBloque}...");
                    CompilerResult result = compileService.Compile();

                    LogService.Write($"[TIA] Resultado: {result.State}. Errores: {result.ErrorCount}, Avisos: {result.WarningCount}");

                    return result.State != CompilerResultState.Error;
                }
                else
                {
                    LogService.Write($"[TIA-ERROR] El bloque '{nombreBloque}' no permite compilación.", true);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Fallo al compilar bloque: {ex.Message}", true);
                return false;
            }
        }


        #endregion

        #region MÉTODOS DE TABLAS Y EXPORTACIÓN

        /// <summary>
        /// Exporta una tabla de variables de TIA Portal a un archivo XML.
        /// </summary>
        public bool ExportarTablaVariables(string nombreCarpeta, string nombreTabla, string rutaXml)
        {
            try
            {
                LogService.Write($"[TIA] Exportando tabla '{nombreTabla}'...");

                if (File.Exists(rutaXml)) File.Delete(rutaXml);

                var tabla = BuscarTablaEnCarpeta(nombreCarpeta, nombreTabla);
                if (tabla == null) throw new Exception($"No se encuentra la tabla '{nombreTabla}' en '{nombreCarpeta}'");

                tabla.Export(new FileInfo(rutaXml), Siemens.Engineering.ExportOptions.WithDefaults);
                LogService.Write($"[TIA] Exportación exitosa.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Error exportando: {ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// Crea, renombra o elimina constantes de usuario en el PLC basándose en la lista del Excel.
        /// </summary>
        public bool SincronizarConstantesConExcel(string nombreCarpeta, string nombreTabla, List<IDevice> listaExcel)
        {
            try
            {
                LogService.Write($"[TIA] === SINCRONIZANDO DISPOSITIVOS: {nombreTabla} ===");
                var tabla = BuscarTablaEnCarpeta(nombreCarpeta, nombreTabla);
                if (tabla == null) throw new Exception($"La tabla '{nombreTabla}' no existe.");

                var idsEnExcel = new HashSet<int>(listaExcel.Select(d => d.Numero));
                var constantesAEliminar = tabla.UserConstants.Where(c => int.TryParse(c.Value, out int id) && !idsEnExcel.Contains(id)).ToList();

                foreach (var c in constantesAEliminar)
                {
                    LogService.Write($"[TIA-DELETE] Borrando ID {c.Value}: {c.Name}");
                    c.Delete();
                }

                foreach (var disp in listaExcel)
                {
                    PlcUserConstant constanteTIA = tabla.UserConstants.FirstOrDefault(c => c.Value == disp.Numero.ToString());

                    if (constanteTIA == null)
                    {
                        LogService.Write($"[TIA-CREATE] Creando ID {disp.Numero}: {disp.CPTag}");
                        constanteTIA = tabla.UserConstants.Create(disp.CPTag, "Int", disp.Numero.ToString());
                    }

                    if (constanteTIA.Name != disp.CPTag)
                    {
                        LogService.Write($"[TIA-RENAME] ID {disp.Numero}: {constanteTIA.Name} -> {disp.CPTag}");
                        constanteTIA.Name = disp.CPTag;
                    }

                    ActualizarComentarios(constanteTIA, disp.CPComentario);
                }
                EnviarEstado("Sincronización de dispositivos finalizada.");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-FATAL] Error en Sync: {ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// Realiza una cirugía XML sobre un Bloque de Datos (DB) para inyectar descripciones.
        /// </summary>
        public bool SincronizarComentariosDB(string nombreDb, string nombreArray, List<IDevice> dispositivos)
        {
            try
            {
                var bloqueGenerico = _plcSoftware.BlockGroup.Blocks.Find(nombreDb)
                                     ?? _plcSoftware.BlockGroup.Groups.SelectMany(g => g.Blocks).FirstOrDefault(b => b.Name == nombreDb);

                var db = bloqueGenerico as GlobalDB;
                if (db == null)
                {
                    LogService.Write($"[TIA-ERROR] No se pudo encontrar o castear el bloque: {nombreDb}", true);
                    return false;
                }

                string rutaXml = Path.Combine(AppConfigManager.TempPath, $"{nombreDb}.xml");
                if (File.Exists(rutaXml)) File.Delete(rutaXml);

                LogService.Write($"[TIA-XML] Exportando {nombreDb} para cirugía...");
                db.Export(new FileInfo(rutaXml), ExportOptions.WithDefaults);

                XDocument doc = XDocument.Load(rutaXml);
                XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

                var sectionStatic = doc.Descendants(ns + "Section").FirstOrDefault(s => s.Attribute("Name")?.Value == "Static");
                if (sectionStatic == null) throw new Exception("No se encontró la sección 'Static' en el DB.");

                var miembroArray = sectionStatic.Elements(ns + "Member").FirstOrDefault(m => m.Attribute("Name")?.Value == nombreArray);

                if (miembroArray != null)
                {
                    foreach (var disp in dispositivos)
                    {
                        var subelement = miembroArray.Elements(ns + "Subelement").FirstOrDefault(s => s.Attribute("Path")?.Value == disp.Numero.ToString());

                        if (subelement == null)
                        {
                            subelement = new XElement(ns + "Subelement", new XAttribute("Path", disp.Numero.ToString()));
                            miembroArray.Add(subelement);
                        }

                        subelement.Elements(ns + "Comment").Remove();
                        subelement.Add(new XElement(ns + "Comment",
                            new XElement(ns + "MultiLanguageText",
                                new XAttribute("Lang", "es-ES"),
                                disp.Tag + " - " + disp.Descripcion)));
                    }
                    doc.Save(rutaXml);
                }

                LogService.Write($"[TIA-XML] Re-importando {nombreDb}...");
                var padre = bloqueGenerico.Parent;
                if (padre is PlcBlockUserGroup carpeta)
                    carpeta.Blocks.Import(new FileInfo(rutaXml), ImportOptions.Override);
                else if (padre is PlcBlockGroup raiz)
                    raiz.Blocks.Import(new FileInfo(rutaXml), ImportOptions.Override);

                return true;
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Error en cirugía XML: {ex.Message}", true);
                return false;
            }
        }

        #endregion

        #region HELPERS INTERNOS

        private PlcTagTable BuscarTabla(string nombre)
        {
            var tablaRaiz = _plcSoftware.TagTableGroup.TagTables.Find(nombre);
            if (tablaRaiz != null) return tablaRaiz;

            return _plcSoftware.TagTableGroup.Groups.SelectMany(g => g.TagTables).FirstOrDefault(t => t.Name == nombre);
        }

        private PlcTagTable BuscarTablaEnCarpeta(string carpeta, string tabla)
        {
            var grupo = _plcSoftware.TagTableGroup.Groups.Find(carpeta);
            return grupo?.TagTables.Find(tabla);
        }

        private void ActualizarComentarios(PlcUserConstant constante, string comentario)
        {
            foreach (var item in constante.Comment.Items)
            {
                try { item.Text = comentario; }
                catch { item.SetAttribute("Text", comentario); }
            }
        }

        private void EnviarEstado(string msg, bool esError = false)
        {
            OnStatusChanged?.Invoke(msg, esError);
        }

        private PlcBlock BuscarBloqueRecursivo(PlcBlockUserGroup group, string nombre)
        {
            var bloque = group.Blocks.Find(nombre);
            if (bloque != null) return bloque;

            foreach (var subCarpeta in group.Groups)
            {
                var encontrado = BuscarBloqueRecursivo(subCarpeta, nombre);
                if (encontrado != null) return encontrado;
            }
            return null;
        }

        #endregion
    }
}