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
        """Inicializa el escáner con un logger y cachés vacíos."""
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )
        self._blocks_cache: dict[str, BloquePLC] = {}
        # Caché de TagTables PLC: clave = table.Name.lower(), valor = objeto COM.
        # Se puebla en build_cache() para que el repo pueda buscar tablas
        # sin tener que conocer su folder_path en TIA Portal.
        self._table_cache: dict[str, object] = {}
        self._current_plc_name: str | None = None

    def clear_cache(self) -> None:
        """Vacía los cachés, preparando para un nuevo escaneo."""
        self._logger.debug("Limpiando cachés (bloques + tablas)...")
        self._blocks_cache.clear()
        self._table_cache.clear()
        self._current_plc_name = None

    def build_cache(self, plc_name: str, plc_object: Any, force: bool = False) -> dict[str, BloquePLC]:
        """
        Construye el caché de bloques Y de tablas de variables
        mediante escaneo profundo del PLC.
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
            self._scan_tag_tables(plc_object)

            self._logger.info(
                f"Caché construido: {len(self._blocks_cache)} bloques, "
                f"{len(self._table_cache)} tablas encontradas."
            )
            return self._blocks_cache

        except Exception as e:
            self._logger.error(f"Error durante el escaneo del PLC '{plc_name}': {e}")
            raise TIAScannerError(f"Fallo en build_cache para {plc_name}") from e

    def _scan_tag_tables(self, plc_object: Any) -> None:
        """
        Puebla self._table_cache con TODAS las PlcTagTables del PLC.

        Estrategia: llamamos plc.get_plc_tag_tables() SIN folder_path
        para que el wrapper recorra todo el árbol recursivamente.
        Manual oficial (seccion 2.2.8) confirma esta firma.
        """
        try:
            tables = plc_object.get_plc_tag_tables()
        except Exception as e:
            self._logger.warning(
                f"No se pudieron listar PlcTagTables del PLC: {e}. "
                "La caché de tablas queda vacia."
            )
            return

        for table in tables:
            try:
                name = (
                    table.get_name()
                    if hasattr(table, "get_name")
                    else getattr(table, "Name", None)
                )
            except Exception as e:
                self._logger.warning(f"TagTable inaccesible (nombre): {e}")
                continue

            if not name:
                continue

            normalized = str(name).replace("\xa0", "").replace(" ", "").strip().lower()
            self._table_cache[normalized] = table
            self._logger.debug(f"Tabla de tags cacheada: {normalized}")

    def _scan_group_recursive(self, group_or_blocks: Any, plc_name: str) -> None:
        """
        Escanea recursivamente grupos y bloques del PLC.
        """
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
        except Exception as e:
            # El escáner silenció COM exceptions menores al leer la ruta
            # de un bloque. TIA Portal las detecta y envenena la
            # transacción si esto ocurre DENTRO del with transaccion().
            # Como ahora el escaneo se hace fuera, solo logueamos.
            self._logger.debug(
                f"El escaner no pudo obtener la ruta del bloque "
                f"'{block_name}' (estado temporal): {e}"
            )
            block_path = ""

        block_num: int = 0
        block_tipo: str = block.__class__.__name__
        match = re.match(r'^(FC|FB|DB|OB)(\d+)', block_name, re.IGNORECASE)
        if match:
            block_tipo = match.group(1).upper()
            block_num = int(match.group(2))

        normalized_key = block_name.replace('\xa0', '').replace(' ', '').strip().lower()

        bloque_dto = BloquePLC(
            nombre=block_name,
            numero=block_num,
            tipo=block_tipo,
            ruta=block_path
        )
        self._blocks_cache[normalized_key] = bloque_dto
        self._logger.debug(f"Bloque cacheado: {normalized_key}")

    def find_block_case_insensitive(self, block_name: str) -> BloquePLC | None:
        """Busca un bloque DTO en el caché sin distinción de mayúsculas/minúsculas."""
        normalized_search = block_name.replace('\xa0', '').replace(' ', '').strip().lower()

        return self._blocks_cache.get(normalized_search)

    def get_cached_blocks(self) -> dict[str, BloquePLC]:
        """Retorna copia del caché actual."""
        return dict(self._blocks_cache)

    def get_plc_name(self) -> str | None:
        """Retorna el nombre del PLC del último build_cache()."""
        return self._current_plc_name

    def find_tag_table_case_insensitive(self, table_name: str) -> object | None:
        """Busca una PlcTagTable en la caché sin distinguir mayus/minus."""
        if not table_name:
            return None
        normalized = (
            table_name.replace("\xa0", "").replace(" ", "").strip().lower()
        )
        return self._table_cache.get(normalized)
