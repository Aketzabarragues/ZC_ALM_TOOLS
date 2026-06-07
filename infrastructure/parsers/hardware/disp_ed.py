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
            ],
        )

        return [
            DispED(
                # Identidad
                uid=str(row.get("UID", "")),
                # _safe_int no funciona aqui (la columna no se llama literalmente
                # "Numero" en la mayoria de excels, viene de columnas_numericas).
                # Usamos int() directamente sobre el valor coercionado.
                numero=int(row.get("Numero", 0) or 0),
                # Etiqueta
                tag=str(row.get("Tag", "")),
                descripcion=str(row.get("Descripcion", "")),
                # Cableado fisico
                fat=str(row.get("FAT", "")),
                e_byte=int(row.get("E.Byte", 0) or 0),
                e_bit=int(row.get("E.Bit", 0) or 0),
                gr_alarma=int(row.get("Gr.Alarma", 0) or 0),
                cuadro=str(row.get("Cuadro", "")),
                observaciones=str(row.get("Observaciones", "")),
                # Tags PLC
                plc_tag=str(row.get("PLC.Tag", "")),
                plc_tipo=str(row.get("PLC.Tipo", "")),
                plc_index=int(row.get("PLC.Index", 0) or 0),
                plc_comentario=str(row.get("PLC.Comentario", "")),
                # CGF (Configuracion de Grupo Funcional)
                cgf_habilitar=str(row.get("CGF.Habilitar", "")),
                cgf_byte_entrada=str(row.get("CGF.ByteEntrada", "")),
                cgf_bit_entrada=str(row.get("CGF.BitEntrada", "")),
                cgf_grupo_alarma=str(row.get("CGF.GrupoAlarma", "")),
            )
            for _, row in df.iterrows()
        ]
