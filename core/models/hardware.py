"""
Core Layer - Hardware Domain Models
=====================================
DTOs del dominio de HARDWARE (Fase 1): dispositivos fisicos
que se cablean a las entradas del PLC (ED = Entradas Digitales,
SD = Salidas Digitales, ANA = Analogicas, etc.).

Estos modelos representan el domino LOGICO de la aplicacion
y NO deben depender de TIA Portal, infrastructure ni de
librerias externas.
"""

from dataclasses import dataclass
from typing import ClassVar

__all__ = ["DispED", "DimensionesDispositivos"]


@dataclass
class DispED:
    """
    Modelo de dominio para un dispositivo de Entrada Digital (ED).

    Representa una senal fisica que llega al PLC desde el campo
    (pulsadores, finales de carrera, sensores digitales, etc.).

    Attributes:
        TIA_DB_NAME: Nombre canonico del DB en TIA Portal (placeholder Fase 2).
        TIA_TAG_TABLE: Nombre canonico de la tabla de tags PLC.
        TIA_CONFIG_CONSTANT: Nombre de la constante de dimensionamiento.
    """
    # --- Metadatos de TIA Portal (PLACEHOLDERS Fase 2) ---
    TIA_DB_NAME: ClassVar[str] = "DB2000_ED"
    TIA_DB_ARRAY_NAME: ClassVar[str] = "ED"
    TIA_TAG_TABLE: ClassVar[str] = "2000_Disp_ED"
    TIA_CONFIG_TABLE: ClassVar[str] = "000_Config_Dispositivos"
    TIA_CONFIG_CONSTANT: ClassVar[str] = "N_MAX_DISP_ED"

    # --- Atributos leidos del Excel ---
    uid: str
    numero: int = 0
    tag: str = ""
    descripcion: str = ""
    fat: str = ""
    e_byte: int = 0
    e_bit: int = 0
    gr_alarma: int = 0
    cuadro: str = ""
    observaciones: str = ""
    plc_tag: str = ""
    plc_tipo: str = ""
    plc_index: int = 0
    plc_comentario: str = ""
    cgf_habilitar: str = ""
    cgf_byte_entrada: str = ""
    cgf_bit_entrada: str = ""
    cgf_grupo_alarma: str = ""


@dataclass
class DimensionesDispositivos:
    """
    Dimensiones maximas (N_MAX) de los arrays de dispositivos hardware.

    Cada campo representa el contador maximo de elementos que
    un proceso industrial puede llegar a tener, leido de las
    celdas nombradas (Defined Names) del Excel Maestro.
    """
    num_disp_ed: int = 0
