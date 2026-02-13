using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using ZC_ALM_TOOLS.Models;
using ZC_ALM_TOOLS.Services;

namespace ZC_ALM_TOOLS.Core
{
    /* * RESUMEN DE FUNCIONAMIENTO - TIA SERVICE:
     * 1. INTERFAZ CON OPENNESS: Es el puente directo entre nuestra App y la API de Siemens TIA Portal.
     * 2. GESTIÓN DE TABLAS: Localiza grupos y tablas de variables dentro del software del PLC.
     * 3. EXPORTACIÓN: Genera archivos XML de las tablas actuales para su posterior comparación.
     * 4. SINCRONIZACIÓN QUIRÚRGICA: 
     * - Identifica y elimina variables en el PLC que no están en el Excel (Limpieza).
     * - Actualiza nombres y comentarios de variables existentes.
     * - Crea las variables nuevas que solo existen en el Excel de ingeniería.
     */

    public class TiaService
    {
        private readonly PlcSoftware _plcSoftware;

        // Delegado para enviar mensajes cortos a la barra de estado de la interfaz (UI)
        public Action<string, bool> OnStatusChanged { get; set; }

        public TiaService(PlcSoftware plcSoftware)
        {
            _plcSoftware = plcSoftware;
        }

        /// <summary>
        /// Exporta una tabla de constantes de usuario a un archivo XML.
        /// </summary>
        public void ExportarTablaVariables(string nombreCarpeta, string nombreTabla, string rutaXml)
        {
            try
            {
                LogService.Write($"[TIA] Intentando exportar tabla '{nombreTabla}' a XML...");

                if (File.Exists(rutaXml))
                {
                    LogService.Write($"[TIA] El archivo ya existe. Borrando '{rutaXml}' para permitir nueva exportación...");
                    File.Delete(rutaXml);
                }

                // Buscamos el grupo (carpeta) y la tabla dentro del PLC
                var grupo = _plcSoftware.TagTableGroup.Groups.Find(nombreCarpeta);
                if (grupo == null) throw new Exception($"No se encuentra la carpeta '{nombreCarpeta}'");

                var tabla = grupo.TagTables.Find(nombreTabla);
                if (tabla == null) throw new Exception($"No se encuentra la tabla '{nombreTabla}'");

                // Exportación mediante Openness (sobreescribe si ya existe)
                tabla.Export(new System.IO.FileInfo(rutaXml), Siemens.Engineering.ExportOptions.WithDefaults);

                LogService.Write($"[TIA] Exportación exitosa: {rutaXml}");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-ERROR] Fallo al exportar tabla: {ex.Message}", true);
                throw;
            }
        }

        /// <summary>
        /// Sincroniza el PLC para que sea un espejo exacto de la lista del Excel.
        /// Borra lo que sobra, actualiza lo que existe y crea lo nuevo.
        /// </summary>
        public void SincronizarConstantesConExcel(string nombreCarpeta, string nombreTabla, List<IDispositivo> listaExcel)
        {
            try
            {
                LogService.Write($"[TIA] === INICIANDO SINCRONIZACIÓN QUIRÚRGICA: {nombreTabla} ===");
                EnviarEstado($"Sincronizando {listaExcel.Count} dispositivos...");

                var grupo = _plcSoftware.TagTableGroup.Groups.Find(nombreCarpeta);
                var tabla = grupo.TagTables.Find(nombreTabla);

                if (tabla == null) throw new Exception($"La tabla '{nombreTabla}' no existe en el proyecto TIA.");

                // --- FASE 1: LIMPIEZA DE SOBRANTES (PLC -> EXCEL) ---
                // Creamos un conjunto de IDs presentes en el Excel para una búsqueda rápida (O(1))
                var idsEnExcel = new HashSet<int>(listaExcel.Select(d => d.Numero));
                var constantesAEliminar = new List<PlcUserConstant>();

                foreach (var constante in tabla.UserConstants)
                {
                    if (int.TryParse(constante.Value, out int idPLC))
                    {
                        // Si el ID del PLC no está en el Excel, el dispositivo ha sido borrado o movido
                        if (!idsEnExcel.Contains(idPLC))
                        {
                            constantesAEliminar.Add(constante);
                        }
                    }
                }

                if (constantesAEliminar.Count > 0)
                {
                    LogService.Write($"[TIA] Se han detectado {constantesAEliminar.Count} variables sobrantes en el PLC. Borrando...");
                    foreach (var c in constantesAEliminar)
                    {
                        LogService.Write($"[TIA-DELETE] Borrando ID {c.Value}: {c.Name}");
                        c.Delete();
                    }
                }

                // --- FASE 2: ACTUALIZACIÓN Y CREACIÓN (EXCEL -> PLC) ---
                foreach (var disp in listaExcel)
                {
                    // Buscamos la constante en el PLC por su valor (ID único de ingeniería)
                    PlcUserConstant constanteTIA = tabla.UserConstants.FirstOrDefault(c => c.Value == disp.Numero.ToString());

                    if (constanteTIA == null)
                    {
                        // Si no existe el ID en el PLC, creamos la constante nueva
                        LogService.Write($"[TIA-CREATE] Creando ID {disp.Numero}: {disp.CPTag}");
                        constanteTIA = tabla.UserConstants.Create(disp.CPTag, "Int", disp.Numero.ToString());
                    }

                    // Sincronizamos el nombre (Tag) si ha cambiado en el Excel
                    if (constanteTIA.Name != disp.CPTag)
                    {
                        LogService.Write($"[TIA-RENAME] ID {disp.Numero}: {constanteTIA.Name} -> {disp.CPTag}");
                        constanteTIA.Name = disp.CPTag;
                    }

                    // Sincronizamos comentarios en todos los idiomas del proyecto TIA Portal
                    ActualizarComentarios(constanteTIA, disp.CPComentario);
                }

                LogService.Write($"[TIA] === SINCRONIZACIÓN FINALIZADA CON ÉXITO ===");
                EnviarEstado("Sincronización finalizada correctamente.");
            }
            catch (Exception ex)
            {
                LogService.Write($"[TIA-FATAL] Error en Sincronización: {ex.Message}", true);
                EnviarEstado($"Error: {ex.Message}", true);
                throw;
            }
        }

        /// <summary>
        /// Helper para actualizar el comentario en todos los idiomas disponibles de la constante.
        /// </summary>
        private void ActualizarComentarios(PlcUserConstant constante, string comentario)
        {
            foreach (var item in constante.Comment.Items)
            {
                try
                {
                    // Intentamos asignación directa (rápida)
                    item.Text = comentario;
                }
                catch
                {
                    // Plan B: Uso de atributos dinámicos si la propiedad Text está bloqueada
                    item.SetAttribute("Text", comentario);
                }
            }
        }

        /// <summary>
        /// Helper para enviar mensajes a la UI y al Log simultáneamente.
        /// </summary>
        private void EnviarEstado(string msg, bool esError = false)
        {
            OnStatusChanged?.Invoke(msg, esError);
        }
    }
}