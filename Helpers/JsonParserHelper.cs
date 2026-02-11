using System;
using System.Collections.Generic;
using ZC_ALM_TOOLS.Models;

namespace ZC_ALM_TOOLS.Core
{
    public static class JsonParserHelper
    {
        public static List<Disp_V> ParsearDispositivos(string json)
        {
            var lista = new List<Disp_V>();
            try
            {
                string content = ExtraerContenidoData(json);
                if (string.IsNullOrEmpty(content)) return lista;

                // Separamos por objetos "}," 
                // Usamos Split con las llaves para ser más robustos ante saltos de línea
                string[] objetos = content.Split(new string[] { "},", "}\r\n,", "}\n," }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var obj in objetos)
                {
                    var d = new Disp_V();
                    // IMPORTANTE: Limpiar saltos de línea y tabulaciones antes de procesar
                    string limpio = obj.Replace("{", "").Replace("}", "").Replace("\r", "").Replace("\n", "").Trim();

                    // Separamos por comas
                    string[] props = limpio.Split(',');

                    foreach (var prop in props)
                    {
                        int sep = prop.IndexOf(':');
                        if (sep > 0)
                        {
                            string key = prop.Substring(0, sep).Replace("\"", "").Trim();
                            string val = prop.Substring(sep + 1).Replace("\"", "").Trim();

                            switch (key)
                            {
                                case "UID": d.UID = val; break;
                                case "Numero": d.Numero = ParseInt(val); break;
                                case "Tag": d.Tag = val; break;
                                case "Descripcion": d.Descripcion = val; break;
                                case "FAT": d.FAT = val; break;
                                case "S.Byte": d.SByte = val; break;
                                case "S.Bit": d.SBit = val; break;
                                case "RR.Byte": d.RRByte = val; break;
                                case "RR.Bit": d.RRBit = val; break;
                                case "RT.Byte": d.RTByte = val; break;
                                case "RT.Bit": d.RTBit = val; break;
                                case "Gr. Alarma": d.GrAlarma = val; break;
                                case "Cuadro": d.Cuadro = val; break;
                                case "Observaciones": d.Observaciones = val; break;
                                case "CP.Tag": d.CPTag = val; break;
                                case "CP.Tipo": d.CPTipo = val; break;
                                case "CP.Num.": d.CPNum = ParseInt(val); break;
                                case "CP.Comentario": d.CPComentario = val; break;
                            }
                        }
                    }
                    // Solo añadimos si hemos logrado leer al menos el UID
                    if (!string.IsNullOrEmpty(d.UID))
                    {
                        lista.Add(d);
                    }
                }
            }
            catch (Exception ex)
            {
                // Esto te dirá si el fallo es aquí dentro
                System.Windows.MessageBox.Show("Error interno en Parser: " + ex.Message);
            }
            return lista;
        }






        private static string ExtraerContenidoData(string json)
        {
            int idxData = json.IndexOf("\"data\":");
            if (idxData == -1) return null;
            int start = json.IndexOf('[', idxData) + 1;
            int end = json.LastIndexOf(']');
            if (start >= end) return null;
            return json.Substring(start, end - start);
        }

        private static Dictionary<string, string> LimpiarYPartirPropiedades(string objRaw)
        {
            var dict = new Dictionary<string, string>();
            string limpio = objRaw.Replace("{", "").Replace("}", "").Trim();
            string[] props = limpio.Split(',');

            foreach (var prop in props)
            {
                int sep = prop.IndexOf(':');
                if (sep > 0)
                {
                    string key = prop.Substring(0, sep).Replace("\"", "").Trim();
                    string val = prop.Substring(sep + 1).Replace("\"", "").Trim();
                    dict[key] = val;
                }
            }
            return dict;
        }

        private static int ParseInt(string val)
        {
            int.TryParse(val, out int r);
            return r;
        }
    }
}