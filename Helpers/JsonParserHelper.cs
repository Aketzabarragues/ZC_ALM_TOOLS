using System;
using System.Collections.Generic;
using ZC_ALM_TOOLS.Models; // Asegúrate de que esto apunta a tus modelos

namespace ZC_ALM_TOOLS
{
    public static class JsonParserHelper
    {
        // ----------------------------------------------------
        // MÉTODOS PÚBLICOS
        // ----------------------------------------------------

        // --- NUEVO: PARSEAR DISPOSITIVOS (Válvulas, Motores, etc.) ---
        public static List<DispositivoModel> ParsearDispositivos(string json)
        {
            var lista = new List<DispositivoModel>();
            try
            {
                string content = ExtraerContenidoData(json);
                if (string.IsNullOrEmpty(content)) return lista;

                // Separamos por objetos "}, {" o "},{"
                string[] objetos = content.Split(new string[] { "}," }, StringSplitOptions.None);

                foreach (var obj in objetos)
                {
                    var d = new DispositivoModel();
                    var props = LimpiarYPartirPropiedades(obj);

                    foreach (var kv in props)
                    {
                        string k = kv.Key;
                        string v = kv.Value;

                        // Mapeo manual según tu JSON (disp_v.json)
                        if (k == "UID") d.UID = v;
                        if (k == "Numero") d.Numero = ParseInt(v);
                        if (k == "Tag" || k == "Nombre") d.Tag = v; // JSON suele traer "Tag"
                        if (k == "Descripcion") d.Descripcion = v;
                        if (k == "Proceso") d.Proceso = v; // Si lo añadiste

                        // Campos específicos de TIA Portal (CP.*)
                        if (k == "CP.Tag") d.TagTiaPortal = v;
                        if (k == "CP.Comentario") d.ComentarioTiaPortal = v;
                    }

                    if (!string.IsNullOrEmpty(d.UID)) lista.Add(d);
                }
            }
            catch (Exception)
            {
                // Manejo silencioso o log
            }
            return lista;
        }

        // --- NUEVO: LEER METADATOS (INFO) ---
        // Devuelve el número total de items sin procesar toda la lista
        public static int ObtenerTotalItems(string json)
        {
            try
            {
                // Buscamos "total_items": 15
                int idxKey = json.IndexOf("\"total_items\"");
                if (idxKey == -1) return 0;

                int idxColon = json.IndexOf(':', idxKey);
                int idxComma = json.IndexOf(',', idxColon);
                int idxEndBrace = json.IndexOf('}', idxColon);

                // El valor termina en la coma o en el cierre de llave
                int end = (idxComma != -1 && idxComma < idxEndBrace) ? idxComma : idxEndBrace;

                if (idxColon != -1 && end != -1)
                {
                    string numStr = json.Substring(idxColon + 1, end - idxColon - 1).Trim();
                    return ParseInt(numStr);
                }
            }
            catch { }
            return 0;
        }

        // --- PARSEADORES EXISTENTES (PROCESOS Y PARAMETROS) ---

        public static List<ProcessConfig> ParsearProcesos(string json)
        {
            var lista = new List<ProcessConfig>();
            try
            {
                string content = ExtraerContenidoData(json);
                if (string.IsNullOrEmpty(content)) return lista;

                string[] objetos = content.Split(new string[] { "}," }, StringSplitOptions.None);

                foreach (var obj in objetos)
                {
                    var p = new ProcessConfig();
                    var props = LimpiarYPartirPropiedades(obj);

                    foreach (var kv in props)
                    {
                        if (kv.Key == "ID") p.Id = kv.Value;
                        if (kv.Key == "Nombre") p.Nombre = kv.Value;
                        if (kv.Key.Contains("Etapas")) p.NumEtapas = ParseInt(kv.Value);
                        if (kv.Key == "Preal") p.MaxPReal = ParseInt(kv.Value);
                        if (kv.Key == "Pint") p.MaxPInt = ParseInt(kv.Value);
                        if (kv.Key == "Alarmas") p.NumAlarmas = ParseInt(kv.Value);
                    }
                    if (!string.IsNullOrEmpty(p.Id)) lista.Add(p);
                }
            }
            catch { }
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
                        if (kv.Key == "UID") p.Uid = kv.Value;
                        if (kv.Key == "Numero") p.Numero = kv.Value;
                        if (kv.Key == "Proceso") p.Proceso = kv.Value;
                        if (kv.Key == "Numero DB") p.DbNumber = kv.Value;
                        if (kv.Key == "Tipo") p.Tipo = kv.Value;
                        if (kv.Key == "Descripcion") p.Descripcion = kv.Value;
                        if (kv.Key == "Visibilidad") p.Visibilidad = kv.Value;
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
                        if (kv.Key == "UID") p.Uid = kv.Value;
                        if (kv.Key == "Proceso") p.Proceso = kv.Value;
                        if (kv.Key == "Nombre" || kv.Key == "Variable") p.Nombre = kv.Value;
                        if (kv.Key == "Tipo") p.Tipo = kv.Value;
                        if (kv.Key == "Valor Inicial" || kv.Key == "Valor") p.ValorInicial = kv.Value;
                        if (kv.Key == "Comentario" || kv.Key == "Descripcion") p.Comentario = kv.Value;
                        if (kv.Key == "Visible" || kv.Key == "HMI") p.HmiVisible = kv.Value;
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
            // Busca "data": [ ... ]
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

            // ADVERTENCIA: Split(',') fallará si la descripción contiene comas (ej: "Valvula, retorno").
            // Para "No dependencias", asumimos que los textos no tienen comas o aceptamos el riesgo.
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
            // Limpiar decimales tipo 10.0 que vienen de Excel/Python a veces
            if (val.Contains("."))
            {
                val = val.Split('.')[0];
            }
            int.TryParse(val, out int r);
            return r;
        }
    }
}