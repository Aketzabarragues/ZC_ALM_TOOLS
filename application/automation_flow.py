"""
Application Layer - Automation Flow
===================================
Máquina de Estados con Main Loop interactivo para orquestar TIA Portal.
Incluye caché de bloques para optimizar rendimiento.
"""

import logging
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import questionary
from questionary import Choice, Separator
from rich.console import Console

from application.use_cases.generar_proceso import ResultadoPreFlight
from core.models import Proceso, BloquePLC, PReal, PInt, Alarma
from infrastructure import config_manager
from infrastructure.excel_parser import ExcelParser, ExcelParsingError
from infrastructure.ui_dialogs import (
    seleccionar_excel,
    seleccionar_carpeta,
    seleccionar_proyecto_tia,
)
from infrastructure.tia_scanner import TIAScanner
from infrastructure.tia_service import (
    TIAService,
    TIAServiceError,
    PortalNotRunningError,
    ConnectionFailedError,
    NoProjectOpenError,
)

__all__ = ["run"]

console = Console()


@dataclass
class AppSession:
    """Sesión de aplicación - estado encapsulado para evitar globales."""
    scanner: TIAScanner
    plc_seleccionado: str | None = None


def _clear_screen() -> None:
    """Limpia la consola para una mejor experiencia TUI."""
    os.system('cls' if os.name == 'nt' else 'clear')


def _pertenece_al_proceso(nombre_proceso: str, nombre_destino: str, codigo_destino: str) -> bool:
    """Helper para filtrar datos por proceso (case-insensitive)."""
    if not nombre_proceso:
        return False
    p_upper = nombre_proceso.upper()
    return p_upper == (nombre_destino.upper() if nombre_destino else "") or \
           p_upper == (codigo_destino.upper() if codigo_destino else "")


def _seleccionar_plc(tia: TIAService, session: AppSession) -> str | None:
    """Selecciona y retorna el nombre del PLC, ejecutando build_cache."""
    with tia.silenciar_ruido():
        plcs = tia.get_plc_names()
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
        with tia.silenciar_ruido():
            tia.build_cache(plc_name)
            bloques = tia.get_existing_blocks(plc_name)
        console.print(f"[green]✅ Caché construido: {len(bloques)} bloques encontrados.[/green]")
    except Exception as e:
        console.print(f"[bold red]❌ Error escaneando PLC: {e}[/bold red]")
        return None

    session.plc_seleccionado = plc_name
    return plc_name


