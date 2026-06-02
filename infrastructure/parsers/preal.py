"""
Infrastructure Layer - PReal Parser
====================================
Parser específico para extraer parámetros reales del Excel Maestro.
"""

from core.models import PReal
from infrastructure.parsers.base_parser import BaseParser


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
                num_db=int(row["Num.DB"]),
                producto=str(row.get("Producto", "")),
                tipo=str(row.get("Tipo", "")),
                descripcion=str(row.get("Descripcion", "")),
                comentario_db=str(row.get("ComentarioDB", "")),
                visibilidad=str(row.get("Visibilidad", "")),
            )
            for _, row in df.iterrows()
        ]