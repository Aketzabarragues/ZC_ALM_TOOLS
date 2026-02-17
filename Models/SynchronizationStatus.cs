namespace ZC_ALM_TOOLS.Models
{
    // Estados para los indicadores visuales de la interfaz
    public enum SynchronizationStatus
    {
        Pending, // Gris - Aún no se ha comprobado
        Ok,      // Verde - Todo coincide
        Error,   // Rojo - Hay discrepancias
        Warning  // Naranja - Requiere atención manual
    }
}