def _flujo_generar_procesos(
    procesos: list[Proceso],
    preal_list: list[PReal],
    pint_list: list[PInt],
    alarmas_list: list[Alarma],
    tia: TIAService,
    session: AppSession
) -> None:
    """Subrutina TUI para el flujo de generación de procesos."""
    from application.use_cases.generar_proceso import (
        GenerarProcesoUseCase,
        PlantillaVaciaError,
        ProcesoOrigenNoEncontradoError,
    )
    from application.use_cases.sincronizar_textos import SincronizarTextosUseCase

    if not session.plc_seleccionado:
        console.print("[bold red]❌ Selecciona un PLC primero desde el Menú Principal.[/bold red]")
        return

    _clear_screen()
    console.rule("[bold blue]GENERAR PROCESOS[/bold blue]")

    opciones = [Choice(f"{p.uid} - {p.nombre}", value=p.uid) for p in procesos]
    uid_destino: int | None = questionary.select(
        "Selecciona el proceso A GENERAR:", choices=opciones
    ).ask()

    if uid_destino is None:
        return
    proceso_destino = next(p for p in procesos if p.uid == uid_destino)

    ruta_raiz = config_manager.get_template_path()
    if not ruta_raiz or not Path(ruta_raiz).is_dir():
        console.print("[bold red]❌ Ruta de plantillas no configurada.[/bold red]")
        return

    subcarpetas = sorted(d.name for d in Path(ruta_raiz).iterdir() if d.is_dir())
    if not subcarpetas:
        console.print("[bold red]❌ No se encontraron carpetas de plantillas.[/bold red]")
        return

    plantilla = questionary.select(
        "Selecciona la plantilla base:",
        choices=[Choice(name, value=name) for name in subcarpetas]
    ).ask()

    if not plantilla:
        return
    ruta_plantilla = str(Path(ruta_raiz) / plantilla)

    uc = GenerarProcesoUseCase(tia)
    try:
        proceso_origen = uc.deducir_proceso_origen(ruta_plantilla, procesos)
        console.print(f"[green]✅ Plantilla:[/green] {proceso_origen.nombre} (UID: {proceso_origen.uid})")
    except PlantillaVaciaError as e:
        console.print(f"[bold red]❌ {e}[/bold red]")
        return
    except ProcesoOrigenNoEncontradoError as e:
        console.print(f"[bold red]❌ {e}[/bold red]")
        return

    console.print(f"\n[cyan]⏳ Escaneando PLC y calculando colisiones...[/cyan]")
    resultado_pf = uc.ejecutar_preflight(ruta_plantilla, proceso_origen, proceso_destino, session.plc_seleccionado)

    _clear_screen()
    _imprimir_resumen_preflight(resultado_pf, proceso_origen, proceso_destino, session.plc_seleccionado)

    if resultado_pf.tiene_colisiones:
        console.print("[bold red]❌ OPERACIÓN ABORTADA: Resuelve las colisiones primero.[/bold red]")
        return

    console.print("[bold green]✅ Comprobaciones previas ok.[/bold green]")

    if not questionary.confirm("¿Proceder con la generación?").ask():
        return

    resultado_gen = uc.generar_y_exportar(ruta_plantilla, proceso_origen, proceso_destino)
    if not resultado_gen.exito:
        console.print(f"[bold red]❌ Error en generación: {resultado_gen.error}[/bold red]")
        return
    console.print(f"[bold green]✅ {resultado_gen.archivos_generados} archivos generados.[/bold green]")

    console.print("[bold yellow]⚠️ El siguiente paso modifica TIA Portal.[/bold yellow]")
    if not questionary.confirm("¿Inyectar en el PLC ahora?").ask():
        console.print("[dim]Puedes importar .build/ manualmente.[/dim]")
        return

    console.print(f"[cyan]⏳ Validando proyecto e inyectando en '{session.plc_seleccionado}' (Esto puede tardar)...[/cyan]")

    with tia.silenciar_ruido():
        inyeccion_ok = uc.inyectar_en_tia(session.plc_seleccionado, resultado_gen.ruta_build, proceso_destino.nombre)

    if inyeccion_ok:
        _clear_screen()
        console.rule(f"[bold blue]GENERANDO PROCESO: {proceso_destino.uid} - {proceso_destino.nombre}[/bold blue]")
        console.print("[bold green]✅ 1/3: Código base y estructura inyectados correctamente.[/bold green]")

        # --- 1. ACTUALIZAR CONSTANTES N_MAX ---
        uid = proceso_destino.uid
        codigo = proceso_destino.codigo
        nombre_tabla = f"{uid}_{codigo}"

        constantes_dict = {
            f"{uid}_N_MAX_PREAL": proceso_destino.preal,
            f"{uid}_N_MAX_PINT": proceso_destino.pint,
            f"{uid}_N_MAX_ALARMAS": proceso_destino.alarmas,
            f"{uid}_N_MAX_ALARMAS_HMI": proceso_destino.alm_hmi
        }

        with tia.silenciar_ruido():
            cambios = tia.actualizar_constantes_proceso(session.plc_seleccionado, nombre_tabla, constantes_dict)
            if cambios:
                tia.compilar_software(session.plc_seleccionado)
                tia.clear_cache()
                tia.build_cache(session.plc_seleccionado)

        console.print("[bold green]✅ 2/3: Dimensiones de DataBlocks ajustadas (N_MAX).[/bold green]")

        # --- 2. SINCRONIZAR TEXTOS (COMENTARIOS DB) ---
        console.print("[cyan]⏳ 3/3: Sincronizando textos y descripciones...[/cyan]")
        uc_sync = SincronizarTextosUseCase(tia, session.scanner)
        # Usar helper module-level (sin closures duplicadas)
        preal_proc = [
            p for p in preal_list
            if hasattr(p, 'proceso') and _pertenece_al_proceso(
                p.proceso, proceso_destino.nombre, proceso_destino.codigo
            )
        ]
        pint_proc = [
            p for p in pint_list
            if hasattr(p, 'proceso') and _pertenece_al_proceso(
                p.proceso, proceso_destino.nombre, proceso_destino.codigo
            )
        ]
        alm_proc = [
            a for a in alarmas_list
            if hasattr(a, 'proceso') and _pertenece_al_proceso(
                a.proceso, proceso_destino.nombre, proceso_destino.codigo
            )
        ]

        tareas: list[dict[str, Any]] = []

        if preal_proc:
            tareas.append({
                "db_name": proceso_destino.db_preal_nombre,
                "array_name": "PReal",
                "items": preal_proc,
                "get_id_func": lambda x: getattr(x, 'numero', 0),
                "get_comment_func": lambda x: getattr(x, 'comentario_db', getattr(x, 'texto', '')),
                "es_parametro": True
            })
        if pint_proc:
            tareas.append({
                "db_name": proceso_destino.db_pint_nombre,
                "array_name": "PInt",
                "items": pint_proc,
                "get_id_func": lambda x: getattr(x, 'numero', 0),
                "get_comment_func": lambda x: getattr(x, 'comentario_db', getattr(x, 'texto', '')),
                "es_parametro": True
            })
        if alm_proc:
            tareas.append({
                "db_name": proceso_destino.db_alm_nombre,
                "array_name": "ALM",
                "items": alm_proc,
                "get_id_func": lambda x: getattr(x, 'numero', 0),
                "get_comment_func": lambda x: getattr(x, 'comentario_db', getattr(x, 'texto', '')),
                "es_parametro": False
            })

        if tareas:
            with tia.silenciar_ruido():
                resultados = uc_sync.sincronizar_multiple_db(plc_name=session.plc_seleccionado, tareas=tareas)
                tia.build_cache(session.plc_seleccionado, force=True)

            console.print("")
            console.rule("[bold blue]RESUMEN FINAL DE COMPONENTES[/bold blue]")
            for db_name, exito in resultados.items():
                estado = "[bold green]✅ OK[/bold green]" if exito else "[bold red]❌ Error[/bold red]"
                console.print(f"{estado} Textos en {db_name}")
        else:
            console.print("[dim]No hay datos en Excel para sincronizar textos.[/dim]")
    else:
        console.print("[bold red]❌ Fallo durante la inyección de bloques.[/bold red]")


