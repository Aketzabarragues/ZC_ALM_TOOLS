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

    public class TiaService
    {
        private readonly PlcSoftware _plcSoftware;

        public Action<string, bool> OnStatusChanged { get; set; }

        public TiaService(PlcSoftware plcSoftware)
        {
            _plcSoftware = plcSoftware;
        }

        #region GESTIÓN DE CONSTANTES GLOBALES (DIMENSIONADO)

        /// <summary>
        /// Busca una constante en una tabla específica y devuelve su valor entero.
        /// Útil para refrescar la UI (Excel vs PLC).
        /// </summary>
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
        /// Paso A: Sincroniza el valor de una constante global (ej. N_MAX_V).
        /// </summary>
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

        private PlcTagTable BuscarTabla(string nombre)
        {
            // Busca en la raíz y en todos los grupos
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