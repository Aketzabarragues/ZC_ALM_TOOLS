"""
Infrastructure Layer - Alarmas Parser
=====================================
Parser específico para extraer alarmas del Excel Maestro.
"""

from core.models import Alarma
from infrastructure.parsers.base_parser import BaseParser


class AlarmasParser(BaseParser):
    """Parser para extraer la tabla de alarmas."""

    def extraer(self, ruta_excel: str) -> list[Alarma]:
        """
        Extrae la lista de alarmas desde el Excel.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos Alarma.
        """
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="ALARMAS",
            table_name="Tabla_Alarmas",
            columnas_numericas=["Num.DB"],
        )

        return [
            Alarma(
                uid=str(row["UID"]),
                numero=str(row["Numero"]),
                proceso=str(row.get("Proceso", "")),
                num_db=int(row["Num.DB"]),
                descripcion=str(row.get("Descripcion", "")),
                comentario_db=str(row.get("ComentarioDB", "")),
            )
            for _, row in df.iterrows()
        ]