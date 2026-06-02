"""
Infrastructure Layer - PInt Parser
===================================
Parser específico para extraer parámetros enteros del Excel Maestro.
"""

from core.models import PInt
from infrastructure.parsers.base_parser import BaseParser


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
                num_db=int(row["Num.DB"]),
                producto=str(row.get("Producto", "")),
                tipo=str(row.get("Tipo", "")),
                descripcion=str(row.get("Descripcion", "")),
                comentario_db=str(row.get("ComentarioDB", "")),
                visibilidad=str(row.get("Visibilidad", "")),
            )
            for _, row in df.iterrows()
        ]