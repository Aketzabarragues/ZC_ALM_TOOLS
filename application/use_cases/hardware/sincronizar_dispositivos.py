"""
Application Layer - Use Case: Sincronizar Dispositivos
======================================================
Orquesta la sincronizacion de las Constantes de Usuario de los dispositivos
hardware (ED, SD, ANA, etc.) entre el Excel Maestro y TIA Portal.

Estrategia HIBRIDA (orden estricto):
  1. N_MAX via COM: actualiza la constante de dimensionamiento del PLC
     preservando los cross-references de los bloques.
  2. COM Sync (Update/Delete): borra y renombra constantes in vivo.
  3. XML Sync (Add): para los nuevos, exporta la tabla -> modifica el XML
     -> reimporta. Es mas rapido para inserciones masivas.

NUNCA se ejecuta una transaccion global. Cada llamada al repositorio es
autonoma. Es responsabilidad de un orquestador superior (futuro) agrupar
varias llamadas en una transaccion si fuera necesario.
"""

import logging
from collections.abc import Sequence
from pathlib import Path
from typing import Any, Callable

from core.models import DimensionesDispositivos, DispositivoHardware
from core.ports import ISoftwareRepository
from infrastructure import config_manager
from infrastructure.xml.modifier import XMLModifier
from infrastructure.xml.tag_modifier import TagTableModifier

__all__ = ["SincronizarDispositivosUseCase"]


