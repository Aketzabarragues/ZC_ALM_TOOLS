"""
Infrastructure Layer - Excel Parser
===================================
Fachada para orquestar la extracción de datos del Excel Maestro.
Delega la lógica específica a los parsers especializados.
"""

import logging

from core.models import Alarma, PInt, PReal, Proceso
from infrastructure.parsers.alarmas import AlarmasParser
from infrastructure.parsers.base_parser import ExcelParsingError
from infrastructure.parsers.pint import PIntParser
from infrastructure.parsers.preal import PRealParser
from infrastructure.parsers.procesos import ProcesosParser

__all__ = ["ExcelParser", "ExcelParsingError"]


class ExcelParser:
    """
    Fachada para orquestar la extracción de datos del Excel Maestro.

    Utiliza parsers especializados para cada tipo de dato:
    - Procesos: Tabla_Procesos (hoja CONFIGURACION)
    - PReal: Tabla_PReal (hoja P_REAL)
    - PInt: Tabla_PInt (hoja P_INT)
    - Alarmas: Tabla_Alarmas (hoja ALARMAS)

    Usage:
        parser = ExcelParser()
        procesos = parser.extraer_procesos("ruta/al/excel.xlsx")
        preales = parser.extraer_preal("ruta/al/excel.xlsx")
    """

    def __init__(self) -> None:
        """Initialize the parser with a logger and specialized parsers."""
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )
        self._procesos: ProcesosParser = ProcesosParser()
        self._preal: PRealParser = PRealParser()
        self._pint: PIntParser = PIntParser()
        self._alarmas: AlarmasParser = AlarmasParser()

    def extraer_procesos(self, ruta_excel: str) -> list[Proceso]:
        """
        Extrae la lista de procesos desde el Excel Maestro.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos Proceso.

        Raises:
            ExcelParsingError: Si no se puede leer el Excel o parsear los datos.
        """
        self._logger.info(f"Extrayendo procesos de: {ruta_excel}")
        datos: list[Proceso] = self._procesos.extraer(ruta_excel)
        self._logger.debug(f"DUMP Procesos: {datos}")
        return datos

    def extraer_preal(self, ruta_excel: str) -> list[PReal]:
        """
        Extrae la lista de parámetros reales desde el Excel Maestro.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos PReal.

        Raises:
            ExcelParsingError: Si no se puede leer el Excel o parsear los datos.
        """
        self._logger.info(f"Extrayendo parámetros reales de: {ruta_excel}")
        datos: list[PReal] = self._preal.extraer(ruta_excel)
        self._logger.debug(f"DUMP PReal: {datos}")
        return datos

    def extraer_pint(self, ruta_excel: str) -> list[PInt]:
        """
        Extrae la lista de parámetros enteros desde el Excel Maestro.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos PInt.

        Raises:
            ExcelParsingError: Si no se puede leer el Excel o parsear los datos.
        """
        self._logger.info(f"Extrayendo parámetros enteros de: {ruta_excel}")
        datos: list[PInt] = self._pint.extraer(ruta_excel)
        self._logger.debug(f"DUMP PInt: {datos}")
        return datos

    def extraer_alarmas(self, ruta_excel: str) -> list[Alarma]:
        """
        Extrae la lista de alarmas desde el Excel Maestro.

        Args:
            ruta_excel: Ruta al archivo Excel Maestro.

        Returns:
            Lista de objetos Alarma.

        Raises:
            ExcelParsingError: Si no se puede leer el Excel o parsear los datos.
        """
        self._logger.info(f"Extrayendo alarmas de: {ruta_excel}")
        datos: list[Alarma] = self._alarmas.extraer(ruta_excel)
        self._logger.debug(f"DUMP Alarmas: {datos}")
        return datos