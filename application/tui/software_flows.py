"""
Application Layer - TUI: Software Flows
========================================
Subrutinas pesadas de consola para los flujos de SOFTWARE:
  - Generar proceso (desde plantilla XML)
  - Sincronizar textos de DBs
  - Imprimir resumen de pre-flight

Estos flujos son invocados desde main_flow.py.

Importante: ya no reciben `tia: TIAService` por parametro.
Acceden a `session.software_repo` y `session.gateway`.
"""

import logging
from pathlib import Path
from typing import Any

import questionary
from questionary import Choice
from rich.console import Console

from application.session import AppSession
from application.use_cases.software.generar_proceso import ResultadoPreFlight
from core.models import Alarma, PInt, PReal, Proceso
from infrastructure import config_manager

from application.tui.utils import _clear_screen, _pertenece_al_proceso

__all__ = [
    "_flujo_generar_procesos",
    "_flujo_sincronizar_textos",
    "_imprimir_resumen_preflight",
]

console = Console()


def _build_tareas(proceso, preal_list, pint_list, alarmas_list) -> list[Any]:
    """Helper que construye la lista de TareaSincronizacion a partir de las
    listas ya filtradas por proceso. Vive aqui para evitar duplicacion
    entre los dos flujos TUI (generar y sincronizar)."""
    from application.use_cases.software.sincronizar_textos import TareaSincronizacion

    tareas: list[Any] = []
    db_configs = [
        (proceso.db_preal_nombre, "PReal", preal_list, True),
        (proceso.db_pint_nombre, "PInt", pint_list, True),
        (proceso.db_alm_nombre, "ALM", alarmas_list, False),
    ]
    for db_name, array_name, datos, es_parametro in db_configs:
        if not datos:
            continue
        tareas.append(
            TareaSincronizacion(
                db_name=db_name,
                array_name=array_name,
                items=list(datos),
                get_id_func=lambda x: getattr(x, "numero", 0),
                get_comment_func=lambda x: getattr(
                    x, "comentario_db", getattr(x, "texto", "")
                ),
                es_parametro=es_parametro,
            )
        )
    return tareas


