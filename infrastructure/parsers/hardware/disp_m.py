"""
Hardware Parser - DispM
==========================
Extrae la tabla de dispositivos de Motores (M) desde el Excel Maestro.

30 columnas (vs 23 de ED/EA/SA, 30 de V) porque los motores tienen
campos especificos: S (setpoint), RT (retorno termico), RM (retorno
marcha) y 10 columnas Cfg.
"""

from core.models import DispM
from infrastructure.parsers.base_parser import BaseParser

__all__ = ["DispMParser"]


class DispMParser(BaseParser):
    """Parser para extraer la tabla de dispositivos de Motores."""

    def extraer(self, ruta_excel: str) -> list[DispM]:
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="DISP_M",
            table_name="Tabla_Disp_M",
            columnas_numericas=[
                # Columnas int del Excel
                "Numero",
                "S.Byte", "S.Bit",
                "RT.Byte", "RT.Bit",
                "RM.Byte", "RM.Bit",
                "Gr.Alarma",
                "PLC.Index",
                "Hmi.Index",
                # Las Cfg.* son strings (SCL crudo), NO entran aqui.
            ],
        )

        dispositivos: list[DispM] = []
        for _, row in df.iterrows():
            dispositivos.append(
                DispM(
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
                    rt_byte=int(row.get("RT.Byte", 0)),
                    rt_bit=int(row.get("RT.Bit", 0)),
                    rm_byte=int(row.get("RM.Byte", 0)),
                    rm_bit=int(row.get("RM.Bit", 0)),
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
                    cfg_byteretornotermico=str(row.get("Cfg.ByteRetornoTermico", "")),
                    cfg_bitretornotermico=str(row.get("Cfg.BitRetornoTermico", "")),
                    cfg_byteconfmarcha=str(row.get("Cfg.ByteConfMarcha", "")),
                    cfg_bitconfmarcha=str(row.get("Cfg.BitConfMarcha", "")),
                    cfg_byteactivacion=str(row.get("Cfg.ByteActivacion", "")),
                    cfg_bitactivacion=str(row.get("Cfg.BitActivacion", "")),
                    cfg_habrettermico=str(row.get("Cfg.HabRetTermico", "")),
                    cfg_habretconfmarcha=str(row.get("Cfg.HabRetConfMarcha", "")),
                    cfg_grupoalarma=str(row.get("Cfg.GrupoAlarma", "")),
                )
            )
        return dispositivos
