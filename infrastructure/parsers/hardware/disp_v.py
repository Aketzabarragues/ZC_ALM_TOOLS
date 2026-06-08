"""
Hardware Parser - DispV
==========================
Extrae la tabla de dispositivos de Valvulas (V) desde el Excel Maestro.

30 columnas (vs 23 de ED/EA/SA) porque las valvulas tienen
campos especificos: S (setpoint), RR (retorno reposo), RT
(retorno trabajo) y 10 columnas Cfg.
"""

from core.models import DispV
from infrastructure.parsers.base_parser import BaseParser

__all__ = ["DispVParser"]


class DispVParser(BaseParser):
    """Parser para extraer la tabla de dispositivos de Valvulas."""

    def extraer(self, ruta_excel: str) -> list[DispV]:
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="DISP_V",
            table_name="Tabla_Disp_V",
            columnas_numericas=[
                # Columnas int del Excel
                "Numero",
                "S.Byte", "S.Bit",
                "RR.Byte", "RR.Bit",
                "RT.Byte", "RT.Bit",
                "Gr.Alarma",
                "PLC.Index",
                "Hmi.Index",
                # Las Cfg.* son strings (SCL crudo), NO entran aqui.
            ],
        )

        dispositivos: list[DispV] = []
        for _, row in df.iterrows():
            dispositivos.append(
                DispV(
                    # 4 atributos del Protocol
                    numero=int(row.get("Numero", 0)),
                    plc_tag=str(row.get("PLC.Tag", "")),
                    plc_comentario=str(row.get("PLC.Comentario", "")),
                    descripcion=str(row.get("Descripcion", "")),
                    # Excel extendido
                    uid=str(row.get("UID", "")),
                    tag=str(row.get("Tag", "")),
                    fat=str(row.get("FAT", "")),
                    s_byte=int(row.get("S.Byte", 0)),
                    s_bit=int(row.get("S.Bit", 0)),
                    rr_byte=int(row.get("RR.Byte", 0)),
                    rr_bit=int(row.get("RR.Bit", 0)),
                    rt_byte=int(row.get("RT.Byte", 0)),
                    rt_bit=int(row.get("RT.Bit", 0)),
                    gr_alarma=int(row.get("Gr.Alarma", 0)),
                    cuadro=str(row.get("Cuadro", "")),
                    observaciones=str(row.get("Observaciones", "")),
                    # PLC
                    plc_tipo=str(row.get("PLC.Tipo", "")),
                    plc_index=int(row.get("PLC.Index", 0)),
                    # HMI
                    hmi_index=int(row.get("Hmi.Index", 0)),
                    hmi_texto=str(row.get("Hmi.Texto", "")),
                    # Cfg (10 campos, todos son lineas SCL crudas)
                    cfg_habilitar=str(row.get("Cfg.Habilitar", "")),
                    cfg_byteretornoreposo=str(row.get("Cfg.ByteRetornoReposo", "")),
                    cfg_bitretornoreposo=str(row.get("Cfg.BitRetornoReposo", "")),
                    cfg_byteretornotrabajo=str(row.get("Cfg.ByteRetornoTrabajo", "")),
                    cfg_bitretornotrabajo=str(row.get("Cfg.BitRetornoTrabajo", "")),
                    cfg_byteactivacion=str(row.get("Cfg.ByteActivacion", "")),
                    cfg_bitactivacion=str(row.get("Cfg.BitActivacion", "")),
                    cfg_habitreposo=str(row.get("Cfg.HabRetReposo", "")),
                    cfg_habitrtrabajo=str(row.get("Cfg.HabRetTrabajo", "")),
                    cfg_grupoalarma=str(row.get("Cfg.GrupoAlarma", "")),
                )
            )
        return dispositivos