def _flujo_sincronizar_textos(
    procesos: list[Proceso],
    preal_list: list[PReal],
    pint_list: list[PInt],
    alarmas_list: list[Alarma],
    tia: TIAService,
    session: AppSession
) -> None:
    """Subrutina TUI para sincronizar parámetros y alarmas."""
    from application.use_cases.sincronizar_textos import SincronizarTextosUseCase

    if not session.plc_seleccionado:
        console.print("[bold red]❌ Selecciona un PLC primero desde el Menú Principal.[/bold red]")
        return

    _clear_screen()
    console.rule("[bold blue]SINCRONIZAR PARÁMETROS Y ALARMAS[/bold blue]")

    opciones = [Choice(f"{p.uid} - {p.nombre}", value=p) for p in procesos]
    proceso = questionary.select(
        "Selecciona el proceso a sincronizar:", choices=opciones
    ).ask()
    if not proceso:
        return

    # Usar helper module-level (sin closures duplicadas)
    preal_proc = [
        p for p in preal_list
        if hasattr(p, 'proceso') and _pertenece_al_proceso(
            p.proceso, proceso.nombre, proceso.codigo
        )
    ]
    pint_proc = [
        p for p in pint_list
        if hasattr(p, 'proceso') and _pertenece_al_proceso(
            p.proceso, proceso.nombre, proceso.codigo
        )
    ]
    alm_proc = [
        a for a in alarmas_list
        if hasattr(a, 'proceso') and _pertenece_al_proceso(
            a.proceso, proceso.nombre, proceso.codigo
        )
    ]

    console.print(
        f"\n[dim]Datos encontrados en Excel para '{proceso.nombre}': "
        f"{len(preal_proc)} PReal, {len(pint_proc)} PInt, {len(alm_proc)} Alarmas.[/dim]"
    )

    if not questionary.confirm("¿Iniciar sincronización con TIA Portal?").ask():
        return

    tareas: list[dict[str, Any]] = []

    db_configs = [
        (proceso.db_preal_nombre, "PReal", preal_proc, True),
        (proceso.db_pint_nombre, "PInt", pint_proc, True),
        (proceso.db_alm_nombre, "ALM", alm_proc, False)
    ]

    for db_name, array_name, datos, es_parametro in db_configs:
        if not datos:
            console.print(f"[dim]- Omitiendo {db_name} (No hay datos en Excel)[/dim]")
            continue

        if not tia.bloque_existe(session.plc_seleccionado, db_name):
            console.print(f"[dim]- Omitiendo {db_name} (No existe en PLC)[/dim]")
            continue

        tareas.append({
            "db_name": db_name,
            "array_name": array_name,
            "items": datos,
            "get_id_func": lambda x: getattr(x, 'numero', 0),
            "get_comment_func": lambda x: getattr(x, 'comentario_db', getattr(x, 'texto', '')),
            "es_parametro": es_parametro
        })

    if not tareas:
        console.print("[bold yellow]⚠️ No hay bloques válidos para sincronizar.[/bold yellow]")
        return

    uc = SincronizarTextosUseCase(tia, session.scanner)

    console.print(f"\n[cyan]⏳ 1/4 Actualizando constantes de dimensionamiento en vivo (COM)...[/cyan]")

    nombre_tabla = f"{proceso.uid}_{proceso.codigo}"

    constantes_dict = {
        f"{proceso.uid}_N_MAX_PREAL": proceso.preal,
        f"{proceso.uid}_N_MAX_PINT": proceso.pint,
        f"{proceso.uid}_N_MAX_ALARMAS": proceso.alarmas,
        f"{proceso.uid}_N_MAX_ALARMAS_HMI": proceso.alm_hmi
    }

    with tia.silenciar_ruido():
        cambios_constantes = tia.actualizar_constantes_proceso(session.plc_seleccionado, nombre_tabla, constantes_dict)

    if cambios_constantes:
        console.print("[cyan]⏳ 2/4 Constantes modificadas. Compilando PLC para redimensionar DataBlocks...[/cyan]")
        with tia.silenciar_ruido():
            tia.compilar_software(session.plc_seleccionado)
            tia.clear_cache()
            tia.build_cache(session.plc_seleccionado)
    else:
        console.print("[dim]No hubo cambios en las constantes N_MAX. Omitiendo redimensionamiento.[/dim]")

    console.print(f"[cyan]⏳ 3/4 Iniciando transacción masiva de textos (Exportación -> Inyección)...[/cyan]")
    with tia.silenciar_ruido():
        resultados = uc.sincronizar_multiple_db(plc_name=session.plc_seleccionado, tareas=tareas)

    console.print(f"[cyan]⏳ 4/4 Actualizando mapa de memoria del PLC...[/cyan]")
    with tia.silenciar_ruido():
        tia.build_cache(session.plc_seleccionado, force=True)

    _clear_screen()
    console.rule("[bold blue]RESULTADO DE LA SINCRONIZACIÓN[/bold blue]")
    console.print(f"[bold cyan]✨ Proceso sincronizado:[/bold cyan] {proceso.uid} - {proceso.nombre}")

    exitosos = sum(1 for v in resultados.values() if v)
    fallidos = sum(1 for v in resultados.values() if not v)

    console.print(f"\n[dim]Resultados: {exitosos} exitosos, {fallidos} fallidos[/dim]")
    for db_name, exito in resultados.items():
        if exito:
            console.print(f"[bold green]✅ {db_name}[/bold green]")
        else:
            console.print(f"[bold red]❌ {db_name}[/bold red]")

    console.print("\n[bold green]🚀 Proceso de sincronización finalizado.[/bold green]")


