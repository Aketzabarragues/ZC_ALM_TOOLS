"""
Software Parser - PReal
========================
Extrae parametros reales del Excel Maestro.
"""

from core.models import PReal
from infrastructure.parsers.base_parser import BaseParser
from infrastructure.parsers.utils import _safe_num_lista

__all__ = ["PRealParser"]


class PRealParser(BaseParser):
    """Parser para extraer la tabla de parámetros reales."""

    def extraer(self, ruta_excel: str) -> list[PReal]:
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
