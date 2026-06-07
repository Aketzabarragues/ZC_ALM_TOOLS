"""
Software Parser - Procesos
===========================
Extrae la tabla de procesos del Excel Maestro.
"""

from core.models import Proceso
from infrastructure.parsers.base_parser import BaseParser
from infrastructure.parsers.utils import _safe_int


class ProcesosParser(BaseParser):
    """Parser para extraer la tabla de procesos."""

    def extraer(self, ruta_excel: str) -> list[Proceso]:
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
                preal=_safe_int(row.get("PReal")),
                index_preal=_safe_int(row.get("Index_Preal")),
                pint=_safe_int(row.get("PInt")),
                index_pint=_safe_int(row.get("Index_Pint")),
                alarmas=_safe_int(row.get("Alarmas")),
            )
            for _, row in df.iterrows()
        ]
