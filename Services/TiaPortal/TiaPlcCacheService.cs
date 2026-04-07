using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using ZC_ALM_TOOLS.Models.TiaPortal;
using ZC_ALM_TOOLS.Services.Common;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{
    /// <summary>
    /// Servicio responsable de mantener el índice en memoria RAM del PLC activo.
    /// Actúa como la única fuente de verdad para búsquedas rápidas sin saturar la API de TIA Portal.
    /// </summary>
    public class TiaPlcCacheService
    {

        private PlcSoftware _currentPlc;

        // Diccionarios de caché en RAM
        private List<CachedPlcBlock> _plcCache;
        private List<CachedPlcTagTable> _tagTableCache;
        private List<CachedPlcType> _typeCache;


        public PlcSoftware CurrentPlc => _currentPlc;



        private bool _isCacheBuilt = false;

        private readonly ILogService _logService;
        private readonly IStatusService _statusService;


        public TiaPlcCacheService(ILogService logService, IStatusService statusService)
        {

            _logService = logService;
            _statusService = statusService;

        }



        // ==================================================================================================================
        /// <summary>
        /// Exportar el contenido de la caché a un archivo TXT para análisis
        /// </summary>
        public void DumpCacheToTxt(string filePath)
        {
            try
            {
                if (!_isCacheBuilt || _plcCache == null) return;

                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("=========================================================");
                    writer.WriteLine("             VOLCADO DE CACHÉ DE TIA PORTAL              ");
                    writer.WriteLine("=========================================================");
                    writer.WriteLine($"Fecha de volcado: {DateTime.Now}");
                    writer.WriteLine($"PLC: {_currentPlc?.Name}");
                    writer.WriteLine($"Total Bloques: {_plcCache.Count} | Total Tablas: {_tagTableCache?.Count ?? 0} | Total UDTs: {_typeCache?.Count ?? 0}");
                    writer.WriteLine("=========================================================\n");

                    writer.WriteLine("=== BLOQUES (OB/FC/FB/DB) ===");

                    // Ordenamos la lista alfabéticamente solo para imprimirla bonita
                    foreach (var item in _plcCache.OrderBy(b => b.Name))
                    {
                        writer.WriteLine($"[Nombre] {item.Name,-35} | [Num] {item.Number,-5} | [Tipo API] {item.ApiType,-12} | [Ruta] {item.FolderPath}");
                    }

                    writer.WriteLine("\n=== TABLAS DE VARIABLES ===");
                    if (_tagTableCache != null)
                    {
                        foreach (var item in _tagTableCache.OrderBy(t => t.Name))
                        {
                            writer.WriteLine($"[Nombre] {item.Name,-35} | [Ruta] {item.FolderPath}");
                        }
                    }

                    writer.WriteLine("\n=== TIPOS DE DATOS DE USUARIO (UDT) ===");
                    if (_typeCache != null)
                    {
                        foreach (var item in _typeCache.OrderBy(t => t.Name))
                        {
                            writer.WriteLine($"[Nombre] {item.Name,-35} | [Ruta] {item.FolderPath}");
                        }
                    }
                }
                _logService.Write($"[TIA-PLC-CACHE-SERVICE] [DumpCache] Caché exportada exitosamente a: {filePath}");
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-CACHE-SERVICE] [DumpCache] Error exportando la caché: {ex.Message}", true);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Asignacion de PLC seleccionado
        /// </summary>
        public void UpdatePlc(PlcSoftware plcSoftware)
        {
            if (_currentPlc != plcSoftware)
            {
                _currentPlc = plcSoftware;

                // Si cambiamos de PLC, destruimos la caché antigua
                _isCacheBuilt = false;
                _plcCache?.Clear();
                _tagTableCache?.Clear();
                _typeCache?.Clear();
                _logService.Write("[TIA-PLC-CACHE-SERVICE] [UpdatePlc] PLC modificado. Caché invalidada.");
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Construye el índice completo del PLC en memoria RAM
        /// </summary>
        public void BuildBlockCache()
        {
            try
            {
                if (_currentPlc == null) return;

                _plcCache = new List<CachedPlcBlock>();
                _tagTableCache = new List<CachedPlcTagTable>();
                _typeCache = new List<CachedPlcType>();

                _logService.Write("[TIA-PLC-CACHE-SERVICE] [BuildBlockCache] Indexando todos los bloques del PLC en memoria...");

                PopulateCacheRecursively(_currentPlc.BlockGroup, "Root");
                PopulateTagTableCacheRecursively(_currentPlc.TagTableGroup, "Variables de PLC");
                PopulateTypeCacheRecursively(_currentPlc.TypeGroup, "Tipos de datos PLC");

                _isCacheBuilt = true;
                _logService.Write($"[TIA-PLC-CACHE-SERVICE] [BuildBlockCache] Indexación completa: {_plcCache.Count} bloques guardados en caché.");
            }
            catch (Exception ex)
            {
                _logService.Write($"[TIA-PLC-CACHE-SERVICE] [BuildBlockCache] Error construyendo la caché: {ex.Message}", true);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Relleno de la cache de Tipos de Datos de Usuario (UDTs)
        /// </summary>
        private void PopulateTypeCacheRecursively(PlcTypeGroup group, string currentPath)
        {
            foreach (var type in group.Types)
            {
                _typeCache.Add(new CachedPlcType
                {
                    Type = type,
                    Name = type.Name,
                    FolderPath = currentPath
                });
            }

            foreach (var subFolder in group.Groups)
            {
                string nextPath = currentPath == "Tipos de datos PLC" ? subFolder.Name : currentPath + "\\" + subFolder.Name;
                PopulateTypeCacheRecursively(subFolder, nextPath);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Relleno de la cache de tabla de variables
        /// </summary>
        private void PopulateTagTableCacheRecursively(PlcTagTableGroup group, string currentPath)
        {
            foreach (var table in group.TagTables)
            {
                _tagTableCache.Add(new CachedPlcTagTable
                {
                    Table = table,
                    Name = table.Name,
                    FolderPath = currentPath
                });
            }

            foreach (var subFolder in group.Groups)
            {
                string nextPath = currentPath == "Variables de PLC" ? subFolder.Name : currentPath + "\\" + subFolder.Name;
                PopulateTagTableCacheRecursively(subFolder, nextPath);
            }
        }



        // ==================================================================================================================
        /// <summary>
        /// Relleno de la cache de bloques
        /// </summary>
        private void PopulateCacheRecursively(PlcBlockGroup group, string currentPath)
        {
            foreach (var block in group.Blocks)
            {
                // Averiguar el tipo simple
                string simpleType = "";
                if (block is GlobalDB || block is InstanceDB || block is ArrayDB) simpleType = "DB";
                else if (block is FC) simpleType = "FC";
                else if (block is FB) simpleType = "FB";
                else if (block is OB) simpleType = "OB";

                // Añadir a nuestra única lista
                _plcCache.Add(new CachedPlcBlock
                {
                    Block = block,
                    Name = block.Name,
                    Number = block.Number,
                    ApiType = block.GetType().Name,
                    SimpleType = simpleType,
                    FolderPath = currentPath,
                    ProgrammingLanguage = block.ProgrammingLanguage.ToString()
                });
            }

            foreach (var subFolder in group.Groups)
            {
                string nextPath = currentPath == "Root" ? subFolder.Name : currentPath + "\\" + subFolder.Name;
                PopulateCacheRecursively(subFolder, nextPath);
            }
        }




        // ==================================================================================================================
        /// <summary>
        /// Devuelve la cache de los UDTs del PLC
        /// </summary>
        public List<CachedPlcType> GetAllTypes()
        {
            if (!_isCacheBuilt) BuildBlockCache();
            return _typeCache ?? new List<CachedPlcType>();
        }



        // ==================================================================================================================
        /// <summary>
        /// Debuelve la cache de los bloques del PLC
        /// </summary>
        public List<CachedPlcBlock> GetAllBlocks()
        {
            if (!_isCacheBuilt) BuildBlockCache();
            return _plcCache ?? new List<CachedPlcBlock>();
        }



        // ==================================================================================================================
        /// <summary>
        /// Devuelve la cache de las tablas de variables
        /// </summary>
        public List<CachedPlcTagTable> GetAllTagTables()
        {
            if (!_isCacheBuilt) BuildBlockCache();
            return _tagTableCache ?? new List<CachedPlcTagTable>();
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar tabla de variables
        /// </summary>
        public PlcTagTable FindTagTableByName(string tableName)
        {
            if (_currentPlc == null) return null;

            // Si la cache no esta construida, lo hacemos ahora
            if (!_isCacheBuilt) BuildBlockCache();

            return _tagTableCache?.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))?.Table;
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar bloque por nombre
        /// </summary>
        public PlcBlock FindBlockByName(string blockName)
        {
            if (_currentPlc == null) return null;

            // Si la cache no esta construida, lo hacemos ahora
            if (!_isCacheBuilt) BuildBlockCache();

            return _plcCache?.FirstOrDefault(b => b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase))?.Block;
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar UDT por nombre
        /// </summary>
        public PlcType FindTypeByName(string typeName)
        {
            if (_currentPlc == null) return null;

            if (!_isCacheBuilt) BuildBlockCache();

            return _typeCache?.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))?.Type;
        }



        // ==================================================================================================================
        /// <summary>
        /// Buscar bloque por numero
        /// </summary>
        public PlcBlock FindBlockByNumber(int number, string blockType)
        {
            if (_currentPlc == null) return null;

            // Si la cache no esta construida, lo hacemos ahora
            if (!_isCacheBuilt) BuildBlockCache();

            return _plcCache?.FirstOrDefault(b => b.Number == number && b.SimpleType.Equals(blockType, StringComparison.OrdinalIgnoreCase))?.Block;
        }



    }

}