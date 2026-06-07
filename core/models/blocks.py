"""
Core Layer - Block Domain Model
================================
DTO BloquePLC: la radiografia de un bloque dentro de TIA Portal.

Se usa en el Radar Anti-colisiones para comparar objetos
existentes en el PLC con los que se van a generar.
"""

from dataclasses import dataclass

__all__ = ["BloquePLC"]


@dataclass
class BloquePLC:
    """
    Representa la radiografía de un bloque dentro de TIA Portal.

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
