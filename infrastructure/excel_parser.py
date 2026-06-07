"""
Infrastructure Layer - Excel Parser
===================================
Fachada para orquestar la extracción de datos del Excel Maestro.
Delega la lógica específica a los parsers especializados.
"""

import logging

import openpyxl

from core.models import (
    Alarma,
    DimensionesDispositivos,
    DispED,
    PInt,
    PReal,
    Proceso,
)
from infrastructure.parsers.hardware import DispEDParser
from infrastructure.parsers.software import (
    AlarmasParser,
    PIntParser,
    PRealParser,
    ProcesosParser,
)
from infrastructure.parsers.utils import _safe_int

__all__ = ["ExcelParser"]


class ExcelParser:
    """
    Fachada para orquestar la extracción de datos del Excel Maestro.

    Uso:
        parser = ExcelParser()
        procesos = parser.extraer_procesos("ruta/al/excel.xlsx")
        dimensiones = parser.extraer_dimensiones("ruta/al/excel.xlsx")
    """

    # Constantes para defined names de openpyxl
    _DN_NUM_DISP_ED: str = "Num_Disp_ED"

    def __init__(self) -> None:
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )
        # Parsers de SOFTWARE
        self._procesos: ProcesosParser = ProcesosParser()
        self._preal: PRealParser = PRealParser()
        self._pint: PIntParser = PIntParser()
        self._alarmas: AlarmasParser = AlarmasParser()
        # Parsers de HARDWARE (Fase 1)
        self._disp_ed: DispEDParser = DispEDParser()

    # ------------------------------------------------------------------ #
    #  Software
    # ------------------------------------------------------------------ #

    def extraer_procesos(self, ruta_excel: str) -> list[Proceso]:
        self._logger.info(f"Extrayendo procesos de: {ruta_excel}")
        datos: list[Proceso] = self._procesos.extraer(ruta_excel)
        self._logger.debug(f"DUMP Procesos: {datos}")
        return datos

    def extraer_preal(self, ruta_excel: str) -> list[PReal]:
        self._logger.info(f"Extrayendo parámetros reales de: {ruta_excel}")
        datos: list[PReal] = self._preal.extraer(ruta_excel)
        self._logger.debug(f"DUMP PReal: {datos}")
        return datos

    def extraer_pint(self, ruta_excel: str) -> list[PInt]:
        self._logger.info(f"Extrayendo parámetros enteros de: {ruta_excel}")
        datos: list[PInt] = self._pint.extraer(ruta_excel)
        self._logger.debug(f"DUMP PInt: {datos}")
        return datos

    def extraer_alarmas(self, ruta_excel: str) -> list[Alarma]:
        self._logger.info(f"Extrayendo alarmas de: {ruta_excel}")
        datos: list[Alarma] = self._alarmas.extraer(ruta_excel)
        self._logger.debug(f"DUMP Alarmas: {datos}")
        return datos

    # ------------------------------------------------------------------ #
    #  Hardware (Fase 1)
    # ------------------------------------------------------------------ #

    def extraer_disp_ed(self, ruta_excel: str) -> list[DispED]:
        """Extrae la lista de dispositivos de Entradas Digitales."""
        self._logger.info(f"Extrayendo DispED de: {ruta_excel}")
        datos: list[DispED] = self._disp_ed.extraer(ruta_excel)
        self._logger.debug(f"DUMP DispED: {datos}")
        return datos

    def extraer_dimensiones(self, ruta_excel: str) -> DimensionesDispositivos:
        """
        Lee las celdas nombradas (Defined Names) del Excel Maestro
        que contienen las dimensiones N_MAX de los dispositivos.

        Por ahora solo Num_Disp_ED. A medida que se agreguen mas
        dispositivos (SD, ANA, etc.), se iran añadiendo aqui.

        En openpyxl moderno, `defined_names` se comporta como un dict:
        `wb.defined_names.get("Num_Disp_ED")` devuelve el `DefinedName`
        o None. `dn.destinations` es un generador que devuelve
        tuplas (nombre_hoja, coordenada_limpia).
        """
        self._logger.info(f"Extrayendo dimensiones (defined names) de: {ruta_excel}")
        dims = DimensionesDispositivos()

        try:
            wb = openpyxl.load_workbook(ruta_excel, data_only=True)
            try:
                # API moderna: defined_names es dict-like.
                dn = wb.defined_names.get(self._DN_NUM_DISP_ED)
                if dn is None:
                    self._logger.warning(
                        f"Defined name '{self._DN_NUM_DISP_ED}' no encontrado en el Excel."
                    )
                    return dims

                destinations = list(dn.destinations)
                if not destinations:
                    self._logger.warning(
                        f"Defined name '{self._DN_NUM_DISP_ED}' sin destinos resolubles."
                    )
                    return dims

                sheet_title, coord = destinations[0]
                cell_obj = wb[sheet_title][coord]
                if isinstance(cell_obj, tuple):
                    # Si es un rango, tomamos la primera celda superior izquierda
                    valor = (
                        cell_obj[0][0].value
                        if isinstance(cell_obj[0], tuple)
                        else cell_obj[0].value
                    )
                else:
                    valor = cell_obj.value

                dims.num_disp_ed = _safe_int(valor)
                self._logger.info(
                    f"Num_Disp_ED resuelto: {dims.num_disp_ed} "
                    f"(hoja='{sheet_title}', celda='{coord}')"
                )
            finally:
                wb.close()
        except Exception as e:
            # Fallback SEGURO: si openpyxl cambia la API o el Excel
            # no tiene defined_names, devolvemos dims=0 y la app sigue.
            self._logger.warning(f"Error extrayendo dimensiones: {e}")

        return dims
