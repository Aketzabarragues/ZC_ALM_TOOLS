"""
Infrastructure Layer - PInt Parser
===================================
Parser específico para extraer parámetros enteros del Excel Maestro.
"""

import math
from typing import Any

from core.models import PInt
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
    Si falla, devolvemos el string tal cual.
    """
    cleaned = _safe_str(value)
    if cleaned is None:
        return 0
    try:
        return int(float(cleaned))
    except (TypeError, ValueError):
        return cleaned


class PIntParser(BaseParser):
    """Parser para extraer la tabla de parámetros enteros."""

    def extraer(self, ruta_excel: str) -> list[PInt]:
        """
        Extrae la lista de parámetros enteros desde el Excel.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos PInt.
        """
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="P_INT",
            table_name="Tabla_PInt",
            columnas_numericas=["Num.DB"],
        )

        return [
            PInt(
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
