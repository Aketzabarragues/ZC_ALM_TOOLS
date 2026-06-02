"""
Infrastructure Layer - Parsers Module
======================================
Submódulo para parsers de Excel organizados por responsabilidad.
"""

from infrastructure.parsers.base_parser import BaseParser, ExcelParsingError

__all__ = ["BaseParser", "ExcelParsingError"]