"""
Infrastructure Layer - Parsers Utils
=====================================
Helpers compartidos por los parsers del Excel Maestro.

Estos helpers centralizan la conversion segura de valores de celda
(que pueden venir como NaN, float, str, None) a tipos Python limpios.
"""

import math
from typing import Any

__all__ = ["_safe_str", "_safe_int", "_safe_num_lista"]


def _safe_num_lista(value: Any) -> int | str:
    """
    Convierte un valor de celda a int|str para campos como Num.Lista.

    - Si el valor es None, NaN, vacio, o 'nan'/'None'/'null' -> 0
    - Si el valor es numerico (1, 1.0, '5', '5.0') -> int
    - Si el valor es texto no numerico -> str (preserva el original)

    Esto evita que un Num.Lista como "N/A" o "TODOS" se rompa con int().
    """
    cleaned = _safe_str(value)
    if cleaned is None:
        return 0
    try:
        return int(float(cleaned))
    except (TypeError, ValueError):
        return cleaned


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


def _safe_int(valor: Any) -> int:
    """
    Conversion robusta a int, tolerante a tipos raros de Pandas.

    Acepta: int, float, str numerico ('60', '60.0'), NaN, None, '', 'nan'.
    Si falla o es nulo: devuelve 0.

    Truco clave: int(float(valor)) dentro del try permite aceptar tanto
    '60' como '60.0' sin que Pandas reviente.
    """
    if valor is None:
        return 0
    try:
        return int(float(valor))
    except (TypeError, ValueError):
        return 0
