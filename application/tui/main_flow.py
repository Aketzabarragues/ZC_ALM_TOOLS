"""
Application Layer - TUI: Main Flow
====================================
Orquestador principal de la aplicacion.

Maquina de Estados con Main Loop interactivo para orquestar TIA Portal.
Los flujos de software viven en application.tui.software_flows.
"""

import logging
from pathlib import Path
from typing import Any, cast

import questionary
from questionary import Choice, Separator
from rich.console import Console

from application.session import AppSession
from application.tui.hardware_flows import (
    _flujo_sincronizar_dispositivos,
    _flujo_sincronizar_dispositivos_ea,
)
from application.tui.software_flows import (
    _flujo_generar_procesos,
    _flujo_sincronizar_textos,
)
from application.tui.utils import _clear_screen, _pertenece_al_proceso
from core.models import (
    Alarma,
    DimensionesDispositivos,
    DispEA,
    DispED,
    PInt,
    PReal,
    Proceso,
)
from infrastructure import config_manager
from infrastructure.excel_parser import ExcelParser
from infrastructure.tia.gateway import (
    TIAPortalGateway,
    ConnectionFailedError,
    NoProjectOpenError,
    PortalNotRunningError,
    TIAServiceError,
    load_siemens_tia,
)
from infrastructure.tia.importer import TIAImporter
from infrastructure.tia.scanner import TIAScanner
from infrastructure.tia.software_repository import SoftwareRepository
from infrastructure.ui_dialogs import (
    seleccionar_carpeta,
    seleccionar_excel,
    seleccionar_proyecto_tia,
)

__all__ = ["run", "_clear_screen", "_pertenece_al_proceso"]

console = Console()


def _seleccionar_plc(session: AppSession) -> str | None:
    """Selecciona y retorna el nombre del PLC, ejecutando build_cache."""
    with session.gateway.silenciar_ruido():
        plcs = session.gateway.get_plc_names()
    if not plcs:
        console.print("[bold red]❌ No se detectaron PLCs en el proyecto.[/bold red]")
        return None

    plc_name = questionary.select(
        "Selecciona el PLC objetivo:",
        choices=plcs
    ).ask()

    if not plc_name:
        return None

    console.print(f"\n[cyan]⏳ Escaneando PLC '{plc_name}' y construyendo caché...[/cyan]")
    try:
        with session.gateway.silenciar_ruido():
            session.software_repo.build_cache(plc_name)
            bloques = session.software_repo.get_existing_blocks(plc_name)
        console.print(f"[green]✅ Caché construido: {len(bloques)} bloques encontrados.[/green]")
    except Exception as e:
        console.print(f"[bold red]❌ Error escaneando PLC: {e}[/bold red]")
        return None

    session.plc_seleccionado = plc_name
    return plc_name


