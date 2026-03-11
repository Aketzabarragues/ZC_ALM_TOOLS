

namespace ZC_ALM_TOOLS.Models.Generator
{
    // ==================================================================================================================
    /// <summary>
    /// Interfaz común para que el sistema maneje cualquier dispositivo del Excel
    /// </summary>
    public interface IDevice
    {
        int Numero { get; set; }
        string Tag { get; set; }
        string Descripcion { get; set; }
        string CPTag { get; set; }
        string CPComentario { get; set; }
        string Estado { get; set; }
    }
}