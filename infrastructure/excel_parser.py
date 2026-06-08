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
    DispEA,
    DispED,
    DispM,
    DispM_VF,
    DispSA,
    DispV,
    PInt,
    PReal,
    Proceso,
)
from infrastructure.parsers.hardware import (
    DispEAParser,
    DispEDParser,
    DispMParser,
    DispMVFarser,
    DispSAParser,
    DispVParser,
)
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
    _DN_NUM_DISP_EA: str = "Num_Disp_EA"
    _DN_NUM_DISP_SA: str = "Num_Disp_SA"
    _DN_NUM_DISP_V: str = "Num_Disp_V"
    _DN_NUM_DISP_M: str = "Num_Disp_M"
    _DN_NUM_DISP_M_VF: str = "Num_Disp_M_VF"

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
        self._disp_ea: DispEAParser = DispEAParser()
        self._disp_sa: DispSAParser = DispSAParser()
        self._disp_v: DispVParser = DispVParser()
        self._disp_m: DispMParser = DispMParser()
        self._disp_m_vf: DispMVFarser = DispMVFarser()

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

    def extraer_disp_ea(self, ruta_excel: str) -> list[DispEA]:
        """Extrae la lista de dispositivos de Entradas Analogicas."""
        self._logger.info(f"Extrayendo DispEA de: {ruta_excel}")
        datos: list[DispEA] = self._disp_ea.extraer(ruta_excel)
        self._logger.debug(f"DUMP DispEA: {datos}")
        return datos

    def extraer_disp_sa(self, ruta_excel: str) -> list[DispSA]:
        """Extrae la lista de dispositivos de Salidas Analogicas."""
        self._logger.info(f"Extrayendo DispSA de: {ruta_excel}")
        datos: list[DispSA] = self._disp_sa.extraer(ruta_excel)
        self._logger.debug(f"DUMP DispSA: {datos}")
        return datos

    def extraer_disp_v(self, ruta_excel: str) -> list[DispV]:
        """Extrae la lista de dispositivos de Valvulas."""
        self._logger.info(f"Extrayendo DispV de: {ruta_excel}")
        datos: list[DispV] = self._disp_v.extraer(ruta_excel)
        self._logger.debug(f"DUMP DispV: {datos}")
        return datos

    def extraer_disp_m(self, ruta_excel: str) -> list[DispM]:
        """Extrae la lista de dispositivos de Motores."""
        self._logger.info(f"Extrayendo DispM de: {ruta_excel}")
        datos: list[DispM] = self._disp_m.extraer(ruta_excel)
        self._logger.debug(f"DUMP DispM: {datos}")
        return datos

    def extraer_disp_m_vf(self, ruta_excel: str) -> list[DispM_VF]:
        """Extrae la lista de dispositivos de Motores Variadores de Frecuencia."""
        self._logger.info(f"Extrayendo DispM_VF de: {ruta_excel}")
        datos: list[DispM_VF] = self._disp_m_vf.extraer(ruta_excel)
        self._logger.debug(f"DUMP DispM_VF: {datos}")
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
                dims.num_disp_ed = self._leer_defined_name(
                    wb, self._DN_NUM_DISP_ED, "Num_Disp_ED"
                )
                dims.num_disp_ea = self._leer_defined_name(
                    wb, self._DN_NUM_DISP_EA, "Num_Disp_EA"
                )
                dims.num_disp_sa = self._leer_defined_name(
                    wb, self._DN_NUM_DISP_SA, "Num_Disp_SA"
                )
                dims.num_disp_v = self._leer_defined_name(
                    wb, self._DN_NUM_DISP_V, "Num_Disp_V"
                )
                dims.num_disp_m = self._leer_defined_name(
                    wb, self._DN_NUM_DISP_M, "Num_Disp_M"
                )
                dims.num_disp_m_vf = self._leer_defined_name(
                    wb, self._DN_NUM_DISP_M_VF, "Num_Disp_M_VF"
                )
            finally:
                wb.close()
        except Exception as e:
            # Fallback SEGURO: si openpyxl cambia la API o el Excel
            # no tiene defined_names, devolvemos dims=0 y la app sigue.
            self._logger.warning(f"Error extrayendo dimensiones: {e}")

        return dims

    def _leer_defined_name(
        self,
        wb: openpyxl.Workbook,
        dn_name: str,
        log_name: str,
    ) -> int:
        """
        Helper: lee un defined name y devuelve su valor como int.
        Retorna 0 si no existe o si la celda no se puede resolver.
        """
        dn = wb.defined_names.get(dn_name)
        if dn is None:
            self._logger.warning(
                f"Defined name '{dn_name}' no encontrado en el Excel."
            )
            return 0

        destinations = list(dn.destinations)
        if not destinations:
            self._logger.warning(
                f"Defined name '{dn_name}' sin destinos resolubles."
            )
            return 0

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

        valor_int = _safe_int(valor)
        self._logger.info(
            f"{log_name} resuelto: {valor_int} "
            f"(hoja='{sheet_title}', celda='{coord}')"
        )
        return valor_int
