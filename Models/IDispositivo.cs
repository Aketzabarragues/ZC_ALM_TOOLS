namespace ZC_ALM_TOOLS.Models
{
    public interface IDispositivo
    {
        int Numero { get; set; }        // El ID numérico (índice del array)
        string Tag { get; set; }       // El nombre en el Excel
        string Descripcion { get; set; }
        string CPTag { get; set; }     // El nombre formateado para el PLC
        string CPComentario { get; set; } // El comentario formateado para el PLC
        string Estado { get; set; }    // "Sincronizado", "Nuevo" o la flecha "->"
    }
}