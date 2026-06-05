"""
Infrastructure Layer - Procesos Parser
======================================
Parser específico para extraer procesos del Excel Maestro.
"""

from typing import Any

from core.models import Proceso
from infrastructure.parsers.base_parser import BaseParser


def _safe_int(valor: Any) -> int:
    """
    Conversión robusta a int, tolerante a tipos raros de Pandas.

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
            columnas_numericas=[
                "UID", "PReal", "Index_Preal",
                "PInt", "Index_Pint", "Alarmas"
            ],
        )

        return [
            Proceso(
                uid=int(row["UID"]),
                nombre=str(row.get("Nombre", "")),
                codigo=str(row.get("Codigo", "")),
                # Columnas unificadas: tipado int puro, sin None
                preal=_safe_int(row.get("PReal")),
                index_preal=_safe_int(row.get("Index_Preal")),
                pint=_safe_int(row.get("PInt")),
                index_pint=_safe_int(row.get("Index_Pint")),
                alarmas=_safe_int(row.get("Alarmas")),
            )
            for _, row in df.iterrows()
        ]
