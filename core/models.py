"""
Core Layer - Domain Models
==========================
Puro dominio, sin dependencias de frameworks o servicios externos.
"""

from dataclasses import dataclass

__all__ = ["Proceso", "PReal", "PInt", "Alarma", "BloquePLC"]


@dataclass
class Proceso:
    """
    Modelo de dominio para un proceso industrial.

    Attributes:
        uid: Identificador único del proceso.
        nombre: Nombre descriptivo del proceso.
        codigo: Código interno del proceso.
        num_etapas: Número de etapas del proceso.
        p_real: Valor de presión real.
        p_int: Valor de presión interna.
        alarmas: Cantidad de alarmas asociadas.
    """
    uid: int
    nombre: str
    codigo: str
    num_etapas: int
    p_real: int
    p_int: int
    alarmas: int

    @property
    def db_preal_numero(self) -> int:
        """Número del DB de parámetros reales."""
        return 3000 + self.uid

    @property
    def db_pint_numero(self) -> int:
        """Número del DB de parámetros enteros."""
        return 3000 + self.uid + 1

    @property
    def db_alm_numero(self) -> int:
        """Número del DB de alarmas."""
        return 5000 + self.uid

    @property
    def db_preal_nombre(self) -> str:
        """Nombre completo del DB de parámetros reales."""
        return f"DB{self.db_preal_numero}_{self.codigo}_PREAL"

    @property
    def db_pint_nombre(self) -> str:
        """Nombre completo del DB de parámetros enteros."""
        return f"DB{self.db_pint_numero}_{self.codigo}_PINT"

    @property
    def db_alm_nombre(self) -> str:
        """Nombre completo del DB de alarmas."""
        return f"DB{self.db_alm_numero}_{self.codigo}_ALM"

    @property
    def alm_hmi(self) -> int:
        """Calcula las Words necesarias para el HMI."""
        return max(0, (self.alarmas // 16) - 1)


@dataclass
class PReal:
    """
    Representa un parámetro real (Tabla_PReal).

    Attributes:
        uid: Identificador único del parámetro (texto, ej. 'PR_1_001').
        numero: Número secuencial del parámetro (texto).
        proceso: Nombre del proceso al que pertenece.
        num_db: Número de bloque de datos (DB).
        producto: Nombre del producto asociado.
        tipo: Tipo de dato (ej. Real, LReal).
        descripcion: Descripción del parámetro.
        comentario_db: Comentario en el DB de TIA Portal.
        visibilidad: Visibilidad del parámetro.
    """
    uid: str
    numero: str
    proceso: str
    num_db: int
    producto: str
    tipo: str
    descripcion: str
    comentario_db: str
    visibilidad: str


@dataclass
class PInt:
    """
    Representa un parámetro entero (Tabla_PInt).

    Attributes:
        uid: Identificador único del parámetro (texto, ej. 'PI_1_001').
        numero: Número secuencial del parámetro (texto).
        proceso: Nombre del proceso al que pertenece.
        num_db: Número de bloque de datos (DB).
        producto: Nombre del producto asociado.
        tipo: Tipo de dato (ej. Int, DInt).
        descripcion: Descripción del parámetro.
        comentario_db: Comentario en el DB de TIA Portal.
        visibilidad: Visibilidad del parámetro.
    """
    uid: str
    numero: str
    proceso: str
    num_db: int
    producto: str
    tipo: str
    descripcion: str
    comentario_db: str
    visibilidad: str


@dataclass
class Alarma:
    """
    Representa una alarma (Tabla_Alarmas).

    Nota: La tabla de Alarmas no incluye columna de Visibilidad.

    Attributes:
        uid: Identificador único de la alarma (texto, ej. 'AL_1_001').
        numero: Número secuencial de la alarma (texto).
        proceso: Nombre del proceso al que pertenece.
        num_db: Número de bloque de datos (DB).
        descripcion: Descripción de la alarma.
        comentario_db: Comentario en el DB de TIA Portal.
    """
    uid: str
    numero: str
    proceso: str
    num_db: int
    descripcion: str
    comentario_db: str


@dataclass
class BloquePLC:
    """
    Representa la radiografía de un bloque dentro de TIA Portal.

    Se usa en el Radar Anti-colisiones para comparar objetos
    existentes en el PLC con los que se van a generar.

    Attributes:
        nombre: Nombre completo del bloque (ej. "DB3110_Datos").
        numero: Número del bloque (ej. 3110 para DB3110).
        tipo: Tipo de bloque (ej. "DB", "FC", "FB", "OB").
        ruta: Ruta en el árbol de TIA Portal.
    """
    nombre: str
    numero: int
    tipo: str
    ruta: str