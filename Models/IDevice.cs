namespace ZC_ALM_TOOLS.Models
{
    // Interfaz común para todos los dispositivos (Válvulas, Motores, etc.)
    public interface IDevice
    {
        int Number { get; set; }        // ID numérico (índice del array en el PLC)
        string Tag { get; set; }       // Nombre original en el Excel
        string Description { get; set; } // Descripción del dispositivo
        string PlcTag { get; set; }    // Nombre formateado para el PLC
        string PlcComment { get; set; } // Comentario formateado para el PLC
        string Status { get; set; }     // Estado visual (Sincronizado, Nuevo, o la flecha ->)
    }
}