using System;
using System.Globalization;
using System.Windows.Data;

namespace ZC_ALM_TOOLS.Core
{
    /// <summary>
    /// Convertidor que comprueba si una cadena de texto contiene un sub-string específico.
    /// Se utiliza en la UI para detectar cambios de nombre (buscando '->') o estados específicos.
    /// </summary>
    public class ContainsConverter : IValueConverter
    {
        /// <summary>
        /// Evalúa si el valor contiene el parámetro proporcionado.
        /// </summary>
        /// <param name="value">El texto de la celda (el estado del dispositivo).</param>
        /// <param name="parameter">El texto a buscar (en nuestro caso, '->').</param>
        /// <returns>True si lo contiene, False en caso contrario.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string text = value.ToString();
            string search = parameter.ToString();

            // Devuelve true si el estado contiene la flecha de cambio o la palabra clave
            return text.Contains(search);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // No es necesario para un binding OneWay
            throw new NotImplementedException();
        }
    }
}