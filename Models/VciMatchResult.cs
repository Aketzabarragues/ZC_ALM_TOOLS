using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZC_ALM_TOOLS.Models
{
    public enum VciMapResult
    {
        Success,              // Mapeado y comparado OK
        SuccessWithWarning,   // Mapeado OK, pero falló el UpdateStatus (Protegido/Errores)
        Error                 // Fallo total (no se pudo mapear)
    }
}
