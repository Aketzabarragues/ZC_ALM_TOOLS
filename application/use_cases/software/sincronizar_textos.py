"""
Application Layer - Use Case: Sincronizar Textos
================================================
Inyecta comentarios en bloques de datos (DBs) a partir de listas de objetos.
Arquitectura: Exportar -> Mutear -> Importar por bloque con staging.
Gestión de ciclo de vida del caché para evitar punteros COM zombies.
Motor de compilación inteligente: Pre-check y Post-check de consistencia.
"""

import logging
import os
import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable

from infrastructure import config_manager
from infrastructure.tia.scanner import TIAScanner
from infrastructure.xml.modifier import XMLModifier
from core.ports import ISoftwareRepository


@dataclass
class TareaSincronizacion:
    """
    DTO que describe una tarea de sincronizacion de comentarios en un DB.

    Reemplaza el uso de diccionarios literales (que eran inferibles
    pero no chequeables estaticamente). Cada tarea tiene todos los
    parametros que necesita para ejecutarse autonomamente.
    """
    db_name: str
    array_name: str
    items: list = field(default_factory=list)
    get_id_func: Callable[[Any], int] = lambda x: getattr(x, 'numero', 0)
    get_comment_func: Callable[[Any], str] = lambda x: getattr(
        x, 'comentario_db', getattr(x, 'texto', '')
    )
    es_parametro: bool = False


class SincronizarTextosUseCase:
    """Caso de uso para sincronizar comentarios de DBs con datos del Excel."""

    def __init__(self, tia: ISoftwareRepository, scanner: 'TIAScanner') -> None:
        self._tia = tia
        self._scanner = scanner  # DI: misma instancia que el Composition Root
        self._logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")

        # Limpieza proactiva del entorno temporal
        self._temp_dir = Path(config_manager.get_build_root()) / "temp"
        if self._temp_dir.exists():
            shutil.rmtree(self._temp_dir, ignore_errors=True)
        self._temp_dir.mkdir(parents=True, exist_ok=True)

    def sincronizar_comentarios_db(
        self,
        plc_name: str,
        db_name: str,
        array_name: str,
        items: Iterable[Any],
        get_id_func: Callable[[Any], int],
        get_comment_func: Callable[[Any], str],
        es_parametro: bool = False
    ) -> bool:
        """
        Sincroniza comentarios de variables en un DB de TIA Portal.
        Flujo: Exportar -> Mutear -> Importar por bloque con staging.
        """
        export_path = str((self._temp_dir / f"{db_name}.xml").absolute())

        try:
            # 1. Exportar el bloque al directorio de forma plana
            if not self._tia.exportar_bloque(plc_name, db_name, str(self._temp_dir)):
                self._logger.error(f"Fallo al exportar {db_name}")
                return False

            if not Path(export_path).exists():
                self._logger.error(f"El archivo {export_path} no se generó.")
                return False

            # 2. Modificar XML usando minidom (UNA SOLA VEZ)
            mod = XMLModifier(export_path)

            cambios = False
            for item in items:
                idx = get_id_func(item)
                comentario = get_comment_func(item)
                if mod.set_comment(array_name, idx, comentario, es_parametro):
                    cambios = True

            if cambios:
                mod.save()
                
                # 3. Sanitizar ruta original y obtener path relativo
                ruta_original = self._tia.obtener_ruta_bloque(plc_name, db_name)
                target_relativo = self._tia.sanitize_import_path(ruta_original or "")
                
                self._logger.debug(f"Ruta original: {ruta_original} -> Relativa: {target_relativo}")
                
                # 4. Importar usando staging
                exito = self._tia.importar_bloque_single(
                    plc_name, export_path, target_relativo
                )
            else:
                self._logger.info(f"No hubo cambios de texto para {db_name}. Se omite importación.")
                exito = True

            return exito

        finally:
            # 5. Limpieza OBLIGATORIA para no re-importar en el siguiente ciclo
            try:
                if Path(export_path).exists():
                    os.remove(export_path)
                self._logger.debug(f"Limpiado archivo temporal: {export_path}")
            except Exception as e:
                self._logger.warning(f"No se pudo limpiar archivo temporal {export_path}: {e}")

            # 6. El caché NO se limpia aquí para permitir que sobreviva durante 
            #    el procesamiento de múltiples bloques en un lote.

    def sincronizar_multiple_db(
        self,
        plc_name: str,
        tareas: list[TareaSincronizacion]
    ) -> dict[str, bool]:
        """
        Sincroniza múltiples DBs en una sola transacción.
        
        Motor de Compilación Inteligente:
        - Pre-Check: Verifica consistencia antes del bucle → compilar si necesario
        - Ejecución: Exportar -> Mutar -> Importar
        - Post-Check: Compilar si hubo cambios
        
        Args:
            plc_name: Nombre del PLC.
            tareas: Lista de dicts con:
                - db_name: Nombre del DB
                - array_name: Nombre del array/member
                - items: Lista de objetos
                - get_id_func: Función para obtener índice
                - get_comment_func: Función para obtener comentario
                - es_parametro: Bool para parámetros
                
        Returns:
            Dict con resultados por DB.
        """
        # ===== PRE-CHECK: Verificar consistencia de bloques =====
        self._logger.info("🔍 Pre-Check: Verificando consistencia de bloques...")
        necesita_compilacion_pre = False
        
        for tarea in tareas:
            db_name = tarea.db_name
            if not self._tia.is_bloque_consistente(plc_name, db_name):
                self._logger.warning(f"Bloque '{db_name}' no está consistente. Se requerirá compilación.")
                necesita_compilacion_pre = True
                break  # Con uno inconsistente es suficiente
        
        if necesita_compilacion_pre:
            self._logger.info("⏳ Compilando antes de sincronizar (Pre-Check)...")
            if not self._tia.compilar_software(plc_name):
                self._logger.error("❌ Fallo en compilación Pre-Check. Abortando sincronización.")
                return {t.db_name: False for t in tareas}
        
        # ===== EJECUCIÓN DEL LOTE =====
        self._logger.info(f"🚀 Iniciando lote de sincronización ({len(tareas)} DBs)...")
        resultados: dict[str, bool] = {}
        bloques_importados = 0
        
        for tarea in tareas:
            resultado = self.sincronizar_comentarios_db(
                plc_name=plc_name,
                db_name=tarea.db_name,
                array_name=tarea.array_name,
                items=tarea.items,
                get_id_func=tarea.get_id_func,
                get_comment_func=tarea.get_comment_func,
                es_parametro=tarea.es_parametro,
            )
            resultados[tarea.db_name] = resultado
            if resultado:
                bloques_importados += 1
        
        # ===== POST-CHECK: Compilar si hubo cambios =====
        if bloques_importados > 0:
            self._logger.info(f"📦 {bloques_importados} bloques sincronizados. Ejecutando Post-Check...")
            if not self._tia.compilar_software(plc_name):
                self._logger.error("⚠️ Fallo en compilación Post-Check. Los cambios fueron inyectados.")
        else:
            self._logger.info("ℹ️ No se sincronizó ningún bloque. Omitiendo compilación Post-Check.")
        
        # Limpiar caché UNA SOLA VEZ al final del lote
        self._tia.clear_cache()
        self._logger.debug("Caché invalidado al final del lote de sincronización.")
        
        return resultados