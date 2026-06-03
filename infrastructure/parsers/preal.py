"""
Infrastructure Layer - PReal Parser
====================================
Parser específico para extraer parámetros reales del Excel Maestro.
"""

import math
from typing import Any

from core.models import PReal
from infrastructure.parsers.base_parser import BaseParser


def _safe_str(value: Any) -> str | None:
    """Convierte un valor de celda a str, devolviendo None para NaN/None/vacio."""
    if value is None:
        return None
    if isinstance(value, float) and math.isnan(value):
        return None
    s = str(value).strip()
    if not s or s.lower() in ("nan", "none", "null"):
        return None
    return s


def _safe_num_lista(value: Any) -> int | str:
    """
    Para Num.Lista: limpiamos NaN y casteamos a int cuando es posible.
    Si falla, devolvemos el string tal cual (ej. '1' o '1.0' o 'N/A').
    """
    cleaned = _safe_str(value)
    if cleaned is None:
        return 0
    try:
        return int(float(cleaned))
    except (TypeError, ValueError):
        return cleaned


class PRealParser(BaseParser):
    """Parser para extraer la tabla de parámetros reales."""

    def extraer(self, ruta_excel: str) -> list[PReal]:
        """
        Extrae la lista de parámetros reales desde el Excel.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos PReal.
        """
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="P_REAL",
            table_name="Tabla_PReal",
            columnas_numericas=["Num.DB"],
        )

        return [
            PReal(
                uid=str(row["UID"]),
                numero=str(row["Numero"]),
                proceso=str(row.get("Proceso", "")),
                codigo=str(row.get("Codigo", "")),
                num_db=int(row["Num.DB"]),
                producto=str(row.get("Producto", "")),
                tipo=str(row.get("Tipo", "")),
                descripcion=str(row.get("Descripcion", "")),
                comentario_db=str(row.get("ComentarioDB", "")),
                visibilidad=str(row.get("Visibilidad", "")),
                num_lista=_safe_num_lista(row.get("Num.Lista", 0)),
                txt_lista=str(row.get("Txt.Lista", "")),
            )
            for _, row in df.iterrows()
        ]
