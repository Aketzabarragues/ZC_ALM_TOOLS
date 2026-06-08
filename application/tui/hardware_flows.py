"""
Application Layer - TUI: Hardware Flows
=======================================
Subrutina de consola para la sincronizacion hibrida (COM + XML) de
dispositivos hardware (ED, EA, SD, M, MVF, ...).

Este modulo se mantiene desacoplado del resto de TUI: recibe
un AppSession (Composition Root) y NO instancia dependencias
de TIA Portal directamente.

Los datos del Excel (DispED, DispEA, ...) y las dimensiones ya
vienen precargados en el AppSession (Carga Maestra en run()),
por lo que este flujo no relee el disco: es instantaneo.

DRY: existe una sola funcion generica parametrizada por:
    - hw_type (str): "ed", "ea", "sd", ...
    - nombre_humano (str): etiqueta para mensajes en pantalla
    - dispositivos (list[DispositivoHardware]): la lista a sincronizar
    - subdir (str): subcarpeta de exportacion (ej: "hardware/ed")
Y dos wrappers de una sola linea para cada dispositivo soportado,
de modo que añadir un nuevo tipo es 1 linea de wrapper + 1 linea
de Choice en main_flow.
"""

from collections.abc import Sequence
from pathlib import Path

import questionary
from rich.console import Console
from rich.table import Table

from application.session import AppSession
from application.tui.utils import _clear_screen
from application.use_cases.hardware.sincronizar_dispositivos import (
    SincronizarDispositivosUseCase,
)
from core.models import DispositivoHardware
from infrastructure import config_manager

__all__ = [
    "_flujo_sincronizar_dispositivos",
    "_flujo_sincronizar_dispositivos_ea",
]

console = Console()