def _flujo_principal_con_tia(
    parser: ExcelParser,
    ruta_excel: str,
    procesos: list[Proceso],
    preal_list: list[PReal],
    pint_list: list[PInt],
    alarmas_list: list[Alarma],
    session: AppSession,
    logger: logging.Logger,
) -> None:
    """
    Bucle principal tras haber establecido una conexion con TIA Portal (via attach o open_new).
    """
    project_name: str = session.gateway.get_project_name()
    with session.gateway.silenciar_ruido():
        plc_names: list[str] = session.gateway.get_plc_names()

    _clear_screen()
    _print_project_summary(project_name, plc_names)
    confirmed: bool = _request_confirmation()

    if not confirmed:
        logger.info("Usuario rechazó el proyecto. Abortando.")
        print("\nOperación cancelada por el usuario.")
        return

    logger.info(f"Proyecto confirmado: {project_name}")
    print(f"\nProyecto '{project_name}' confirmado. Continuando...")

    if not _seleccionar_plc(session):
        console.print("[bold yellow]⚠️ No se seleccionó PLC. Abortando.[/bold yellow]")
        return

    while True:
        _clear_screen()
        console.rule(
            f"MENÚ PRINCIPAL | Proyecto: [bold orange1]{project_name}[/bold orange1] | "
            f"PLC: [bold orange1]{session.plc_seleccionado}[/bold orange1]"
        )
        opcion_principal: str | None = questionary.select(
            "Selecciona una opción:",
            choices=[
                Separator(),
                Choice(" ⚡ Generar Procesos", value="generate"),
                Choice(" 🔄 Sincronizar Parámetros y Alarmas", value="sync_texts"),
                Choice(" 🎛️ Sincronizar Dispositivos", value="sincronizar_dispositivos"),
                Separator(),
                Choice(" 🔌 Cambiar PLC objetivo", value="change_plc"),
                Choice(" 📡 Forzar escaneo completo del PLC", value="rescan"),
                Choice(" 📊 Recargar datos del Excel Maestro", value="reload_excel"),
                Choice(" 📂 Configurar Ruta de Plantillas", value="config_templates"),
                Separator(),
                Choice(" ❌ Salir", value="exit")
            ]
        ).ask()

        if not opcion_principal or opcion_principal == "exit":
            logger.info("Saliendo de la aplicación...")
            _clear_screen()
            print("\n👋 Desconectando de TIA Portal...")
            break

        if opcion_principal == "change_plc":
            _seleccionar_plc(session)
            input("\nPulsa Enter para continuar...")

        elif opcion_principal == "rescan":
            if session.plc_seleccionado:
                console.print(
                    f"\n[cyan]⏳ Forzando re-escaneo completo de '{session.plc_seleccionado}'...[/cyan]"
                )
                try:
                    with session.gateway.silenciar_ruido():
                        session.software_repo.force_rescan(session.plc_seleccionado)
                        bloques = session.software_repo.get_existing_blocks(session.plc_seleccionado)
                    console.print(f"[bold green]✅ Caché reconstruido: {len(bloques)} bloques.[/bold green]")
                except Exception as e:
                    console.print(f"[bold red]❌ Error en re-escaneo: {e}[/bold red]")
            else:
                console.print("[bold red]❌ No hay PLC seleccionado.[/bold red]")
            input("\nPulsa Enter para continuar...")

        elif opcion_principal == "generate":
            _flujo_generar_procesos(procesos, preal_list, pint_list, alarmas_list, session)
            input("\nPulsa Enter para volver al Menú Principal...")

        elif opcion_principal == "sync_texts":
            _flujo_sincronizar_textos(procesos, preal_list, pint_list, alarmas_list, session)
            input("\nPulsa Enter para volver al Menú Principal...")

        elif opcion_principal == "sincronizar_dispositivos":
            _clear_screen()
            console.rule(
                f"SINCRONIZAR DISPOSITIVOS | PLC: [bold orange1]{session.plc_seleccionado}[/bold orange1]"
            )

            tipo_seleccionado = questionary.select(
                "Selecciona el tipo de dispositivo a sincronizar:",
                choices=[
                    Choice(" Entradas Digitales (ED)", value="ED"),
                    Choice(" Entradas Analógicas (EA)", value="EA"),
                    # Aqui se anadiran SD, M, MVF, etc. en el futuro
                ]
            ).ask()

            if not tipo_seleccionado:
                console.print("[dim]No se seleccionó ningún dispositivo. Cancelando...[/dim]")
                input("\nPulsa Enter para volver al Menú Principal...")
                continue

            # Por cada tipo seleccionado, ejecutamos su flujo correspondiente
            if tipo_seleccionado == "ED":
                _flujo_sincronizar_dispositivos(session)
            elif tipo_seleccionado == "EA":
                _flujo_sincronizar_dispositivos_ea(session)

            # Cuando haya mas tipos, iran aqui debajo como elif "SD" == tipo_seleccionado: ...
            # Nota: el _flujo_sincronizar_dispositivos_generico ya incluye su propio _clear_screen
            # y su propia pausa al final, por lo que no hace falta anadir un input() extra aqui.

        elif opcion_principal == "reload_excel":
            logger.info("Opción seleccionada: Recargar Excel")
            
            console.print(f"\n[cyan]⏳ Recargando datos desde: {ruta_excel}[/cyan]")
            try:
                # Software
                procesos.clear()
                procesos.extend(parser.extraer_procesos(ruta_excel))
                preal_list.clear()
                preal_list.extend(parser.extraer_preal(ruta_excel))
                pint_list.clear()
                pint_list.extend(parser.extraer_pint(ruta_excel))
                alarmas_list.clear()
                alarmas_list.extend(parser.extraer_alarmas(ruta_excel))
                # Hardware
                try:
                    nuevas_dimensiones = parser.extraer_dimensiones(ruta_excel)
                except Exception as e_dim:
                    logger.warning(
                        f"No se pudieron extraer dimensiones: {e_dim}. "
                        "Fallback a 0."
                    )
                    nuevas_dimensiones = DimensionesDispositivos()
                nuevos_disp_ed = parser.extraer_disp_ed(ruta_excel)
                nuevos_disp_ea = parser.extraer_disp_ea(ruta_excel)
                # Actualizar la sesion
                session.dimensiones = nuevas_dimensiones
                session.disp_ed_list = nuevos_disp_ed
                session.disp_ea_list = nuevos_disp_ea

                console.print(
                    f"[bold green]✅ Datos recargados correctamente "
                    f"(software + hardware).[/bold green]"
                )
                console.print(f"[dim]  • Procesos: {len(procesos)}[/dim]")
                console.print(f"[dim]  • PReal: {len(preal_list)}[/dim]")
                console.print(f"[dim]  • PInt: {len(pint_list)}[/dim]")
                console.print(f"[dim]  • Alarmas: {len(alarmas_list)}[/dim]")
                console.print(
                    f"[dim]  • DispED: {len(nuevos_disp_ed)} "
                    f"(N_MAX={nuevas_dimensiones.num_disp_ed})[/dim]"
                )
                console.print(
                    f"[dim]  • DispEA: {len(nuevos_disp_ea)} "
                    f"(N_MAX={nuevas_dimensiones.num_disp_ea})[/dim]"
                )
            except Exception as e:
                logger.error(f"Error recargando Excel: {e}")
                console.print(f"\n[bold red]❌ Error al recargar el Excel: {e}[/bold red]")
            input("\nPulsa Enter para volver al Menú Principal...")

        elif opcion_principal == "config_templates":
            logger.info("Opción seleccionada: Configurar Ruta de Plantillas")
            ruta_actual = config_manager.get_template_path() or "No configurada"
            console.print(f"\n[dim]Ruta actual: {ruta_actual}[/dim]")
            console.print("\nAbriendo explorador de archivos...")
            nueva_ruta = seleccionar_carpeta("Selecciona la carpeta raíz de las plantillas")
            if nueva_ruta:
                path_obj = Path(nueva_ruta)
                if path_obj.exists() and path_obj.is_dir():
                    config_manager.set_template_path(str(path_obj.absolute()))
                    console.print(
                        f"\n[bold green]✅ Ruta de plantillas guardada correctamente: {nueva_ruta}[/bold green]"
                    )
                else:
                    console.print("\n[bold red]❌ La ruta seleccionada no es válida.[/bold red]")
            else:
                console.print("\n[dim]Operación cancelada.[/dim]")
            input("\nPulsa Enter para volver al Menú Principal...")


