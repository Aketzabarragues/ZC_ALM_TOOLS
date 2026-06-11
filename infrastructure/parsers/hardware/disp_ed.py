"""
Hardware Parser - DispED
==========================
Extrae la tabla de dispositivos de Entradas Digitales (ED)
desde el Excel Maestro.
"""

from core.models import DispED
from infrastructure.parsers.base_parser import BaseParser
from infrastructure.parsers.utils import _safe_str

__all__ = ["DispEDParser"]


class DispEDParser(BaseParser):
    """Parser para extraer la tabla de dispositivos de Entradas Digitales."""

    def extraer(self, ruta_excel: str) -> list[DispED]:
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="DISP_ED",
            table_name="Tabla_Disp_ED",
            columnas_numericas=[
                "Numero",
                "E.Byte",
                "E.Bit",
                "Gr.Alarma",
                "PLC.Index",
                "Hmi.Index",          # NUEVO
            ],
        )

        dispositivos: list[DispED] = []
        for _, row in df.iterrows():
            dispositivos.append(
                DispED(
                    numero=int(row.get("Numero", 0)),
                    plc_tag=str(row.get("PLC.Tag", "")),
                    plc_comentario=str(row.get("PLC.Comentario", "")),
                    descripcion=str(row.get("Descripcion", "")),
                    uid=str(row.get("UID", "")),
                    tag=str(row.get("Tag", "")),
                    fat=str(row.get("FAT", "")),
                    e_byte=int(row.get("E.Byte", 0)),
                    e_bit=int(row.get("E.Bit", 0)),
                    gr_alarma=int(row.get("Gr.Alarma", 0)),
                    cuadro=str(row.get("Cuadro", "")),
                    observaciones=str(row.get("Observaciones", "")),
                    plc_tipo=str(row.get("PLC.Tipo", "")),
                    plc_index=int(row.get("PLC.Index", 0)),
                    hmi_index=int(row.get("Hmi.Index", 0)),
                    hmi_texto=str(row.get("Hmi.Texto", "")),
                    cfg_habilitar=str(row.get("Cfg.Habilitar", "")),
                    cfg_byte_entrada=str(row.get("Cfg.ByteEntrada", "")),
                    cfg_bit_entrada=str(row.get("Cfg.BitEntrada", "")),
                    cfg_grupo_alarma=str(row.get("Cfg.GrupoAlarma", "")),
                    comentario_db=str(row.get("ComentarioDB", "")).strip(),
                )
            )
        return dispositivos