def _flujo_generar_procesos(
    procesos: list[Proceso],
    preal_list: list[PReal],
    pint_list: list[PInt],
    alarmas_list: list[Alarma],
    session: AppSession,
) -> None:
    """Subrutina TUI para el flujo de generacion de procesos."""
    from application.use_cases.software.generar_proceso import (
        GenerarProcesoUseCase,
        PlantillaVaciaError,
        ProcesoOrigenNoEncontradoError,
    )
    from application.use_cases.software.sincronizar_textos import (
        SincronizarTextosUseCase,
    )

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

    uc = GenerarProcesoUseCase(session.software_repo)
    try:
        # El proceso origen se deduce PURAMENTE del nombre del archivo
        # de la plantilla. NO se consulta el Excel del usuario.
        proceso_origen = uc.deducir_proceso_origen(ruta_plantilla)
        console.print(f"[green]✅ Plantilla:[/green] {proceso_origen.nombre} (UID: {proceso_origen.uid})")
    except PlantillaVaciaError as e:
        console.print(f"[bold red]❌ {e}[/bold red]")
        return
    except ProcesoOrigenNoEncontradoError as e:
        console.print(f"[bold red]❌ {e}[/bold red]")
        return

    console.print(f"\n[cyan]⏳ Escaneando PLC y calculando colisiones...[/cyan]")
    resultado_pf = uc.ejecutar_preflight(
        ruta_plantilla, proceso_origen, proceso_destino, session.plc_seleccionado
    )

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

    # ------------------------------------------------------------------ #
    #  PRE-INYECCIÓN DE N_MAX EN LA TABLA DE VARIABLES (anti-histeresis)
    #  Antes de importar a TIA Portal, sobreescribimos los <StartValue>
    #  de las constantes N_MAX en el XML de la tabla con los valores
    #  del Excel. Si no lo hacemos, TIA compila con los valores
    #  originales de la plantilla (cero o heredados) y no recalcula las
    #  dimensiones de los DBs.
    # ------------------------------------------------------------------ #
    with console.status(
        "[cyan]⏳ Pre-inyectando N_MAX en tabla XML (anti-histéresis)...[/cyan]",
        spinner="dots",
    ):
        inyeccion_ok = uc.inyectar_constantes_en_tabla_xml(
            resultado_gen.ruta_build, proceso_destino
        )
    if inyeccion_ok:
        console.print("[bold green]✅ N_MAX pre-inyectados en la tabla XML.[/bold green]")
    else:
        console.print(
            "[bold yellow]⚠️ No se modificaron constantes N_MAX en la tabla "
            "(continuando con valores originales).[/bold yellow]"
        )

    console.print("[bold yellow]⚠️ El siguiente paso modifica TIA Portal.[/bold yellow]")
    if not questionary.confirm("¿Inyectar en el PLC ahora?").ask():
        console.print("[dim]Puedes importar .build/ manualmente.[/dim]")
        return

    console.print(
        f"[cyan]⏳ Validando proyecto e inyectando en '{session.plc_seleccionado}' (Esto puede tardar)...[/cyan]"
    )

    # ------------------------------------------------------------------ #
    #  TRANSACCION GLOBAL: agrupa inyeccion + N_MAX + Sinc. Textos en
    #  una sola entrada en el historial de TIA Portal. Si la inyeccion
    #  falla, ROLLBACK completo. Si N_MAX falla, ROLLBACK completo
    #  (incluida la inyeccion). Si la Sinc. de Textos falla, ROLLBACK
    #  completo (incluidos los 2 anteriores).
    # ------------------------------------------------------------------ #
    with session.software_repo.transaccion(
        f"Generar Proceso: {proceso_destino.nombre} (UID {proceso_destino.uid})"
    ):
        with session.gateway.silenciar_ruido():
            inyeccion_ok = uc.inyectar_en_tia(
                session.plc_seleccionado, resultado_gen.ruta_build, proceso_destino.nombre
            )

        if inyeccion_ok:
            _clear_screen()
            console.rule(
                f"[bold blue]GENERANDO PROCESO: {proceso_destino.uid} - {proceso_destino.nombre}[/bold blue]"
            )
            console.print("[bold green]✅ 1/3: Código base y estructura inyectados correctamente.[/bold green]")

            uid = proceso_destino.uid
            codigo = proceso_destino.codigo
            nombre_tabla = f"{uid}_{codigo}"

            constantes_dict = {
                f"{uid}_N_MAX_PREAL": proceso_destino.preal,
                f"{uid}_N_MAX_PINT": proceso_destino.pint,
                f"{uid}_N_MAX_ALARMAS": proceso_destino.alarmas,
                f"{uid}_N_MAX_ALARMAS_HMI": proceso_destino.alm_hmi,
            }

            with session.gateway.silenciar_ruido():
                cambios = session.software_repo.actualizar_constantes_proceso(
                    session.plc_seleccionado, nombre_tabla, constantes_dict
                )
                if cambios:
                    session.software_repo.compilar_software(session.plc_seleccionado)
                    session.software_repo.clear_cache()
                    session.software_repo.build_cache(session.plc_seleccionado)

            console.print("[bold green]✅ 2/3: Dimensiones de DataBlocks ajustadas (N_MAX).[/bold green]")

            preal_proc = [
                p for p in preal_list
                if hasattr(p, "proceso") and _pertenece_al_proceso(
                    p.proceso, proceso_destino.nombre, proceso_destino.codigo
                )
            ]
            pint_proc = [
                p for p in pint_list
                if hasattr(p, "proceso") and _pertenece_al_proceso(
                    p.proceso, proceso_destino.nombre, proceso_destino.codigo
                )
            ]
            alm_proc = [
                a for a in alarmas_list
                if hasattr(a, "proceso") and _pertenece_al_proceso(
                    a.proceso, proceso_destino.nombre, proceso_destino.codigo
                )
            ]
            tareas = _build_tareas(proceso_destino, preal_proc, pint_proc, alm_proc)

            with console.status(
                "[cyan]⏳ 3/3: Sincronizando textos y descripciones...[/cyan]",
                spinner="dots",
            ):
                uc_sync = SincronizarTextosUseCase(session.software_repo, session.scanner)
                if tareas:
                    with session.gateway.silenciar_ruido():
                        resultados = uc_sync.sincronizar_multiple_db(
                            plc_name=session.plc_seleccionado, tareas=tareas
                        )
                        session.software_repo.build_cache(
                            session.plc_seleccionado, force=True
                        )
                else:
                    resultados = {}
                    console.print("[dim]No hay datos en Excel para sincronizar textos.[/dim]")
            console.print("[green]✅ 3/3 Textos sincronizados correctamente.[/green]")

            if tareas:
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
    session: AppSession,
) -> None:
    """Subrutina TUI para sincronizar parametros y alarmas."""
    from application.use_cases.software.sincronizar_textos import SincronizarTextosUseCase

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

    preal_proc = [
        p for p in preal_list
        if hasattr(p, "proceso") and _pertenece_al_proceso(
            p.proceso, proceso.nombre, proceso.codigo
        )
    ]
    pint_proc = [
        p for p in pint_list
        if hasattr(p, "proceso") and _pertenece_al_proceso(
            p.proceso, proceso.nombre, proceso.codigo
        )
    ]
    alm_proc = [
        a for a in alarmas_list
        if hasattr(a, "proceso") and _pertenece_al_proceso(
            a.proceso, proceso.nombre, proceso.codigo
        )
    ]

    console.print(
        f"\n[dim]Datos encontrados en Excel para '{proceso.nombre}': "
        f"{len(preal_proc)} PReal, {len(pint_proc)} PInt, {len(alm_proc)} Alarmas.[/dim]"
    )

    if not questionary.confirm("¿Iniciar sincronización con TIA Portal?").ask():
        return

    # Construir tareas candidatas y descartar las que no existan en el PLC.
    tareas_candidatas = _build_tareas(proceso, preal_proc, pint_proc, alm_proc)
    tareas: list[Any] = []
    for t in tareas_candidatas:
        if not session.software_repo.bloque_existe(session.plc_seleccionado, t.db_name):
            console.print(f"[dim]- Omitiendo {t.db_name} (No existe en PLC)[/dim]")
            continue
        tareas.append(t)

    if not tareas:
        console.print("[bold yellow]⚠️ No hay bloques válidos para sincronizar.[/bold yellow]")
        return

    uc = SincronizarTextosUseCase(session.software_repo, session.scanner)

    # ------------------------------------------------------------------ #
    #  TRANSACCION GLOBAL: agrupa los 4 pasos en una sola entrada
    #  en el historial de TIA Portal. Si algo falla a mitad, ROLLBACK
    #  completo (N_MAX + DBs + cache).
    # ------------------------------------------------------------------ #
    # Spinner GLOBAL: envuelve TODAS las 4 fases (incluida la Fase 1)
    # para evitar el "⏳ 1/4" estático que parecia colgado. Cada fase
    # usa status.update() para refrescar el texto del spinner.
    resultados: dict[str, bool] = {}
    cambios_constantes = False
    with console.status(
        "[cyan]⏳ Iniciando sincronización de Parámetros y Alarmas...[/cyan]",
        spinner="dots",
    ) as status_spinner:
        with session.software_repo.transaccion(
            f"Sinc. Textos: {proceso.nombre} (UID {proceso.uid})"
        ):
            # --- Paso 1/4 ---
            status_spinner.update(
                "[cyan]⏳ 1/4 Actualizando constantes de dimensionamiento en vivo (COM)...[/cyan]"
            )
            nombre_tabla = f"{proceso.uid}_{proceso.codigo}"

            constantes_dict = {
                f"{proceso.uid}_N_MAX_PREAL": proceso.preal,
                f"{proceso.uid}_N_MAX_PINT": proceso.pint,
                f"{proceso.uid}_N_MAX_ALARMAS": proceso.alarmas,
                f"{proceso.uid}_N_MAX_ALARMAS_HMI": proceso.alm_hmi,
            }

            with session.gateway.silenciar_ruido():
                cambios_constantes = session.software_repo.actualizar_constantes_proceso(
                    session.plc_seleccionado, nombre_tabla, constantes_dict
                )
            console.print(
                "[green]✅ 1/4 Constantes N_MAX actualizadas con éxito.[/green]"
                if cambios_constantes
                else "[dim]  ↳ 1/4 Sin cambios en constantes N_MAX.[/dim]"
            )

            # --- Paso 2/4 ---
            if cambios_constantes:
                status_spinner.update(
                    "[cyan]⏳ 2/4 Compilando PLC para redimensionar DataBlocks...[/cyan]"
                )
                with session.gateway.silenciar_ruido():
                    session.software_repo.compilar_software(session.plc_seleccionado)
                    session.software_repo.clear_cache()
                    session.software_repo.build_cache(session.plc_seleccionado)
                console.print("[green]✅ 2/4 Compilación y redimensionamiento finalizados.[/green]")

            # --- Paso 3/4 ---
            status_spinner.update(
                "[cyan]⏳ 3/4 Iniciando transacción masiva de textos...[/cyan]"
            )
            with session.gateway.silenciar_ruido():
                resultados = uc.sincronizar_multiple_db(
                    plc_name=session.plc_seleccionado, tareas=tareas
                )
            console.print("[green]✅ 3/4 Textos inyectados en DataBlocks.[/green]")

            # --- Paso 4/4 ---
            status_spinner.update(
                "[cyan]⏳ 4/4 Actualizando mapa de memoria del PLC...[/cyan]"
            )
            with session.gateway.silenciar_ruido():
                session.software_repo.build_cache(
                    session.plc_seleccionado, force=True
                )
            console.print("[green]✅ 4/4 Mapa de memoria reconstruido.[/green]")

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
    """Extrae la presentacion del resumen fuera del flujo principal."""
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
