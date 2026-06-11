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

__all__ = ["DispositivoHardware", "DispED", "DispEA", "DispSA", "DispV", "DispM", "DispM_VF", "DimensionesDispositivos"]


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
    comentario_db: str


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
    comentario_db: str = ""


@dataclass
class DispEA:
    """
    Modelo de dominio para un dispositivo de Entrada Analogica (EA).

    Representa una senal analogica (4-20mA, 0-10V, RTD, etc.) que
    llega al PLC desde el campo (sensores de temperatura, presion,
    caudal, etc.).

    Attributes:
        uid, numero, tag, descripcion, fat, e_byte, unidades, rii, rsi,
        gr_alarma, cuadro, observaciones, plc_tag, plc_tipo, plc_index,
        plc_comentario, hmi_index, hmi_texto, cfg_habilitar, cfg_byte_entrada,
        cfg_escaladomin, cfg_escaladomax, cfg_grupo_alarma.
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
    unidades: str = ""
    rii: float = 0.0   # Rango Inferior Ingenieria
    rsi: float = 0.0   # Rango Superior Ingenieria
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
    # Lineas SCL crudas, igual que en DispED.
    cfg_habilitar: str = ""
    cfg_byte_entrada: str = ""
    cfg_escaladomin: str = ""
    cfg_escaladomax: str = ""
    cfg_grupo_alarma: str = ""
    comentario_db: str = ""


@dataclass
class DispSA:
    """
    Modelo de dominio para un dispositivo de Salida Analogica (SA).

    Representa una senal analogica de salida (4-20mA, 0-10V) que
    el PLC envia al campo para actuadores (valvulas proporcionales,
    variadores de frecuencia, reguladores, etc.).

    Misma estructura que DispEA (entrada analogica) pero como SALIDA.
    Hereda los 4 atributos del Protocol DispositivoHardware.
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
    unidades: str = ""
    rii: float = 0.0   # Rango Inferior Ingenieria
    rsi: float = 0.0   # Rango Superior Ingenieria
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
    # Lineas SCL crudas, igual que en DispED/DispEA.
    cfg_habilitar: str = ""
    cfg_byte_entrada: str = ""
    cfg_escaladomin: str = ""
    cfg_escaladomax: str = ""
    cfg_grupo_alarma: str = ""
    comentario_db: str = ""


@dataclass
class DispV:
    """
    Modelo de dominio para un dispositivo de Valvula (V).

    Representa una valvula industrial con setpoint, retorno de reposo
    y retorno de trabajo. 30 atributos derivados directamente de
    la hoja Excel 'DISP_V' del Excel Maestro.

    Mapeo de columnas -> atributos (snake_case):
        UID, Numero, Tag, Descripcion, FAT,
        S.Byte, S.Bit, RR.Byte, RR.Bit, RT.Byte, RT.Bit,
        Gr.Alarma, Cuadro, Observaciones,
        PLC.Tag, PLC.Tipo, PLC.Index, PLC.Comentario,
        Hmi.Index, Hmi.Texto,
        Cfg.Habilitar, Cfg.ByteRetornoReposo, Cfg.BitRetornoReposo,
        Cfg.ByteRetornoTrabajo, Cfg.BitRetornoTrabajo,
        Cfg.ByteActivacion, Cfg.BitActivacion,
        Cfg.HabRetReposo, Cfg.HabRetTrabajo, Cfg.GrupoAlarma.
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
    s_byte: int = 0     # Setpoint byte
    s_bit: int = 0      # Setpoint bit
    rr_byte: int = 0    # Retorno Reposo byte
    rr_bit: int = 0     # Retorno Reposo bit
    rt_byte: int = 0    # Retorno Trabajo byte
    rt_bit: int = 0     # Retorno Trabajo bit
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
    # 10 campos Cfg con líneas SCL crudas.
    cfg_habilitar: str = ""
    cfg_byteretornoreposo: str = ""
    cfg_bitretornoreposo: str = ""
    cfg_byteretornotrabajo: str = ""
    cfg_bitretornotrabajo: str = ""
    cfg_byteactivacion: str = ""
    cfg_bitactivacion: str = ""
    cfg_habitreposo: str = ""
    cfg_habitrtrabajo: str = ""
    cfg_grupoalarma: str = ""
    comentario_db: str = ""


@dataclass
class DispM:
    """
    Modelo de dominio para un dispositivo de Motor (M).

    Representa un motor industrial con setpoint, retorno termico y
    retorno de marcha. 30 atributos derivados directamente de la hoja
    Excel 'DISP_M' del Excel Maestro.

    Mapeo de columnas -> atributos (snake_case):
        UID, Numero, Tag, Descripcion, FAT,
        S.Byte, S.Bit, RT.Byte, RT.Bit, RM.Byte, RM.Bit,
        Gr.Alarma, Cuadro, Observaciones,
        PLC.Tag, PLC.Tipo, PLC.Index, PLC.Comentario,
        Hmi.Index, Hmi.Texto,
        Cfg.Habilitar, Cfg.ByteRetornoTermico, Cfg.BitRetornoTermico,
        Cfg.ByteConfMarcha, Cfg.BitConfMarcha,
        Cfg.ByteActivacion, Cfg.BitActivacion,
        Cfg.HabRetTermico, Cfg.HabRetConfMarcha, Cfg.GrupoAlarma.
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
    s_byte: int = 0     # Setpoint byte
    s_bit: int = 0      # Setpoint bit
    rt_byte: int = 0    # Retorno Termico byte
    rt_bit: int = 0     # Retorno Termico bit
    rm_byte: int = 0    # Retorno Marcha byte
    rm_bit: int = 0     # Retorno Marcha bit
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
    # 10 campos Cfg con líneas SCL crudas.
    cfg_habilitar: str = ""
    cfg_byteretornotermico: str = ""
    cfg_bitretornotermico: str = ""
    cfg_byteconfmarcha: str = ""
    cfg_bitconfmarcha: str = ""
    cfg_byteactivacion: str = ""
    cfg_bitactivacion: str = ""
    cfg_habrettermico: str = ""
    cfg_habretconfmarcha: str = ""
    cfg_grupoalarma: str = ""
    comentario_db: str = ""


