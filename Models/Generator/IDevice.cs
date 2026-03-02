namespace ZC_ALM_TOOLS.Models
{
    // Interfaz común para que el sistema maneje cualquier dispositivo del Excel
    public interface IDevice
    {
        int Numero { get; set; }           // ID o índice del array
        string Tag { get; set; }          // Nombre del dispositivo
        string Descripcion { get; set; }   // Descripción técnica
        string CPTag { get; set; }        // Nombre para TIA Portal
        string CPComentario { get; set; } // Comentario para TIA Portal
        string Estado { get; set; }       // Estado de sincronización (UI)
    }
}