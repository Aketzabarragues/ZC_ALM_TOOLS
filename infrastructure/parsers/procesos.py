"""
Infrastructure Layer - Procesos Parser
======================================
Parser específico para extraer procesos del Excel Maestro.
"""

from core.models import Proceso
from infrastructure.parsers.base_parser import BaseParser


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
            columnas_numericas=["UID", "Num.Etapas", "PReal", "PInt", "Alarmas"],
        )

        return [
            Proceso(
                uid=int(row["UID"]),
                nombre=str(row.get("Nombre", "")),
                codigo=str(row.get("Codigo", "")),
                num_etapas=int(row.get("Num.Etapas", 0)),
                p_real=int(row.get("PReal", 0)),
                p_int=int(row.get("PInt", 0)),
                alarmas=int(row.get("Alarmas", 0)),
            )
            for _, row in df.iterrows()
        ]