@dataclass
class DispM_VF:
    """
    Modelo de dominio para un Motor Variador de Frecuencia (M_VF).

    Representa un variador de frecuencia industrial con setpoint,
    retorno termico, retorno de marcha, salida analogica y
    confirmacion de marcha. 32 atributos derivados directamente
    de la hoja Excel 'DISP_M_VF' del Excel Maestro.

    Mapeo de columnas -> atributos (snake_case):
        UID, Numero, Tag, Descripcion, FAT,
        S.Byte, S.Bit, RT.Byte, RT.Bit, RM.Byte, RM.Bit, SA.Byte,
        Gr.Alarma, Cuadro, Observaciones,
        PLC.Tag, PLC.Tipo, PLC.Index, PLC.Comentario,
        Hmi.Index, Hmi.Texto,
        Cfg.Habilitar, Cfg.ByteRetornoTermico, Cfg.BitRetornoTermico,
        Cfg.ByteConfMarcha, Cfg.BitConfMarcha,
        Cfg.ByteActivacion, Cfg.BitActivacion,
        Cfg.ByteAnalogica,
        Cfg.HabRetTermico, Cfg.HabRetConfMarcha, Cfg.GrupoAlarma.
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
    s_byte: int = 0     # Setpoint byte
    s_bit: int = 0      # Setpoint bit
    rt_byte: int = 0    # Retorno Termico byte
    rt_bit: int = 0     # Retorno Termico bit
    rm_byte: int = 0    # Retorno Marcha byte
    rm_bit: int = 0     # Retorno Marcha bit
    sa_byte: int = 0    # Salida Analogica byte (referencia de velocidad al variador)
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
    # 11 campos Cfg con líneas SCL crudas.
    cfg_habilitar: str = ""
    cfg_byteretornotermico: str = ""
    cfg_bitretornotermico: str = ""
    cfg_byteconfmarcha: str = ""
    cfg_bitconfmarcha: str = ""
    cfg_byteactivacion: str = ""
    cfg_bitactivacion: str = ""
    cfg_byteanalogica: str = ""   # Byte para el setpoint analogico del variador
    cfg_habrettermico: str = ""
    cfg_habretconfmarcha: str = ""
    cfg_grupoalarma: str = ""
    comentario_db: str = ""


@dataclass
class DimensionesDispositivos:
    """
    Dimensiones maximas (N_MAX) de los arrays de dispositivos hardware.

    Cada campo representa el contador maximo de elementos que
    un proceso industrial puede llegar a tener, leido de las
    celdas nombradas (Defined Names) del Excel Maestro.
    """
    num_disp_ed: int = 0
    num_disp_ea: int = 0
    num_disp_sa: int = 0
    num_disp_v: int = 0
    num_disp_m: int = 0
    num_disp_m_vf: int = 0
