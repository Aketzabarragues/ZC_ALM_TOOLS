"""
Infrastructure Layer - Procesos Parser
======================================
Parser específico para extraer procesos del Excel Maestro.
"""

import math
from typing import Any

from core.models import Proceso
from infrastructure.parsers.base_parser import BaseParser


def _safe_str(value: Any) -> str | None:
    """
    Convierte un valor de celda a str, devolviendo None para NaN/None/vacio.
    Pandas lee celdas vacias como NaN (float). Las .xlsx nuevas
    a veces dejan los strings como 'nan' o 'None'.
    """
    if value is None:
        return None
    if isinstance(value, float) and math.isnan(value):
        return None
    s = str(value).strip()
    if not s or s.lower() in ("nan", "none", "null"):
        return None
    return s


def _safe_index(value: Any) -> int | str | None:
    """
    Para index_preal / index_pint: limpia NaN, y si es un numero
    lo castea a int (sin decimales) para evitar que TIA Portal reciba '1.0'.
    Si es texto no numerico, lo devuelve tal cual (preservando prefijos como 'PR_1_001').
    """
    cleaned = _safe_str(value)
    if cleaned is None:
        return None
    # Intentar cast a int (puede ser '1', '1.0', 1, 1.0)
    try:
        return int(float(cleaned))  # '1.0' -> 1.0 -> 1
    except (TypeError, ValueError):
        # Texto no numerico (ej. 'PR_1_001') - devolver tal cual
        return cleaned


class ProcesosParser(BaseParser):
    """Parser para extraer la tabla de procesos."""

    def extraer(self, ruta_excel: str) -> list[Proceso]:
        """
        Extrae la lista de procesos desde el Excel.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos Proceso.
        """
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="CONFIGURACION",
            table_name="Tabla_Procesos",
            # Num.Etapas ya no existe. Columnas numericas restantes:
            columnas_numericas=["UID", "PReal", "PInt", "Alarmas"],
        )

        return [
            Proceso(
                uid=int(row["UID"]),
                nombre=str(row.get("Nombre", "")),
                codigo=str(row.get("Codigo", "")),
                # Nuevas columnas (manejo de NaN -> None)
                preal=_safe_str(row.get("PReal", None)),
                index_preal=_safe_index(row.get("Index_Preal", None)),
                pint=_safe_str(row.get("PInt", None)),
                index_pint=_safe_index(row.get("Index_Pint", None)),
                alarmas=_safe_str(row.get("Alarmas", None)),
                # Aliases historicos (compatibilidad con menu / use cases)
                p_real=int(float(str(row.get("PReal_count", 0)))) if _safe_str(row.get("PReal_count")) else 0,
                p_int=int(float(str(row.get("PInt_count", 0)))) if _safe_str(row.get("PInt_count")) else 0,
            )
            for _, row in df.iterrows()
        ]
