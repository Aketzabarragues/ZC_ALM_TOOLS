namespace ZC_ALM_TOOLS.Models.Vci
{
    // ==================================================================================================================
    /// <summary>
    /// ENUM con los estados para los indicadores visuales de la interfaz VCI
    /// </summary>
    public enum VciMatchState
    {
        YaEnlazado,       
        ListoParaEnlazar, 
        FaltaExportar,    
        Conflicto,
        ErrorAlEnlazar
    }
}