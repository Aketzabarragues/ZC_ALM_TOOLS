using System;
using System.Collections.Generic;

namespace ZC_ALM_TOOLS
{
    public static class JsonParserHelper
    {
        // ----------------------------------------------------
        // MÉTODOS PÚBLICOS (Los que llamas desde fuera)
        // ----------------------------------------------------

        public static List<ProcessConfig> ParsearProcesos(string json)
        {
            var lista = new List<ProcessConfig>();
            try
            {
                string content = ExtraerContenidoData(json);
                if (string.IsNullOrEmpty(content)) return lista;

                // Separamos por objetos "}, {"
                string[] objetos = content.Split(new string[] { "}," }, StringSplitOptions.None);

                foreach (var obj in objetos)
                {
                    var p = new ProcessConfig();
                    var props = LimpiarYPartirPropiedades(obj);

                    foreach (var kv in props)
                    {
                        string k = kv.Key;
                        string v = kv.Value;

                        if (k == "ID") p.Id = v; // Lo guardamos como string o int según tu clase
                        if (k == "Nombre") p.Nombre = v;
                        if (k.Contains("Etapas")) p.NumEtapas = ParseInt(v);
                        if (k == "Preal") p.MaxPReal = ParseInt(v);
                        if (k == "Pint") p.MaxPInt = ParseInt(v);
                        if (k == "Alarmas") p.NumAlarmas = ParseInt(v);
                    }
                    // Validación mínima: que tenga ID
                    if (!string.IsNullOrEmpty(p.Id)) lista.Add(p);
                }
            }
            catch
            {
                // En caso de error grave devolvemos lista vacía o parcial
            }
            return lista;
        }

        public static List<ParameterConfigReal> ParsearParametros(string json)
        {
            var lista = new List<ParameterConfigReal>();
            try
            {
                string content = ExtraerContenidoData(json);
                if (string.IsNullOrEmpty(content)) return lista;

                string[] objetos = content.Split(new string[] { "}," }, StringSplitOptions.None);

                foreach (var obj in objetos)
                {
                    var p = new ParameterConfigReal();
                    var props = LimpiarYPartirPropiedades(obj);

                    foreach (var kv in props)
                    {
                        string k = kv.Key;
                        string v = kv.Value;

                        if (k == "UID") p.Uid = v;
                        if (k == "Numero") p.Numero = v;
                        if (k == "Proceso") p.Proceso = v;
                        if (k == "Numero DB") p.DbNumber = v;
                        if (k == "Tipo") p.Tipo = v;
                        if (k == "Descripcion") p.Descripcion = v;
                        if (k == "Visibilidad") p.Visibilidad = v;
                    }
                    if (!string.IsNullOrEmpty(p.Uid)) lista.Add(p);
                }
            }
            catch { }
            return lista;
        }



        public static List<ParameterConfigInt> ParsearPInt(string json)
        {
            var lista = new List<ParameterConfigInt>();
            try
            {
                string content = ExtraerContenidoData(json);
                if (string.IsNullOrEmpty(content)) return lista;

                string[] objetos = content.Split(new string[] { "}," }, StringSplitOptions.None);

                foreach (var obj in objetos)
                {
                    var p = new ParameterConfigInt();
                    var props = LimpiarYPartirPropiedades(obj);

                    foreach (var kv in props)
                    {
                        string k = kv.Key;
                        string v = kv.Value;

                        // AJUSTA ESTOS "IF" SEGÚN TU EXCEL
                        if (k == "UID") p.Uid = v;
                        if (k == "Proceso") p.Proceso = v;
                        if (k == "Nombre" || k == "Variable") p.Nombre = v;
                        if (k == "Tipo") p.Tipo = v;
                        if (k == "Valor Inicial" || k == "Valor") p.ValorInicial = v;
                        if (k == "Comentario" || k == "Descripcion") p.Comentario = v;
                        if (k == "Visible" || k == "HMI") p.HmiVisible = v;
                    }
                    if (!string.IsNullOrEmpty(p.Uid)) lista.Add(p);
                }
            }
            catch { }
            return lista;
        }


















        // ----------------------------------------------------
        // MÉTODOS PRIVADOS (Auxiliares internos)
        // ----------------------------------------------------

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
            // Limpiamos llaves externas y espacios
            string limpio = objRaw.Replace("{", "").Replace("}", "").Trim();

            // Separamos por comas
            string[] props = limpio.Split(',');

            foreach (var prop in props)
            {
                int sep = prop.IndexOf(':');
                if (sep > 0)
                {
                    // Quitamos comillas de clave y valor
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