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

    def ejecutar(
        self,
        plc_name: str,
        hw_type: str,
        dispositivos: Sequence[DispositivoHardware],
        dimensiones: DimensionesDispositivos,
        export_dir: str,
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
        """
        # ------------------------------------------------------------------ #
        #  TRANSACCION GLOBAL: si la Fase 4 falla, la Fase 1 (cambio de
        #  N_MAX) debe revertirse. El Gateway expone un context manager
        #  reentrante; si ya hay una transacción abierta (p.ej. importer),
        #  cede el control sin anidar (TIA no lo soporta).
        # ------------------------------------------------------------------ #
        with self._repo.transaccion(
            f"Sincronizar {len(dispositivos)} disp. {hw_type.upper()} en PLC '{plc_name}'"
        ):
            self._ejecutar_fases(
                plc_name, hw_type, dispositivos, dimensiones, export_dir
            )

    def _ejecutar_fases(
        self,
        plc_name: str,
        hw_type: str,
        dispositivos: Sequence[DispositivoHardware],
        dimensiones: DimensionesDispositivos,
        export_dir: str,
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
        # ------------------------------------------------------------------ #
        self._logger.info(
            f"Actualizando N_MAX ({hw_config.config_constant}) = "
            f"{dimensiones.num_disp_ed} en tabla {hw_config.config_table}."
        )
        self._repo.update_user_constant_value(
            plc_name=plc_name,
            table_name=hw_config.config_table,
            constant_name=hw_config.config_constant,
            new_value=dimensiones.num_disp_ed,
        )

        # ------------------------------------------------------------------ #
        #  2. COM SYNC: leer PLC y comparar con Excel (delete/rename)
        # ------------------------------------------------------------------ #
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
                self._logger.error(
                    "Fallo la exportacion de la tabla; abortando fase XML."
                )
                return

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
            modifier.save()

            # Folder para ED: externalizable en config.json.
            self._repo.importar_tabla_variables(
                plc_name=plc_name,
                xml_path=xml_path,
                folder_path=tia_folder_dispositivos,
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
        self._logger.info(
            "[DB] Actualizando comentarios del DataBlock (Redimensionando)..."
        )

        # Es CRÍTICO hacer una compilación global para que el cambio de
        # N_MAX (Fase 1) redimensione el array del DB en la memoria de
        # TIA Portal ANTES de exportarlo. Si no, el XML retiene elementos
        # fuera de rango que hacen crashear la importación posterior.
        if not self._repo.compilar_software(plc_name):
            self._logger.error(
                "La compilacion global fallo; abortando Fase 4 "
                "para evitar inconsistencias."
            )
            return

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
            self._logger.error(
                f"Fallo exportando DB '{hw_config.db_name}' "
                "o el archivo no existe; abortando Fase 4."
            )
            return

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

        self._repo.importar_bloque(
            plc_name=plc_name,
            xml_path=db_xml_path,
            folder_path=tia_folder_dispositivos,
        )
        self._repo.compilar_bloque(plc_name, hw_config.db_name)
        self._logger.info(
            "Sincronizacion de dispositivos finalizada. "
            "Refrescando caché COM..."
        )
        # Tras importar tablas y DBs, los objetos COM cacheados en
        # TIAScanner quedan invalidados (Disposed) por TIA Portal.
        # Forzamos un re-escaneo para renovar los punteros y permitir
        # ejecuciones consecutivas del flujo.
        self._repo.force_rescan(plc_name)