def _imprimir_resumen_preflight(
    resultado: "ResultadoPreFlight",
    origen: Proceso,
    destino: Proceso,
    plc: str,
) -> None:
    """Extrae la presentación del resumen fuera del flujo principal."""
    console.rule("[bold blue]PREVISIÓN DE GENERACIÓN (PRE-CHECK)[/bold blue]")
    console.print(f"  ▶ Plantilla : [cyan]{origen.nombre}[/cyan]")
    console.print(f"  ▶ Proceso   : [yellow]{destino.nombre}[/yellow] (UID: {destino.uid})")
    console.print(f"  ▶ PLC       : [cyan]{plc}[/cyan]")
    console.print(f"  ▶ Bloques   : [cyan]{len(resultado.bloques_predichos)}[/cyan]")

    for b in resultado.bloques_predichos:
        console.print(f"     - [cyan]{b.tipo}{b.numero}[/cyan] | {b.nombre}")

    for col in resultado.colisiones_nombre:
        console.print(f"   [red][NOMBRE][/red] '{col.nombre}' ya existe.")
    for pred, exist in resultado.colisiones_numero:
        console.print(f"   [red][NÚMERO][/red] N°{pred.numero} ocupado por '{exist.nombre}'.")


def _flujo_principal_con_tia(
    tia: TIAService,
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
    project_name: str = tia.get_project_name()
    with tia.silenciar_ruido():
        plc_names: list[str] = tia.get_plc_names()

    _clear_screen()
    _print_project_summary(project_name, plc_names)
    confirmed: bool = _request_confirmation()

    if not confirmed:
        logger.info("Usuario rechazó el proyecto. Abortando.")
        print("\nOperación cancelada por el usuario.")
        return

    logger.info(f"Proyecto confirmado: {project_name}")
    print(f"\nProyecto '{project_name}' confirmado. Continuando...")

    if not _seleccionar_plc(tia, session):
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
            _seleccionar_plc(tia, session)
            input("\nPulsa Enter para continuar...")

        elif opcion_principal == "rescan":
            if session.plc_seleccionado:
                console.print(f"\n[cyan]⏳ Forzando re-escaneo completo de '{session.plc_seleccionado}'...[/cyan]")
                try:
                    with tia.silenciar_ruido():
                        tia.force_rescan(session.plc_seleccionado)
                        bloques = tia.get_existing_blocks(session.plc_seleccionado)
                    console.print(f"[bold green]✅ Caché reconstruido: {len(bloques)} bloques.[/bold green]")
                except Exception as e:
                    console.print(f"[bold red]❌ Error en re-escaneo: {e}[/bold red]")
            else:
                console.print("[bold red]❌ No hay PLC seleccionado.[/bold red]")
            input("\nPulsa Enter para continuar...")

        elif opcion_principal == "generate":
            _flujo_generar_procesos(procesos, preal_list, pint_list, alarmas_list, tia, session)
            input("\nPulsa Enter para volver al Menú Principal...")

        elif opcion_principal == "sync_texts":
            _flujo_sincronizar_textos(procesos, preal_list, pint_list, alarmas_list, tia, session)
            input("\nPulsa Enter para volver al Menú Principal...")

        elif opcion_principal == "reload_excel":
            logger.info("Opción seleccionada: Recargar Excel")
            console.print(f"\n[cyan]⏳ Recargando datos desde: {ruta_excel}[/cyan]")
            try:
                procesos.clear()
                procesos.extend(parser.extraer_procesos(ruta_excel))
                preal_list.clear()
                preal_list.extend(parser.extraer_preal(ruta_excel))
                pint_list.clear()
                pint_list.extend(parser.extraer_pint(ruta_excel))
                alarmas_list.clear()
                alarmas_list.extend(parser.extraer_alarmas(ruta_excel))
                console.print(f"[bold green]✅ Datos recargados correctamente.[/bold green]")
                console.print(f"[dim]  • Procesos: {len(procesos)}[/dim]")
                console.print(f"[dim]  • PReal: {len(preal_list)}[/dim]")
                console.print(f"[dim]  • PInt: {len(pint_list)}[/dim]")
                console.print(f"[dim]  • Alarmas: {len(alarmas_list)}[/dim]")
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
                    console.print(f"\n[bold green]✅ Ruta de plantillas guardada correctamente: {nueva_ruta}[/bold green]")
                else:
                    console.print("\n[bold red]❌ La ruta seleccionada no es válida.[/bold red]")
            else:
                console.print("\n[dim]Operación cancelada.[/dim]")
            input("\nPulsa Enter para volver al Menú Principal...")


def run(version: str | None = None) -> None:
    """Ejecuta el flujo de automatización (Máquina de Estados)."""
    _clear_screen()
    logger: logging.Logger = logging.getLogger(f"{__name__}.run")
    logger.info("Iniciando flujo de automatización...")

    console.print("\n[bold cyan]⏳ Esperando selección del archivo Excel Maestro...[/bold cyan]")
    ruta_excel = seleccionar_excel()

    if not ruta_excel:
        console.print("\n[bold yellow]⚠️ Selección de archivo cancelada. Saliendo...[/bold yellow]")
        return

    console.print(f"\n[bold cyan]⏳ Leyendo y analizando Excel Maestro...[/bold cyan]")
    console.print(f"[dim]{ruta_excel}[/dim]")

    try:
        parser = ExcelParser()
        procesos: list[Proceso] = parser.extraer_procesos(ruta_excel)
        preal_list: list[PReal] = parser.extraer_preal(ruta_excel)
        pint_list: list[PInt] = parser.extraer_pint(ruta_excel)
        alarmas_list: list[Alarma] = parser.extraer_alarmas(ruta_excel)

        console.print(
            f"[bold green]✅ ¡Excel cargado con éxito![/bold green] "
            f"({len(procesos)} procesos, {len(preal_list)} PReal, {len(pint_list)} PInt, {len(alarmas_list)} alarmas)"
        )
    except Exception as e:
        logger.exception("Error inesperado durante el parseo.")
        console.print(f"\n[bold red]❌ Fallo crítico al leer el Excel:[/bold red] {e}")
        return

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

    session = AppSession(scanner=TIAScanner())
    tia: TIAService | None = None

    try:
        tia = TIAService(version=version, scanner=session.scanner)

        if connection_mode == "open_new":
            # === ABRIR PROYECTO NUEVO ===
            ruta_proyecto = seleccionar_proyecto_tia()
            if not ruta_proyecto:
                console.print("[bold yellow]⚠️ No se seleccionó proyecto. Abortando.[/bold yellow]")
                return

            project_path = Path(ruta_proyecto)

            # Spinner de Rich mientras se abre TIA Portal (puede tardar 20-60s)
            with console.status(
                "[bold green]🚀 Abriendo nueva instancia de TIA Portal "
                "(esto puede tardar unos segundos)...",
                spinner="dots"
            ):
                tia.open_new_portal(project_path)

            logger.info(f"TIAService abriendo en nueva instancia con proyecto: {project_path.name}")

        # Context manager unificado (idempotente gracias a __enter__)
        # tia.open_new_portal() ya pobló self._portal; para connect_open,
        # __enter__() detecta _portal is None y llama a _attach().
        with tia:
            if tia._portal is None:
                raise RuntimeError(
                    "Fallo critico: TIAService no conecto correctamente."
                )
            logger.info(f"TIAService creado con scanner inyectado. Scanner ID: {id(session.scanner)}")

            # Llamada al bucle principal con el TIAService ya conectado
            _flujo_principal_con_tia(
                tia=tia,
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
