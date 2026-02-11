using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZC_ALM_TOOLS.Models
{
    public class ProjectDevice
    {
        public string Name { get; set; }
        public string Type { get; set; } // "PLC", "HMI", "PC Station"
        public object DeviceObject { get; set; } // Guardamos el objeto real (PlcSoftware o HmiTarget)
        public string Firmware { get; set; } // Opcional, por si quieres verlo
    }
}
