namespace ZC_ALM_TOOLS.Models
{
    public interface IDispositivo
    {
        int Numero { get; set; }
        string CPTag { get; set; }        // El nombre real en el PLC (VA_101)
        string CPComentario { get; set; } // El comentario real en el PLC
        string Estado { get; set; }
        string Tag {  get; set; }
        string Descripcion { get; set; }
    }
}