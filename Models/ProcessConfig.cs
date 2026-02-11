namespace ZC_ALM_TOOLS
{
    public class ProcessConfig
    {
        // Usamos string para ID por flexibilidad, o int si prefieres
        public string Id { get; set; }
        public string Nombre { get; set; }
        public int NumEtapas { get; set; }
        public int MaxPReal { get; set; }
        public int MaxPInt { get; set; }
        public int NumAlarmas { get; set; }
    }
}