def _flujo_sincronizar_dispositivos_generico(
    session: AppSession,
    hw_type: str,
    nombre_humano: str,
    dispositivos: Sequence[DispositivoHardware],
    subdir: str,
) -> None:
    """
    Flujo TUI generico para sincronizar cualquier tipo de dispositivo
    (ED, EA, SD, M, MVF) contra el PLC abierto en la sesion.

    Args:
        session: Contexto de la aplicacion (inyectado).
        hw_type: Codigo del tipo ("ed", "ea", ...). Se pasa al use case.
        nombre_humano: Etiqueta legible para mensajes en pantalla (ej: "ED", "EA").
        dispositivos: Lista de dispositivos a sincronizar (Carga Maestra).
        subdir: Subcarpeta de exportacion (ej: "hardware/ed", "hardware/ea").
    """
    _clear_screen()
    console.rule(f"[bold blue]SINCRONIZAR DISPOSITIVOS ({nombre_humano})[/bold blue]")

    # ------------------------------------------------------------------ #
    #  Guardas: PLC seleccionado + Carga Maestra presente
    # ------------------------------------------------------------------ #
    if not session.plc_seleccionado:
        console.print(
            "[bold red]❌ Selecciona un PLC primero desde el Menú Principal.[/bold red]"
        )
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    dimensiones = session.dimensiones

    if not dispositivos:
        console.print(
            f"[bold yellow]⚠️ La Carga Maestra del Excel no devolvio dispositivos "
            f"de tipo '{nombre_humano}'. "
            f"Verifica que la hoja y la tabla del Excel existen para este tipo.[/bold yellow]"
        )
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    # ------------------------------------------------------------------ #
    #  Resumen + confirmacion (instantaneo, sin lectura de disco)
    # ------------------------------------------------------------------ #
    # Mostramos el N_MAX correspondiente al tipo de dispositivo, leyendo
    # el atributo del DimensionesDispositivos por convencion `num_disp_<hw_type>`.
    n_max_attr = f"num_disp_{hw_type}"
    n_max = getattr(dimensiones, n_max_attr, 0)

    console.print(
        f"\n[green]✅ Datos en memoria (Carga Maestra):[/green] "
        f"{len(dispositivos)} {nombre_humano}, N_MAX = {n_max}"
    )

    # ------------------------------------------------------------------ #
    #  Ejecucion del caso de uso (se instancia AQUI para reusarse
    #  tanto en la prevision como en la sincronizacion real).
    # ------------------------------------------------------------------ #
    use_case = SincronizarDispositivosUseCase(session.software_repo)
    build_root = Path(config_manager.get_build_root())
    export_dir = str((build_root / "hardware" / subdir).absolute())

    # ------------------------------------------------------------------ #
    #  PREVISION DE CAMBIOS (DRY-RUN): exporta la tabla actual, parsea
    #  el XML, compara con el Excel y renderiza una tabla Rich.
    #  Si falla (tabla no existe en proyecto virgen), NO abortamos.
    # ------------------------------------------------------------------ #
    try:
        console.print(
            f"\n[cyan]Leyendo estado actual del PLC para {nombre_humano}...[/cyan]"
        )
        with session.gateway.silenciar_ruido():
            diff_report = use_case.generar_prevision(
                plc_name=session.plc_seleccionado,
                hw_type=hw_type,
                dispositivos=dispositivos,
                export_dir=export_dir,
            )

        if not diff_report:
            console.print(
                "[yellow]No hay variables del Excel con plc_tag definido. "
                "Nada que sincronizar.[/yellow]"
            )
        else:
            tabla = Table(
                title=f"PREVISIÓN DE CAMBIOS ({nombre_humano})",
                show_header=True,
                header_style="bold magenta",
            )
            tabla.add_column("Index", style="dim", width=6)
            tabla.add_column("Variable Actual (PLC)", min_width=25)
            tabla.add_column("Nueva Variable (Excel)", min_width=25)
            tabla.add_column("Acción / Estado", min_width=20)

            nuevos, sin_cambios, renombrar, eliminar = 0, 0, 0, 0

            for fila in diff_report:
                # Contadores
                if "Nueva" in fila["estado"]: nuevos += 1
                elif "Sin cambios" in fila["estado"]: sin_cambios += 1
                elif "Renombrar" in fila["estado"]: renombrar += 1
                elif "Eliminar" in fila["estado"]: eliminar += 1

                # Colores
                color = "white"
                if "Nueva" in fila["estado"]: color = "green"
                elif "Renombrar" in fila["estado"]: color = "yellow"
                elif "Eliminar" in fila["estado"]: color = "red"

                tabla.add_row(
                    str(fila["index"]),
                    fila["tag_plc"],
                    fila["tag_excel"],
                    f"[{color}]{fila['estado']}[/{color}]",
                )

            console.print(tabla)
            console.print(
                f"[dim]✅ Resumen: {len(dispositivos)} dispositivos en Excel | "
                f"{sin_cambios} sin cambios, {nuevos} a crear, "
                f"{renombrar} a renombrar, {eliminar} a eliminar.[/dim]\n"
            )
    except Exception as e:
        console.print(
            f"[yellow]⚠️ No se pudo generar la prevision visual: {e}[/yellow]"
        )

    if not questionary.confirm("¿Proceder con la sincronización de dispositivos?").ask():
        console.print("[dim]Operación cancelada por el usuario.[/dim]")
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    # Tras confirmar, limpiamos la pantalla para que el rastro impreso
    # de las fases completadas quede limpio y no compita con la tabla
    # de prevision que acabamos de renderizar.
    console.clear()
    console.print(
        f"[bold cyan]{'─' * 44} SINCRONIZANDO ({nombre_humano}) {'─' * 44}[/bold cyan]\n"
    )

    try:
        # Spinner de Rich que se actualiza dinámicamente desde el callback
        # del caso de uso (progress_callback). Cada fase (1/4 a 4/4) refresca
        # el texto del spinner con `status_spinner.update(...)`.
        # Ademas, llevamos un contador `ultimo_paso_visto` para IMPRIMIR
        # (no solo actualizar) cada fase al terminar, de modo que quede un
        # registro visual permanente aunque el spinner desaparezca al final.
        ultimo_paso_visto: int = 0

        with console.status("Iniciando sincronización...") as status_spinner:
            def _update_status(mensaje: str) -> None:
                nonlocal ultimo_paso_visto
                # Detectar si el mensaje empieza por "X/Y" (ej: "1/4", "2/4")
                import re
                match = re.match(r"^(\d+)/", mensaje)
                if match:
                    paso_actual = int(match.group(1))
                    # Si hemos avanzado de paso, imprimimos el anterior
                    # como completado ANTES de empezar el nuevo.
                    if paso_actual > ultimo_paso_visto and ultimo_paso_visto > 0:
                        console.print(
                            f"[green]✅ Fase {ultimo_paso_visto} completada.[/green]"
                        )
                    ultimo_paso_visto = paso_actual

                status_spinner.update(f"⏳ {mensaje}")

            # Usar el context manager nativo del Gateway para silenciar
            # la salida del wrapper C# de TIA Portal durante la sincronizacion.
            with session.gateway.silenciar_ruido():
                use_case.ejecutar(
                    plc_name=session.plc_seleccionado,
                    hw_type=hw_type,
                    dispositivos=dispositivos,
                    dimensiones=dimensiones,
                    export_dir=export_dir,
                    progress_callback=_update_status,
                )

        # Al salir del with, imprimir la última fase como completada
        # (porque la lógica del callback solo imprime al AVANZAR de fase,
        # no al terminar la última).
        if ultimo_paso_visto > 0:
            console.print(f"[green]✅ Fase {ultimo_paso_visto} completada.[/green]\n")

        
    except Exception as e:
        console.print(f"[bold red]❌ Error durante la sincronización: {e}[/bold red]")
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    # ------------------------------------------------------------------ #
    #  Cierre con pausa (unica pausa, main_flow NO añade otra)
    # ------------------------------------------------------------------ #
    console.print(
        f"\n[bold green]✅ Sincronización de dispositivos {nombre_humano} finalizada "
        f"contra PLC '{session.plc_seleccionado}'.[/bold green]"
    )
    input("\nPulsa Enter para volver al Menú Principal...")


# ------------------------------------------------------------------ #
#  Wrappers especificos: una sola linea cada uno.
#  Añadir un nuevo tipo (SA, V, M, MVF) = 1 wrapper + 1 Choice en main_flow.
# ------------------------------------------------------------------ #
def _flujo_sincronizar_dispositivos(session: AppSession) -> None:
    """Wrapper para Entradas Digitales (ED)."""
    _flujo_sincronizar_dispositivos_generico(
        session=session,
        hw_type="ed",
        nombre_humano="ED",
        dispositivos=session.disp_ed_list,
        subdir="ed",
    )


def _flujo_sincronizar_dispositivos_ea(session: AppSession) -> None:
    """Wrapper para Entradas Analogicas (EA)."""
    _flujo_sincronizar_dispositivos_generico(
        session=session,
        hw_type="ea",
        nombre_humano="EA",
        dispositivos=session.disp_ea_list,
        subdir="ea",
    )
