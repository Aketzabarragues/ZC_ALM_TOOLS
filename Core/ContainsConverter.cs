using System;
using System.Globalization;
using System.Windows.Data;

namespace ZC_ALM_TOOLS.Core
{



    // ==================================================================================================================
    /// <summary>
    /// Convertidor para comprobar si un texto contiene una cadena específica
    ///  Se usa en la interfaz para detectar cambios (buscando el símbolo '->')
    /// </summary>
    public class ContainsConverter : IValueConverter
    {

        // ==================================================================================================================
        /// <summary>
        /// Evalúa si el valor recibido contiene el texto pasado como parámetro
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string textValue = value.ToString();
            string searchString = parameter.ToString();

            // Devuelve true si el texto contiene la flecha de cambio
            return textValue.Contains(searchString);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // No es necesario para bindings de una sola dirección (OneWay)
            throw new NotImplementedException();
        }
    }
}