def _print_project_summary(project_name: str, plc_names: list[str]) -> None:
    """Imprime el resumen del proyecto detectado."""
    print("\n" + "=" * 50)
    print("RESUMEN DEL PROYECTO DETECTADO")
    print("=" * 50)
    print(f"Proyecto: {project_name}")
    print(f"PLCs detectados: {len(plc_names)}")
    for i, name in enumerate(plc_names, 1):
        print(f"  {i}. {name}")
    print("=" * 50 + "\n")


def _request_confirmation() -> bool:
    """Solicita confirmación al usuario. Retorna True si acepta."""
    return questionary.confirm("¿Es este el proyecto correcto?").ask()


def run(version: str | None = None) -> None:
    """Ejecuta el flujo de automatizacion (Maquina de Estados)."""
    _clear_screen()
    logger: logging.Logger = logging.getLogger(f"{__name__}.run")
    logger.info("Iniciando flujo de automatización...")

    console.print("\n[bold cyan]⏳ Esperando selección del archivo Excel Maestro...[/bold cyan]")
    ruta_excel = seleccionar_excel()

    if not ruta_excel:
        console.print("\n[bold yellow]⚠️ Selección de archivo cancelada. Saliendo...[/bold yellow]")
        return

    # ------------------------------------------------------------------ #
    #  CARGA MAESTRA DEL EXCEL: un solo bloque, un solo spinner.
    #  Leemos TODO (software + hardware) una unica vez. Asi los flujos
    #  seran instantaneos (cero relecturas de disco).
    # ------------------------------------------------------------------ #
    with console.status(
        "[cyan]⏳ Realizando Carga Maestra del Excel en memoria...[/cyan]",
        spinner="dots",
    ):
        parser = ExcelParser()

        # 1. Software
        procesos: list[Proceso] = parser.extraer_procesos(ruta_excel)
        preal_list: list[PReal] = parser.extraer_preal(ruta_excel)
        pint_list: list[PInt] = parser.extraer_pint(ruta_excel)
        alarmas_list: list[Alarma] = parser.extraer_alarmas(ruta_excel)

        # 2. Hardware
        try:
            dimensiones = parser.extraer_dimensiones(ruta_excel)
        except Exception as e_dimensiones:
            logger.warning(
                f"No se pudieron extraer dimensiones: {e_dimensiones}. "
                "Fallback a 0."
            )
            dimensiones = DimensionesDispositivos()
        disp_ed_list: list[DispED] = parser.extraer_disp_ed(ruta_excel)
        disp_ea_list: list[DispEA] = parser.extraer_disp_ea(ruta_excel)

    console.print(
        f"\n[bold green]✅ ¡Carga Maestra completada![/bold green] "
        f"({len(procesos)} procesos, {len(preal_list)} PReal, {len(pint_list)} PInt, "
        f"{len(alarmas_list)} alarmas, {len(disp_ed_list)} DispED, "
        f"{len(disp_ea_list)} DispEA, "
        f"N_MAX ED={dimensiones.num_disp_ed} / EA={dimensiones.num_disp_ea})"
    )

    _clear_screen()
    modo_tia: str | None = questionary.select(
        "¿Cómo deseas conectar con TIA Portal?",
        choices=[
            Choice(" 🔌 Conectar a una instancia abierta", value="connect_open"),
            Choice(" 🚀 Abrir un proyecto nuevo", value="open_new"),
            Separator(),
            Choice(" ❌ Salir", value="exit")
        ]
    ).ask()

    if not modo_tia or modo_tia == "exit":
        logger.info("Usuario seleccionó Salir. Finalizando aplicación.")
        print("\n👋 Saliendo de la aplicación.")
        return

    connection_mode: str = modo_tia
    logger.info(f"Modo de conexión seleccionado: {connection_mode}")

    # ----- Composition Root -----
    # Construimos manualmente la jerarquía completa:
    #   scanner (Singleton, inyectado en todo)
    #   gateway (ciclo de vida COM)
    #   importer (usado por el repository)
    #   software_repo (logica de software, depende del gateway)
    #   session (Contexto que se pasa a los flows)
    scanner = TIAScanner()
    gateway = TIAPortalGateway(version=version, scanner=scanner)
    ts = cast(Any, load_siemens_tia())
    importer = TIAImporter(export_with_defaults_enum=ts.Enums.ExportOptions.WithDefaults)
    software_repo = SoftwareRepository(gateway=gateway, scanner=scanner, importer=importer)
    session = AppSession(
            gateway=gateway,
            software_repo=software_repo,
            scanner=scanner,
            ruta_excel=ruta_excel,  # Inyectamos al AppSession para evitar re-prompt.
            procesos=procesos,
            preal_list=preal_list,
            pint_list=pint_list,
            alarmas_list=alarmas_list,
            disp_ed_list=disp_ed_list,
            disp_ea_list=disp_ea_list,
            dimensiones=dimensiones,
        )

    try:
        if connection_mode == "open_new":
            ruta_proyecto = seleccionar_proyecto_tia()
            if not ruta_proyecto:
                console.print("[bold yellow]⚠️ No se seleccionó proyecto. Abortando.[/bold yellow]")
                return

            project_path = Path(ruta_proyecto)

            with console.status(
                "[bold green]🚀 Abriendo nueva instancia de TIA Portal "
                "(esto puede tardar unos segundos)...",
                spinner="dots"
            ):
                gateway.open_new_portal(project_path)

            logger.info(
                f"TIAPortalGateway abriendo en nueva instancia con proyecto: {project_path.name}"
            )

        # Context manager del Gateway (idempotente: attach si no se hizo open_new).
        with gateway:
            logger.info(
                f"TIA Gateway listo. Scanner ID: {id(scanner)}, Repo ID: {id(software_repo)}"
            )

            _flujo_principal_con_tia(
                parser=parser,
                ruta_excel=ruta_excel,
                procesos=procesos,
                preal_list=preal_list,
                pint_list=pint_list,
                alarmas_list=alarmas_list,
                session=session,
                logger=logger,
            )

    except PortalNotRunningError:
        logger.error("TIA Portal no está ejecutándose.")
        print("ERROR: TIA Portal no está ejecutándose. Inicie TIA Portal e intente de nuevo.")
    except ConnectionFailedError:
        logger.error("No se pudo conectar a TIA Portal.")
        print("ERROR: No se pudo conectar a TIA Portal.")
    except NoProjectOpenError:
        logger.error("No hay proyecto abierto en TIA Portal.")
        print("ERROR: No hay proyecto abierto en TIA Portal. Abra un proyecto e intente de nuevo.")
    except TIAServiceError as e:
        logger.exception("Error inesperado en TIAService.")
        print(f"ERROR: {e}")

    logger.info("Flujo de automatización finalizado.")
