using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;
using Siemens.Engineering.Compiler;

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

        #endregion
    }
}