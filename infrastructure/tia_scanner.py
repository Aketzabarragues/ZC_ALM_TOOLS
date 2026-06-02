"""
Infrastructure Layer - TIA Portal Scanner
=========================================
Escanea recursivamente los bloques de un PLC y construye un caché en memoria.
"""

import logging
import re
from typing import Any

from core.models import BloquePLC


class TIAScannerError(Exception):
    """Base exception for TIA Scanner errors."""
    pass


class TIAScanner:
    """
    Escáner especializado en extraer la estructura de bloques de un PLC.
    Construye un caché en memoria para evitar escaneos recursivos constantes.
    """

    def __init__(self) -> None:
        """Inicializa el escáner con un logger y caché vacío."""
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )
        self._blocks_cache: dict[str, BloquePLC] = {}
        self._current_plc_name: str | None = None

    def clear_cache(self) -> None:
        """Vacía el diccionario de caché, preparando para un nuevo escaneo."""
        self._logger.debug("Limpiando caché de bloques...")
        self._blocks_cache.clear()
        self._current_plc_name = None

    def build_cache(self, plc_name: str, plc_object: Any, force: bool = False) -> dict[str, BloquePLC]:
        """
        Construye el caché de bloques mediante escaneo profundo del PLC.

        Args:
            plc_name: Nombre del PLC a escanear.
            plc_object: Objeto Plc de TIA Portal.
            force: Si True, fuerza el re-escaneo incluso si ya existe caché.

        Returns:
            Diccionario con bloques: clave en minúsculas, valor BloquePLC.
        """
        if self._blocks_cache and not force:
            self._logger.info(f"Caché ya existente para '{plc_name}'. Usa force=True para regenerar.")
            return self._blocks_cache

        self._logger.info(f"Construyendo caché para PLC '{plc_name}'...")
        self.clear_cache()
        self._current_plc_name = plc_name

        try:
            program_blocks = plc_object.get_program_blocks()
            self._scan_group_recursive(program_blocks, plc_name)
            
            self._logger.info(f"Caché construido: {len(self._blocks_cache)} bloques encontrados.")
            return self._blocks_cache

        except Exception as e:
            self._logger.error(f"Error durante el escaneo del PLC '{plc_name}': {e}")
            raise TIAScannerError(f"Fallo en build_cache para {plc_name}") from e

    def _scan_group_recursive(self, group_or_blocks: Any, plc_name: str) -> None:
        """
        Escanea recursivamente grupos y bloques del PLC.
        Maneja bloques protegidos capturando excepciones COM.
        """
        # Procesar bloques directos
        blocks: list[Any] = []
        try:
            if hasattr(group_or_blocks, 'get_blocks'):
                blocks = group_or_blocks.get_blocks()
            elif hasattr(group_or_blocks, 'Blocks'):
                blocks = group_or_blocks.Blocks
            elif hasattr(group_or_blocks, '__iter__'):
                blocks = list(group_or_blocks)
        except Exception as e:
            self._logger.warning(f"No se pudieron obtener bloques del grupo: {e}")
            return

        for block in blocks:
            self._process_block(block, plc_name)

        # Recursión en subgrupos
        groups: list[Any] = []
        try:
            if hasattr(group_or_blocks, 'get_groups'):
                groups = group_or_blocks.get_groups()
            elif hasattr(group_or_blocks, 'Groups'):
                groups = group_or_blocks.Groups
        except Exception as e:
            self._logger.warning(f"No se pudieron obtener subgrupos: {e}")

        for sub_group in groups:
            self._scan_group_recursive(sub_group, plc_name)

    def _process_block(self, block: Any, plc_name: str) -> None:
        """Procesa un bloque individual, extrayendo nombre y ruta."""
        block_name: str | None = None
        block_path: str = ""

        try:
            if hasattr(block, 'get_name'):
                block_name = block.get_name()
            elif hasattr(block, 'Name'):
                block_name = block.Name
        except Exception as e:
            self._logger.warning(f"Bloque inaccesible (nombre): {e}")
            return

        if not block_name:
            return

        try:
            if hasattr(block, 'get_path'):
                block_path = block.get_path()
            elif hasattr(block, 'Path'):
                block_path = block.Path
        except Exception:
            # Algunos bloques no tienen Path accesible
            block_path = ""

        # Extraer tipo y número del bloque (ej: "DB3110_PREP_COC2_1_PRINCIPAL" -> tipo="DB", num=3110)
        block_num: int = 0
        block_tipo: str = block.__class__.__name__  # Fallback: usar nombre de clase COM
        match = re.match(r'^(FC|FB|DB|OB)(\d+)', block_name, re.IGNORECASE)
        if match:
            block_tipo = match.group(1).upper()
            block_num = int(match.group(2))

        # Normalización ABSOLUTA: elimina espacios duros, normales, saltos y fuerza minúsculas
        normalized_key = block_name.replace('\xa0', '').replace(' ', '').strip().lower()

        # Crear el DTO y cachearlo (Extraemos la info mientras el COM está vivo)
        bloque_dto = BloquePLC(
            nombre=block_name,
            numero=block_num,
            tipo=block_tipo,
            ruta=block_path
        )
        self._blocks_cache[normalized_key] = bloque_dto
        self._logger.debug(f"Bloque cacheado: {normalized_key}")

    def find_block_case_insensitive(self, block_name: str) -> BloquePLC | None:
        """
        Busca un bloque DTO en el caché sin distinción de mayúsculas/minúsculas.

        Args:
            block_name: Nombre del bloque a buscar.

        Returns:
            Objeto BloquePLC si existe, None si no está en caché.
        """
        # Normalización ABSOLUTA: elimina espacios duros, normales, saltos y fuerza minúsculas
        normalized_search = block_name.replace('\xa0', '').replace(' ', '').strip().lower()
        
        return self._blocks_cache.get(normalized_search)

    def get_cached_blocks(self) -> dict[str, BloquePLC]:
        """Retorna copia del caché actual."""
        return dict(self._blocks_cache)

    def get_plc_name(self) -> str | None:
        """Retorna el nombre del PLC del último build_cache()."""
        return self._current_plc_name