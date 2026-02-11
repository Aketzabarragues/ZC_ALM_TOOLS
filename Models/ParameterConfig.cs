namespace ZC_ALM_TOOLS
{
    public class ParameterConfigReal
    {
        public string Uid { get; set; }
        public string Numero { get; set; }
        public string Proceso { get; set; }
        public string DbNumber { get; set; } // Corresponde a "Numero DB"
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
        public string Visibilidad { get; set; }
    }


    public class ParameterConfigInt
    {
        public string Uid { get; set; }
        public string Proceso { get; set; } // NECESARIO para filtrar
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public string ValorInicial { get; set; }
        public string Comentario { get; set; }
        public string HmiVisible { get; set; } // Ejemplo por si hay visibilidad
    }
}