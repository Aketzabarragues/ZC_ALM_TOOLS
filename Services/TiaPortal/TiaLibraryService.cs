using System;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{

    /// <summary>
    /// Servicio para gestión de librerías globales en Siemens Openness, incluyendo apertura y manejo de excepciones relacionadas con seguridad.
    /// </summary>
    public class TiaLibraryService
    {

        private Siemens.Engineering.TiaPortal _tiaApp;

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;

        public TiaLibraryService(
            Siemens.Engineering.TiaPortal tiaApp,
            ILogService logService,
            IStatusService statusService)
        { 

            _tiaApp = tiaApp;
            _logService = logService;
            _statusService = statusService;

        }



        // ==================================================================================================================
        /// <summary>
        /// Busca una librería global por nombre y, si no está abierta, la abre desde la ruta especificada.
        /// </summary>
        public GlobalLibrary GetOrOpenGlobalLibrary(string libraryPath)
        {
            if (_tiaApp == null)
            {
                _logService.Write("[TIA-LIBRARY-SERVICE] [GetOrOpenGlobalLibrary] ERROR: Instancia de TIA Portal no asignada al servicio.", true);
                return null;
            }

            if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath))
            {
                _logService.Write($"[TIA-LIBRARY-SERVICE] [GetOrOpenGlobalLibrary] La ruta es inválida o el archivo no existe: {libraryPath}", true);
                return null;
            }

            try
            {
                FileInfo libFile = new FileInfo(libraryPath);
                string libraryName = Path.GetFileNameWithoutExtension(libFile.Name);

                // 1. Comprobar si ya está abierta en la instancia actual
                var openedLibrary = _tiaApp.GlobalLibraries.FirstOrDefault(l =>
                    l.Name.Equals(libraryName, StringComparison.OrdinalIgnoreCase));

                if (openedLibrary != null)
                {
                    _logService.Write($"[TIA-LIBRARY-SERVICE] [GetOrOpenGlobalLibrary] Librería global '{libraryName}' ya se encuentra abierta.");
                    return openedLibrary;
                }

                // 2. Si no está abierta, pedir a Openness que la abra
                _statusService.Set($"Abriendo librería global '{libraryName}'...", StatusType.Warning);

                // OpenMode.ReadOnly es crucial para evitar bloqueos si la librería está en uso por otro proceso
                var newOpenedLibrary = _tiaApp.GlobalLibraries.Open(libFile, OpenMode.ReadOnly);

                _logService.Write($"[TIA-LIBRARY-SERVICE] [GetOrOpenGlobalLibrary] Librería '{libraryName}' abierta correctamente.");
                return newOpenedLibrary;
            }
            catch (EngineeringSecurityException)
            {
                // Se relanza para que el ViewModel lo capture y avise al usuario en la UI
                throw;
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-LIBRARY-SERVICE] [GetOrOpenGlobalLibrary] Excepción al abrir la librería: {ex.Message}", true);
                return null;
            }
        }



    }
    

}
