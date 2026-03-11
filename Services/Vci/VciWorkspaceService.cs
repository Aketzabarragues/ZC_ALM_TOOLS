using System;
using System.Collections.Generic;
using System.IO;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// Servicio encargado de gestionar el Workspace de VCI en Tia Portal
    /// </summary>
    public class VciWorkspaceService
    {

        // ==================================================================================================================
        /// <summary>
        /// Constructor
        /// </summary>
        public VciWorkspaceService()
        {
        }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para escanear un directorio y devuelve un diccionario: Key = Nombre del Bloque, Value = Ruta del Archivo XML
        /// </summary>
        public Dictionary<string, string> GetVciFilesFromWorkspace(string workspacePath)
        {
            var vciFilesDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            {
                return vciFilesDict; // Devuelve diccionario vacío si la ruta no es válida
            }

            try
            {
                // Buscamos todos los XML de forma recursiva (por si el usuario organizó el Workspace en subcarpetas)
                string[] xmlFiles = Directory.GetFiles(workspacePath, "*.xml", SearchOption.AllDirectories);

                foreach (string filePath in xmlFiles)
                {
                    // Por norma general, Siemens nombra el archivo XML igual que el bloque (ej. "FC_Main.xml")
                    string blockName = Path.GetFileNameWithoutExtension(filePath);

                    // Evitamos duplicados por si acaso hay archivos con el mismo nombre en distintas carpetas
                    if (!vciFilesDict.ContainsKey(blockName))
                    {
                        vciFilesDict.Add(blockName, filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                // En el futuro aquí podemos usar tu LogService para registrar el error
                LogService.Write($"[VciWorkspaceService] [GetVciFilesFromWorkspace] Error al leer el Workspace VCI en '{workspacePath}': {ex.Message}", true);
            }

            return vciFilesDict;
        }



        // ==================================================================================================================
        // (Futuro) Método para leer las dependencias internas del XML
        // public List<string> ExtractDependenciesFromXml(string xmlFilePath) { ... }


        // ==================================================================================================================
        // (Futuro) Método para inyectar comentarios/changelogs en el XML
        // public void UpdateChangelogInXml(string xmlFilePath, string newComment) { ... }

    }
}