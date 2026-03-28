
namespace ZC_ALM_TOOLS.Models.Vci
{
    public enum VciMapResult
    {
        Success,              // Mapeado y comparado OK
        SuccessWithWarning,   // Mapeado OK, pero falló el UpdateStatus (Protegido/Errores)
        Error                 // Fallo total (no se pudo mapear)
    }
}
