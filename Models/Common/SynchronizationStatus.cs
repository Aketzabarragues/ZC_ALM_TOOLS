namespace ZC_ALM_TOOLS.Models.Common
{

    // ==================================================================================================================
    /// <summary>
    /// ENUM con los estados para los indicadores visuales de la interfaz
    /// </summary>
    public enum SynchronizationStatus
    {
        Pending, // Gris - Aún no se ha comprobado
        Ok,      // Verde - Todo coincide
        Error,   // Rojo - Hay discrepancias
        Warning  // Naranja - Requiere atención manual
    }

}