"""
Hardware Parser - DispSA
==========================
Extrae la tabla de dispositivos de Salidas Analogicas (SA)
desde el Excel Maestro.
"""

from core.models import DispSA
from infrastructure.parsers.base_parser import BaseParser

__all__ = ["DispSAParser"]


class DispSAParser(BaseParser):
    """Parser para extraer la tabla de dispositivos de Salidas Analogicas."""

    def extraer(self, ruta_excel: str) -> list[DispSA]:
        df = self._leer_tabla(
            ruta_excel=ruta_excel,
            sheet_name="DISP_SA",
            table_name="Tabla_Disp_SA",
            columnas_numericas=[
                "Numero",
                "E.Byte",
                "RII",
                "RSI",
                "Gr.Alarma",
                "PLC.Index",
                "Hmi.Index",
            ],
        )

        # NOTA: RII y RSI son float en el Excel (celdas "real"). En el
        # BaseParser se castean con .astype(int) por la lista columnas_numericas.
        # Aqui necesitamos float, asi que los leemos con float() manualmente
        # en el bucle (seran int por el cast del BaseParser; los reconvertimos).
        dispositivos: list[DispSA] = []
        for _, row in df.iterrows():
            dispositivos.append(
                DispSA(
                    # 4 atributos del Protocol
                    numero=int(row.get("Numero", 0)),
                    plc_tag=str(row.get("PLC.Tag", "")),
                    plc_comentario=str(row.get("PLC.Comentario", "")),
                    descripcion=str(row.get("Descripcion", "")),
                    # Excel extendido
                    uid=str(row.get("UID", "")),
                    tag=str(row.get("Tag", "")),
                    fat=str(row.get("FAT", "")),
                    e_byte=int(row.get("E.Byte", 0)),
                    unidades=str(row.get("UNIDADES", "")),
                    # RII / RSI eran float; el BaseParser los paso a int
                    # por estar en columnas_numericas. Los reconstruimos.
                    rii=float(int(row.get("RII", 0))),
                    rsi=float(int(row.get("RSI", 0))),
                    gr_alarma=int(row.get("Gr.Alarma", 0)),
                    cuadro=str(row.get("Cuadro", "")),
                    observaciones=str(row.get("Observaciones", "")),
                    # PLC
                    plc_tipo=str(row.get("PLC.Tipo", "")),
                    plc_index=int(row.get("PLC.Index", 0)),
                    # HMI
                    hmi_index=int(row.get("Hmi.Index", 0)),
                    hmi_texto=str(row.get("Hmi.Texto", "")),
                    # Cfg (lineas SCL crudas)
                    cfg_habilitar=str(row.get("Cfg.Habilitar", "")),
                    cfg_byte_entrada=str(row.get("Cfg.ByteEntrada", "")),
                    cfg_escaladomin=str(row.get("Cfg.EscaladoMin", "")),
                    cfg_escaladomax=str(row.get("Cfg.EscaladoMax", "")),
                    cfg_grupo_alarma=str(row.get("Cfg.GrupoAlarma", "")),
                    comentario_db=str(row.get("ComentarioDB", "")).strip(),
                )
            )
        return dispositivos
