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
from typing import Protocol

__all__ = ["DispositivoHardware", "DispED", "DimensionesDispositivos"]


class DispositivoHardware(Protocol):
    """
    Contrato base (Protocol) que cualquier dispositivo hardware
    (ED, EA, SD, Motor, etc.) debe cumplir para ser gestionado
    por SincronizarDispositivosUseCase.

    Define solo las propiedades MINIMAS que el caso de uso necesita.
    Implementaciones concretas (DispED, futuros DispEA/DispSD) pueden
    tener muchos mas atributos; este contrato no los obliga.

    Cumplimiento estatico (duck typing): cualquier clase con estos 4
    atributos (con los tipos correctos) es aceptada por Pylance/Mypy
    sin necesidad de herencia explicita.
    """
    numero: int
    plc_tag: str
    plc_comentario: str
    descripcion: str


@dataclass
class DispED:
    """
    Modelo de dominio para un dispositivo de Entrada Digital (ED).

    Representa una senal fisica que llega al PLC desde el campo
    (pulsadores, finales de carrera, sensores digitales, etc.).

    Attributes:
        uid, numero, tag, descripcion, fat, e_byte, e_bit, gr_alarma,
        cuadro, observaciones, plc_tag, plc_tipo, plc_index, plc_comentario,
        hmi_index, hmi_texto, cfg_habilitar, cfg_byte_entrada, cfg_bit_entrada,
        cfg_grupo_alarma.
        (Las rutas de TIA Portal han salido del modelo de dominio
        y viven ahora en config_manager.get_hardware_tia_config('ed').)
    """

    # --- Atributos mínimos del Protocol DispositivoHardware ---
    numero: int = 0
    plc_tag: str = ""
    plc_comentario: str = ""
    descripcion: str = ""

    # --- Atributos extendidos del Excel ---
    uid: str = ""
    tag: str = ""
    fat: str = ""
    e_byte: int = 0
    e_bit: int = 0
    gr_alarma: int = 0
    cuadro: str = ""
    observaciones: str = ""

    # --- Datos PLC adicionales ---
    plc_tipo: str = ""
    plc_index: int = 0

    # --- Datos HMI ---
    hmi_index: int = 0
    hmi_texto: str = ""

    # --- Datos de Configuración (Cfg) ---
    # Los Cfg almacenan líneas de código SCL crudas (ej:
    # 'DB2000_ED.ED[1].Config_Habilitar := TRUE;') que se inyectan
    # en el proyecto TIA al sincronizar. Por eso son str, no bool/int.
    cfg_habilitar: str = ""
    cfg_byte_entrada: str = ""
    cfg_bit_entrada: str = ""
    cfg_grupo_alarma: str = ""


@dataclass
class DimensionesDispositivos:
    """
    Dimensiones maximas (N_MAX) de los arrays de dispositivos hardware.

    Cada campo representa el contador maximo de elementos que
    un proceso industrial puede llegar a tener, leido de las
    celdas nombradas (Defined Names) del Excel Maestro.
    """
    num_disp_ed: int = 0
