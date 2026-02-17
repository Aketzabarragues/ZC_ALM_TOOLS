using System;
using ZC_ALM_TOOLS.Core;

namespace ZC_ALM_TOOLS.Models
{
    /// <summary>
    /// Define los estados posibles para los indicadores visuales (semáforos) de la UI.
    /// </summary>
    public enum EstadoSincronizacion
    {
        Pendiente, // Gris
        Ok,        // Verde
        Error,     // Rojo
        Warning    // Naranja/Amarillo
    }

    /// <summary>
    /// Modelo que representa una categoría de dispositivos (ej. Motores, Válvulas)
    /// y contiene su configuración específica para TIA Portal y su estado de sincronización.
    /// </summary>
    public class DeviceCategory : ObservableObject
    {
        // --- CONFIGURACIÓN DE IDENTIFICACIÓN ---

        /// <summary>Nombre legible de la categoría (ej. "Válvula").</summary>
        public string Name { get; set; }

        /// <summary>Clave para buscar el valor N_MAX en el diccionario de datos globales del Excel.</summary>
        public string GlobalConfigKey { get; set; }

        /// <summary>Nombre de la constante en el PLC que define el tamaño del array (ej. N_MAX_V).</summary>
        public string PlcCountConstant { get; set; }


        // --- CONFIGURACIÓN TIA PORTAL (MAPPING) ---

        /// <summary>Carpeta o Grupo donde se encuentra la tabla de variables.</summary>
        public string TiaGroup { get; set; }

        /// <summary>Nombre de la tabla de variables (Tag Table).</summary>
        public string TiaTable { get; set; }

        /// <summary>Nombre del bloque de datos para la cirugía XML (ej. DB2010_V).</summary>
        public string TiaDbName { get; set; }

        /// <summary>Nombre del Array dentro del DB donde se inyectarán comentarios (ej. V).</summary>
        public string TiaDbArrayName { get; set; }


        // --- PROPIEDADES DE ESTADO (SEMÁFOROS UI) ---
        // Usamos OnPropertyChanged para que los círculos de la UI cambien de color al instante.

        private EstadoSincronizacion _estadoNMax = EstadoSincronizacion.Pendiente;
        public EstadoSincronizacion EstadoNMax
        {
            get => _estadoNMax;
            set { _estadoNMax = value; OnPropertyChanged(); }
        }

        private EstadoSincronizacion _estadoConstantes = EstadoSincronizacion.Pendiente;
        public EstadoSincronizacion EstadoConstantes
        {
            get => _estadoConstantes;
            set { _estadoConstantes = value; OnPropertyChanged(); }
        }

        private EstadoSincronizacion _estadoDB = EstadoSincronizacion.Pendiente;
        public EstadoSincronizacion EstadoDB
        {
            get => _estadoDB;
            set { _estadoDB = value; OnPropertyChanged(); }
        }
    }
}