class SincronizarDispositivosUseCase:
    """
    Caso de uso: sincroniza las UserConstants de la tabla de tags
    de dispositivos (configurable via config_manager) con los datos
    del Excel Maestro.

    El repositorio se inyecta via Protocol (ISoftwareRepository) para
    que la logica sea testeable con un mock y no dependa de TIA Portal.
    """

    def __init__(self, repo: ISoftwareRepository) -> None:
        self._repo = repo
        self._logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )

    def generar_prevision(
        self,
        plc_name: str,
        hw_type: str,
        dispositivos: Sequence[DispositivoHardware],
        export_dir: str,
    ) -> list[dict[str, Any]]:
        """
        DRY-RUN: exporta la tabla actual y genera un diff comparando
        los tags existentes en el PLC con los del Excel.

        Pensado para que la TUI muestre una tabla Rich antes de que
        el usuario confirme la sincronización. NO modifica el PLC.

        Args:
            plc_name: nombre del PLC objetivo.
            hw_type: tipo de hardware ("ed", "ea", ...).
            dispositivos: lista del Excel (cualquier tipo que cumpla el
                Protocol DispositivoHardware).
            export_dir: directorio temporal donde exportar el XML de
                la tabla actual del PLC.

        Returns:
            Lista de dicts con keys:
                - "index": int (1-based, posicion en la lista original)
                - "tag_excel": str (el plc_tag del dispositivo)
                - "estado": str ("Nueva variable" | "Sin cambios")
        """
        # Import local para evitar posibles ciclos de imports si
        # tag_modifier creciera en el futuro.
        from infrastructure.xml.tag_modifier import (  # noqa: PLC0415
            TagTableModifier,
        )

        hw_config = config_manager.get_hardware_tia_config(hw_type)

        # 1. Exportar tabla actual (silenciosamente)
        xml_path = self._repo.exportar_tabla_variables(
            plc_name=plc_name,
            table_name=hw_config.tag_table,
            export_dir=export_dir,
        )
        if not xml_path or not Path(xml_path).exists():
            self._logger.warning(
                f"No se pudo exportar la tabla '{hw_config.tag_table}' "
                "para la prevision. Devolviendo lista vacia."
            )
            return []

        # 2. Parsear el XML para extraer mapeo {Index: TagName}
        tags_actuales: dict[int, str] = {}
        try:
            self._logger.info(f"Analizando XML de previsión: {Path(xml_path).name}")
            modifier = TagTableModifier(xml_path)

            nodos_a_buscar = [".//SW.Tags.PlcTag", ".//SW.Tags.PlcUserConstant"]
            for node_type in nodos_a_buscar:
                for elem in modifier.root.findall(node_type):
                    name_str, val_int = None, None
                    for child in elem.iter():
                        local_child = child.tag.split("}")[-1] if "}" in child.tag else child.tag
                        if local_child == "Name" and child.text:
                            name_str = child.text.strip()
                        elif local_child == "Value" and child.text:
                            try:
                                val_int = int(child.text.strip())
                            except ValueError:
                                pass

                    if name_str and val_int is not None:
                        tags_actuales[val_int] = name_str

            self._logger.info(f"Previsión: {len(tags_actuales)} variables encontradas en el PLC.")
        except Exception as e:
            self._logger.warning(f"No se pudo parsear el XML de previsión (asumiendo tabla vacía): {e}")

        # 3. Generar el Diff
        diff: list[dict[str, Any]] = []
        for disp in dispositivos:
            if not disp.plc_tag:
                continue

            name_excel = disp.plc_tag.strip()
            idx = disp.numero

            name_plc = tags_actuales.get(idx, "[No existe]")

            if name_plc == "[No existe]":
                estado = "➕ Nueva variable"
            elif name_plc == name_excel:
                estado = "➖ Sin cambios"
            else:
                estado = "🔄 Renombrar"

            diff.append({
                "index": idx,
                "tag_plc": name_plc,
                "tag_excel": name_excel,
                "estado": estado,
            })

            # Quitar de tags_actuales para detectar los que sobran (Eliminar)
            if idx in tags_actuales:
                del tags_actuales[idx]

        # Los que queden en tags_actuales son variables que están en el PLC pero no en el Excel (o están fuera de rango)
        for idx, name_plc in sorted(tags_actuales.items()):
            diff.append({
                "index": idx,
                "tag_plc": name_plc,
                "tag_excel": "[No en Excel / Sobrante]",
                "estado": "🗑️ Eliminar",
            })

        # Ordenar por índice para que la tabla quede limpia
        diff.sort(key=lambda x: x["index"])
        return diff

    def ejecutar(
        self,
        plc_name: str,
        hw_type: str,
        dispositivos: Sequence[DispositivoHardware],
        dimensiones: DimensionesDispositivos,
        export_dir: str,
        progress_callback: Callable[[str], None] | None = None,
    ) -> None:
        """
        Sincroniza las UserConstants del PLC con el Excel Maestro.

        Args:
            plc_name: nombre del PLC objetivo.
            hw_type: tipo de hardware ("ed", "ea", "sd", etc.). Determina
                el DTO de TIA Portal a usar (config.json).
            dispositivos: lista de cualquier tipo que cumpla el Protocol
                DispositivoHardware (DispED, DispEA, DispSD, ...).
            dimensiones: contadores N_MAX leidos de las celdas nombradas.
            export_dir: directorio temporal donde exportar el XML de la
                tabla para anadir las nuevas constantes.
            progress_callback: callable opcional que recibe un str con el
                texto de la fase actual. La TUI lo usa para actualizar un
                spinner de Rich dinamicamente.
        """
        # ------------------------------------------------------------------ #
        #  TRANSACCION GLOBAL: si la Fase 4 falla, la Fase 1 (cambio de
        #  N_MAX) debe revertirse. El Gateway expone un context manager
        #  reentrante; si ya hay una transacción abierta (p.ej. importer),
        #  cede el control sin anidar (TIA no lo soporta).
        # ------------------------------------------------------------------ #
        # 1. Ejecutar todo el trabajo crítico dentro de la transacción.
        # Si la Fase 4 falla, la Fase 1 (cambio de N_MAX) debe revertirse.
        # El Gateway expone un context manager reentrante; si ya hay una
        # transacción abierta (p.ej. importer), cede el control sin
        # anidar (TIA no lo soporta).
        with self._repo.transaccion(
            f"Sincronizar {len(dispositivos)} disp. {hw_type.upper()} en PLC '{plc_name}'"
        ):
            self._ejecutar_fases(
                plc_name, hw_type, dispositivos, dimensiones, export_dir, progress_callback
            )

        # 2. Refrescar el caché SOLO si la transacción terminó y se hizo
        # COMMIT con éxito. Si el escaner sufre excepciones COM menores
        # al re-leer el PLC, ya NO corrompe la transacción (porque está
        # cerrada). Antes este escaneo estaba dentro del `with` y TIA
        # Portal detectaba el fallo y rechazaba el COMMIT con
        # "Commit of a Transaction is not allowed after an exception
        # is thrown due to potential project data corruption."
        self._logger.info(
            "Sincronizacion de dispositivos finalizada. "
            "Refrescando caché COM (post-COMMIT)..."
        )
        self._repo.force_rescan(plc_name)

    def _ejecutar_fases(
        self,
        plc_name: str,
        hw_type: str,
        dispositivos: Sequence[DispositivoHardware],
        dimensiones: DimensionesDispositivos,
        export_dir: str,
        progress_callback: Callable[[str], None] | None = None,
    ) -> None:
        """
        Cuerpo de las 4 fases, envuelto en transaccion global por ejecutar().
        """
        # Obtenemos la configuracion de TIA Portal para este tipo de hardware.
        # Vive en config_manager (externalizable en config.json) en vez de
        # en el modelo de dominio, para poder escalar a EA, SD, ANA, etc.
        hw_config = config_manager.get_hardware_tia_config(hw_type)
        tia_folder_dispositivos = config_manager.get_tia_folder_dispositivos_ed()

        # ------------------------------------------------------------------ #
        #  1. UPDATE del N_MAX en la tabla de configuracion
        #  El N_MAX es DINAMICO: depende del hw_type ("ed" -> num_disp_ed,
        #  "ea" -> num_disp_ea, etc.). Si la celda del Excel no existe
        #  o el hw_type es nuevo, n_max_val = 0 (no rompe, pero las
        #  posteriores fases daran errores por array de tamano 0).
        # ------------------------------------------------------------------ #
        if progress_callback:
            progress_callback("1/4 Actualizando constantes de dimensionamiento (COM)...")
        n_max_val: int = getattr(dimensiones, f"num_disp_{hw_type}", 0)
        self._logger.info(
            f"Actualizando N_MAX ({hw_config.config_constant}) = "
            f"{n_max_val} en tabla {hw_config.config_table}."
        )
        self._repo.update_user_constant_value(
            plc_name=plc_name,
            table_name=hw_config.config_table,
            constant_name=hw_config.config_constant,
            new_value=n_max_val,
        )

        # ------------------------------------------------------------------ #
        #  2. COM SYNC: leer PLC y comparar con Excel (delete/rename)
        # ------------------------------------------------------------------ #
        if progress_callback:
            progress_callback("2/4 Sincronizando variables existentes (COM)...")
        plc_consts: dict[int, str] = self._repo.get_user_constants(
            plc_name=plc_name,
            table_name=hw_config.tag_table,
        )
        excel_dict: dict[int, str] = {
            d.numero: d.plc_tag for d in dispositivos if d.plc_tag
        }

        self._logger.info(
            f"Comparando {len(plc_consts)} constantes PLC vs "
            f"{len(excel_dict)} del Excel."
        )

        for value, pl_name_in_plc in plc_consts.items():
            if value not in excel_dict:
                # El PLC tiene una constante que el Excel no: borrar.
                self._logger.info(
                    f"[DEL] Constante value={value} (name='{pl_name_in_plc}') "
                    "no esta en el Excel. Borrando."
                )
                self._repo.delete_user_constant(
                    plc_name=plc_name,
                    table_name=hw_config.tag_table,
                    name=pl_name_in_plc,
                )
            else:
                expected_name = excel_dict[value]
                if pl_name_in_plc != expected_name:
                    # El PLC tiene un nombre distinto al Excel: renombrar.
                    self._logger.info(
                        f"[REN] Constante value={value}: "
                        f"'{pl_name_in_plc}' -> '{expected_name}'."
                    )
                    self._repo.update_user_constant_name(
                        plc_name=plc_name,
                        table_name=hw_config.tag_table,
                        current_name=pl_name_in_plc,
                        new_name=expected_name,
                    )

        # ------------------------------------------------------------------ #
        #  3. XML SYNC: anadir los nuevos via export/modify/import
        # ------------------------------------------------------------------ #
        if progress_callback:
            progress_callback("3/4 Inyectando nuevas variables (XML)...")
        nuevos = [d for d in dispositivos if d.numero not in plc_consts]
        if nuevos:
            self._logger.info(
                f"Hay {len(nuevos)} constantes nuevas. "
                f"Procediendo via export XML + reimport."
            )

            xml_path = self._repo.exportar_tabla_variables(
                plc_name=plc_name,
                table_name=hw_config.tag_table,
                export_dir=export_dir,
            )
            if not xml_path or not Path(xml_path).exists():
                # RAISE explicito: el caller (transaccion) vera la excepcion
                # y disparara el ROLLBACK. Antes hacia return silencioso,
                # lo que engañaba al context manager y dejaba la transaccion
                # COM en estado corrupto.
                raise RuntimeError(
                    f"Fallo critico: la exportacion de la tabla '{hw_config.tag_table}' "
                    f"no genero un XML valido (xml_path={xml_path!r}). "
                    "Abortando transaccion."
                )

            modifier = TagTableModifier(xml_path)
            for d in nuevos:
                self._logger.info(
                    f"[ADD] value={d.numero} name='{d.plc_tag}' "
                    f"comment='{d.plc_comentario}'."
                )
                modifier.add_user_constant(
                    name=d.plc_tag,
                    value=d.numero,
                    comment=d.plc_comentario,
                )
            self._logger.debug(
                f"Guardando XML modificado en: {xml_path}"
            )
            modifier.save()

            # Folder para ED: externalizable en config.json.
            # El repo no retorna bool aqui (firma void), pero defensivamente
            # comprobamos que el XML modificado aun existe. Un fallo real
            # de importacion en TIA seria capturado por el subproceso COM
            # y se manifestaria en la siguiente operacion. Para tener una
            # excepcion explicita, podriamos pedirle al repo que retorne bool;
            # por ahora, validamos el estado del archivo.
            if not Path(xml_path).exists():
                raise RuntimeError(
                    f"Fallo critico: el XML modificado '{xml_path}' "
                    "desaparecio antes de la importacion. Abortando transaccion."
                )
            if not self._repo.importar_tabla_variables(
                plc_name=plc_name,
                xml_path=xml_path,
                folder_path=tia_folder_dispositivos,
            ):
                raise RuntimeError(
                    f"Fallo critico: TIA Portal rechazo la importacion de la "
                    f"tabla '{hw_config.tag_table}' desde '{xml_path}'. "
                    "Abortando transaccion para forzar ROLLBACK completo "
                    "(N_MAX + DB)."
                )
        else:
            # Sin nuevos: la Fase 3 no se ejecuta, pero la Fase 4 SIEMPRE corre.
            self._logger.info(
                "No hay constantes nuevas que anadir (Fase 3 omitida). "
                "Continuando con la Fase 4 (DB)..."
            )

        # ------------------------------------------------------------------ #
        #  FASE 4: Comentarios del DataBlock (ED)
        #  Compilar -> Exportar -> Editar XML -> Importar -> Compilar.
        #  Replica la secuencia del motor C# original.
        # ------------------------------------------------------------------ #
        if progress_callback:
            progress_callback("4/4 Actualizando comentarios del DB (XML)...")
        self._logger.info(
            "[DB] Actualizando comentarios del DataBlock (Redimensionando)..."
        )

        # Es CRÍTICO hacer una compilación global para que el cambio de
        # N_MAX (Fase 1) redimensione el array del DB en la memoria de
        # TIA Portal ANTES de exportarlo. Si no, el XML retiene elementos
        # fuera de rango que hacen crashear la importación posterior.
        if not self._repo.compilar_software(plc_name):
            raise RuntimeError(
                "La compilacion global fallo tras el cambio de N_MAX. "
                "Abortando transaccion para evitar inconsistencias "
                "(el DB no se redimensionaria)."
            )

        # Refrescar caché tras la compilación para renovar punteros COM.
        self._repo.force_rescan(plc_name)

        # Exportar. exportar_bloque retorna bool (legacy); construimos
        # la ruta manualmente para evitar incompatibilidades de tipo.
        export_ok = self._repo.exportar_bloque(
            plc_name=plc_name,
            block_name=hw_config.db_name,
            target_dir=export_dir,
        )
        db_xml_path = str(Path(export_dir) / f"{hw_config.db_name}.xml")

        if not export_ok or not Path(db_xml_path).exists():
            raise RuntimeError(
                f"Fallo exportando DB '{hw_config.db_name}' "
                f"(export_ok={export_ok}, db_xml_path={db_xml_path!r}). "
                "Abortando transaccion."
            )

        # Mapear: el Excel es base-1, TIA espera Path base-0.
        # Restamos 1 al numero del Excel al construir el indice del Subelement.
        modifier = XMLModifier(db_xml_path)
        for d in dispositivos:
            texto = f"{d.plc_tag} - {d.descripcion}"
            index_base_0 = d.numero - 1
            modifier.set_comentario_array(
                hw_config.db_array_name, index_base_0, texto
            )
        modifier.save()

        # Validar existencia del XML antes de importar (defensa por si
        # XMLModifier fallara silenciosamente al guardar).
        if not Path(db_xml_path).exists():
            raise RuntimeError(
                f"Fallo critico: el XML modificado del DB '{db_xml_path}' "
                "desaparecio antes de la importacion. Abortando transaccion."
            )

        # importar_bloque ahora retorna bool: si TIA Portal falla la
        # importacion, el context manager ve la excepcion INMEDIATAMENTE
        # y hace ROLLBACK (en vez de continuar con transaccion corrupta
        # y fallar despues en compilar_bloque con datos a medio escribir).
        if not self._repo.importar_bloque(
            plc_name=plc_name,
            xml_path=db_xml_path,
            folder_path=tia_folder_dispositivos,
        ):
            raise RuntimeError(
                f"Fallo critico: TIA Portal rechazo la importacion del bloque "
                f"DB '{hw_config.db_name}' desde '{db_xml_path}'. Abortando "
                "transaccion para forzar ROLLBACK completo (N_MAX + DB)."
            )

        if not self._repo.compilar_bloque(plc_name, hw_config.db_name):
            raise RuntimeError(
                f"La compilacion del bloque '{hw_config.db_name}' fallo tras "
                "la importacion del XML. Es probable que la importacion haya "
                "sido rechazada por TIA Portal. Abortando transaccion para "
                "forzar ROLLBACK completo (N_MAX + DB)."
            )
        # NOTA: el refresco del cache (force_rescan) NO se hace aqui
        # porque el escaner sufre excepciones COM menores que silencia
        # y que TIA Portal detecta, corrompiendo la transaccion. El
        # escaneo se hace FUERA del `with transaccion(...)` en ejecutar().
