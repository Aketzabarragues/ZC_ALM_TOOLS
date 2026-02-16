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
        /// <param name="nombreTabla">Nombre de la tabla de variables.</param>
        /// <param name="nombreConstante">Nombre de la constante de dimensionado.</param>
        /// <param name="nuevoValor">Valor de dimensionado procedente del Excel.</param>
        public void SincronizarDimensionGlobal(string nombreTabla, string nombreConstante, int nuevoValor)
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
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Fallo en Sincronización Global: {ex.Message}", true);
                throw;
            }
        }

        #endregion

        #region COMPILACIÓN (PASO B)

        /// <summary>
        /// Paso B: Compila el software del PLC para aplicar cambios estructurales (redimensionado de DBs).
        /// </summary>
        public void CompilarSoftware()
        {
            
        }


        /// <summary>
        /// Ejecuta la compilación de un bloque específico mediante el servicio ICompilable.
        /// Es necesario tras cambios estructurales para que los DBs se actualicen.
        /// </summary>
        /// <param name="nombreBloque">Nombre del bloque a compilar (ej. DB2010_V).</param>
        public void CompilarBloque(string nombreBloque)
        {
            try
            {
                LogService.Write($"[TIA] Buscando bloque '{nombreBloque}' para compilación...");

                // 1. Buscamos el bloque (en este caso será un DataBlock)
                var bloque = _plcSoftware.BlockGroup.Blocks.Find(nombreBloque);

                if (bloque == null)
                {
                    // Si no está en la raíz, busca en todos los grupos de usuario
                    bloque = _plcSoftware.BlockGroup.Groups
                        .SelectMany(g => g.Blocks)
                        .FirstOrDefault(b => b.Name == nombreBloque);
                }


                if (bloque == null)
                {
                    LogService.Write($"[TIA-ERROR] No se encontró el bloque '{nombreBloque}' para compilar.", true);
                    return;
                }


                if (bloque != null)
                {
                    // 2. SEGÚN EL MANUAL: Usar ICompilable en lugar de ICompilerService
                    ICompilable compileService = bloque.GetService<ICompilable>();

                    if (compileService != null)
                    {
                        LogService.Write($"[TIA] Iniciando compilación de: {nombreBloque}...");

                        // 3. Ejecutar compilación
                        CompilerResult result = compileService.Compile();

                        LogService.Write($"[TIA] Resultado: {result.State}. Errores: {result.ErrorCount}, Avisos: {result.WarningCount}");

                        if (result.State == CompilerResultState.Error)
                        {
                            throw new Exception($"La compilación de {nombreBloque} falló.");
                        }
                    }
                    else
                    {
                        LogService.Write($"[TIA-ERROR] El bloque '{nombreBloque}' no permite compilación (ICompilable no disponible).", true);
                    }
                }
                else
                {
                    LogService.Write($"[TIA-ERROR] No se encontró el bloque '{nombreBloque}' en la carpeta raíz.", true);
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Fallo al compilar bloque: {ex.Message}", true);
                throw;
            }
        }


        #endregion

        #region MÉTODOS DE TABLAS Y EXPORTACIÓN












        /// <summary>
        /// Exporta una tabla de variables de TIA Portal a un archivo XML.
        /// </summary>
        /// <param name="nombreGrupo">Nombre de la carpeta de variables (opcional).</param>
        /// <param name="nombreTabla">Nombre de la tabla de variables.</param>
        /// <param name="rutaDestino">Ruta física donde se guardará el XML.</param>
        public void ExportarTablaVariables(string nombreCarpeta, string nombreTabla, string rutaXml)
        {
            try
            {
                LogService.Write($"[TIA] Exportando tabla '{nombreTabla}'...");

                if (File.Exists(rutaXml)) File.Delete(rutaXml);

                var tabla = BuscarTablaEnCarpeta(nombreCarpeta, nombreTabla);
                if (tabla == null) throw new Exception($"No se encuentra la tabla '{nombreTabla}' en '{nombreCarpeta}'");

                tabla.Export(new FileInfo(rutaXml), Siemens.Engineering.ExportOptions.WithDefaults);
                LogService.Write($"[TIA] Exportación exitosa.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Error exportando: {ex.Message}", true);
                throw;
            }
        }





        /// <summary>
        /// Crea, renombra o elimina constantes de usuario en el PLC basándose en la lista del Excel.
        /// </summary>
        /// <param name="nombreGrupo">Carpeta de destino en TIA Portal.</param>
        /// <param name="nombreTabla">Tabla de variables de destino.</param>
        /// <param name="dispositivos">Lista de dispositivos sincronizados desde el Excel.</param>
        public void SincronizarConstantesConExcel(string nombreCarpeta, string nombreTabla, List<IDispositivo> listaExcel)
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
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-FATAL] Error en Sync: {ex.Message}", true);
                throw;
            }
        }




        /// <summary>
        /// Realiza una cirugía XML sobre un Bloque de Datos (DB) para inyectar descripciones en los elementos de un Array.
        /// </summary>
        /// <param name="nombreDb">Nombre del DB (ej. DB2010_V).</param>
        /// <param name="nombreArray">Nombre del Array dentro del DB (ej. V).</param>
        /// <param name="dispositivos">Lista de dispositivos con las descripciones del Excel.</param>
        /// <remarks>
        /// Este método exporta el bloque, modifica el XML inyectando nodos 'Subelement' con 'Comment' 
        /// y re-importa el bloque en su carpeta original.
        /// </remarks>
        public void SincronizarComentariosDB(string nombreDb, string nombreArray, List<IDispositivo> dispositivos)
        {
            try
            {
                // 1. Localizar el DB en TIA Portal
                // 1. Buscamos el bloque genérico (en raíz o primer nivel de carpetas)
                var bloqueGenerico = _plcSoftware.BlockGroup.Blocks.Find(nombreDb)
                                     ?? _plcSoftware.BlockGroup.Groups.SelectMany(g => g.Blocks).FirstOrDefault(b => b.Name == nombreDb);

                // 2. Intentamos el cast a GlobalDB
                var db = bloqueGenerico as GlobalDB;

                if (db == null)
                {
                    // Esto te ayudará a saber si el error es que NO EXISTE o que NO ES un GlobalDB
                    LogService.Write($"[TIA-ERROR] No se pudo encontrar o castear el bloque: {nombreDb}", true);
                    return;
                }



                // 2. Exportar a carpeta Temp
                string rutaXml = Path.Combine(AppConfigManager.TempPath, $"{nombreDb}.xml");

                if (File.Exists(rutaXml))
                {
                    File.Delete(rutaXml);
                    LogService.Write($"[TIA-XML] Archivo previo borrado: {nombreDb}.xml");
                }

                LogService.Write($"[TIA-XML] Exportando {nombreDb} para cirugía de comentarios...");
                db.Export(new FileInfo(rutaXml), ExportOptions.WithDefaults);

                // 3. Modificar el XML (La cirugía)
                LogService.Write($"[TIA-XML] Inyectando {dispositivos.Count} comentarios en el array '{nombreArray}'...");
                XDocument doc = XDocument.Load(rutaXml);
                XNamespace ns = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

                // 1. Buscamos primero la sección "Static" (donde están tus variables principales)
                var sectionStatic = doc.Descendants(ns + "Section")
                                       .FirstOrDefault(s => s.Attribute("Name")?.Value == "Static");

                if (sectionStatic == null) throw new Exception("No se encontró la sección 'Static' en el DB.");

                // 2. Buscamos el miembro que sea hijo DIRECTO de Static (evita los hijos de Mux)
                var miembroArray = sectionStatic.Elements(ns + "Member")
                                                .FirstOrDefault(m => m.Attribute("Name")?.Value == nombreArray);

                if (miembroArray != null)
                {
                    foreach (var disp in dispositivos)
                    {
                        // Buscamos si existe el subelemento para este índice
                        var subelement = miembroArray.Elements(ns + "Subelement")
                                                     .FirstOrDefault(s => s.Attribute("Path")?.Value == disp.Numero.ToString());

                        if (subelement == null)
                        {
                            subelement = new XElement(ns + "Subelement", new XAttribute("Path", disp.Numero.ToString()));
                            miembroArray.Add(subelement);
                        }

                        // Inyectamos el comentario
                        subelement.Elements(ns + "Comment").Remove();
                        subelement.Add(
                            new XElement(ns + "Comment",
                                new XElement(ns + "MultiLanguageText",
                                    new XAttribute("Lang", "es-ES"),
                                    disp.Tag + " - " + disp.Descripcion // <--- Tu propiedad del modelo
                                )
                            )
                        );
                    }
                    doc.Save(rutaXml);
                }

                // 4. Importar de vuelta
                LogService.Write($"[TIA-XML] Re-importando {nombreDb} con comentarios actualizados...");




                // Obtenemos el contenedor(Composition) donde vive el bloque actualmente
                // El 'Parent' de un bloque es la colección 'Blocks' de la carpeta donde reside.
                var padre = bloqueGenerico.Parent;

                if (padre is PlcBlockUserGroup carpeta)
                {
                    // Si el bloque estaba en una carpeta, lo importamos en su colección de bloques
                    carpeta.Blocks.Import(new FileInfo(rutaXml), ImportOptions.Override);
                    LogService.Write($"[TIA-XML] Bloque actualizado en la carpeta: {carpeta.Name}");
                }
                else if (padre is PlcBlockGroup raiz)
                {
                    // Si el bloque estaba en la raíz (Program blocks)
                    raiz.Blocks.Import(new FileInfo(rutaXml), ImportOptions.Override);
                    LogService.Write("[TIA-XML] Bloque actualizado en la raíz de Program Blocks.");
                }
                else
                {
                    // Fallback por seguridad si no se detecta el tipo de contenedor
                    _plcSoftware.BlockGroup.Blocks.Import(new FileInfo(rutaXml), ImportOptions.Override);
                    LogService.Write("[TIA-XML] Advertencia: Importado en la raíz por defecto.");
                }

                LogService.Write($"[TIA-XML] Proceso finalizado con éxito para {nombreDb}.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Error en cirugía XML: {ex.Message}", true);
                throw;
            }
        }










        #endregion

        #region HELPERS INTERNOS

        /// <summary>
        /// Busca una tabla de variables en la raíz o en carpetas de primer nivel.
        /// </summary>
        /// <param name="nombre">Nombre de la tabla.</param>
        /// <returns>La tabla encontrada o null.</returns>
        private PlcTagTable BuscarTabla(string nombre)
        {
            // Busca en la raíz y en todos los grupos
            var tablaRaiz = _plcSoftware.TagTableGroup.TagTables.Find(nombre);
            if (tablaRaiz != null) return tablaRaiz;

            return _plcSoftware.TagTableGroup.Groups.SelectMany(g => g.TagTables).FirstOrDefault(t => t.Name == nombre);
        }

        /// <summary>
        /// Busca una tabla de variables dentro de una carpeta específica.
        /// </summary>
        /// <param name="carpeta">Nombre de la carpeta.</param>
        /// <param name="tabla">Nombre de la tabla.</param>
        /// <returns>La tabla encontrada o null.</returns>
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

        /// <summary>
        /// Helper para enviar notificaciones de estado a la interfaz de usuario.
        /// </summary>
        /// <param name="msg">Mensaje a mostrar.</param>
        /// <param name="esError">Si es verdadero, se marca como error.</param>
        private void EnviarEstado(string msg, bool esError = false)
        {
            OnStatusChanged?.Invoke(msg, esError);
        }


        /// <summary>
        /// Busca un bloque de forma recursiva navegando por todas las subcarpetas de bloques del PLC.
        /// </summary>
        /// <param name="group">Grupo de bloques donde iniciar la búsqueda.</param>
        /// <param name="nombre">Nombre del bloque buscado.</param>
        /// <returns>El bloque encontrado o null.</returns>
        private PlcBlock BuscarBloqueRecursivo(PlcBlockUserGroup group, string nombre)
        {
            // 1. Buscar en la carpeta actual
            var bloque = group.Blocks.Find(nombre);
            if (bloque != null) return bloque;

            // 2. Si no está, buscar en cada una de las subcarpetas
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