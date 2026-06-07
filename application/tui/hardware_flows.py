"""
Application Layer - TUI: Hardware Flows
========================================
Subrutina de consola para la sincronizacion hibrida (COM + XML) de
dispositivos hardware (ED = Entradas Digitales).

Este modulo se mantiene desacoplado del resto de TUI: recibe
un AppSession (Composition Root) y NO instancia dependencias
de TIA Portal directamente.

Los datos del Excel (DispED + dimensiones) ya vienen precargados
en el AppSession (Carga Maestra en run()), por lo que este
flujo no relee el disco: es instantaneo.
"""

from pathlib import Path

import questionary
from rich.console import Console

from infrastructure import config_manager

from application.session import AppSession
from application.use_cases.hardware.sincronizar_dispositivos import (
    SincronizarDispositivosUseCase,
)
from application.tui.utils import _clear_screen

__all__ = ["_flujo_sincronizar_dispositivos"]

console = Console()


def _flujo_sincronizar_dispositivos(session: AppSession) -> None:
    """
    Flujo TUI para sincronizar las UserConstants de dispositivos ED
    del PLC abierto en la sesion.

    Lee los datos pre-cargados del Excel desde `session` (no toca disco):
      - session.disp_ed_list   (Carga Maestra)
      - session.dimensiones    (Carga Maestra)
      - session.plc_seleccionado (menu principal)
      - session.software_repo  (inyectado en el Composition Root)
    """
    _clear_screen()
    console.rule("[bold blue]SINCRONIZAR DISPOSITIVOS (ED)[/bold blue]")

    # ------------------------------------------------------------------ #
    #  Guardas: PLC seleccionado + Carga Maestra presente
    # ------------------------------------------------------------------ #
    if not session.plc_seleccionado:
        console.print(
            "[bold red]❌ Selecciona un PLC primero desde el Menú Principal.[/bold red]"
        )
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    disp_ed_list = session.disp_ed_list
    dimensiones = session.dimensiones

    if not disp_ed_list:
        console.print(
            "[bold yellow]⚠️ La Carga Maestra del Excel no devolvio DispED. "
            "Verifica que la hoja 'DISP_ED' y la tabla 'Tabla_Disp_ED' existen.[/bold yellow]"
        )
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    if dimensiones is None:
        dimensiones = session.dimensiones  # ya viene con default_factory en dataclass

    # ------------------------------------------------------------------ #
    #  Resumen + confirmacion (instantaneo, sin lectura de disco)
    # ------------------------------------------------------------------ #
    console.print(
        f"\n[green]✅ Datos en memoria (Carga Maestra):[/green] "
        f"{len(disp_ed_list)} DispED, N_MAX = {dimensiones.num_disp_ed}"
    )

    if not questionary.confirm("¿Proceder con la sincronización de dispositivos?").ask():
        console.print("[dim]Operación cancelada por el usuario.[/dim]")
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    # ------------------------------------------------------------------ #
    #  Ejecucion del caso de uso
    # ------------------------------------------------------------------ #
    use_case = SincronizarDispositivosUseCase(session.software_repo)
    build_root = Path(config_manager.get_build_root())
    export_dir = str((build_root / "hardware").absolute())

    try:
        # Usar el context manager nativo del Gateway para silenciar
        # la salida del wrapper C# de TIA Portal durante la sincronizacion.
        with session.gateway.silenciar_ruido():
            use_case.ejecutar(
                plc_name=session.plc_seleccionado,
                hw_type="ed",
                dispositivos=disp_ed_list,
                dimensiones=dimensiones,
                export_dir=export_dir,
            )
    except Exception as e:
        console.print(f"[bold red]❌ Error durante la sincronización: {e}[/bold red]")
        input("\nPulsa Enter para volver al Menú Principal...")
        return

    # ------------------------------------------------------------------ #
    #  Cierre con pausa (unica pausa, main_flow NO añade otra)
    # ------------------------------------------------------------------ #
    console.print(
        f"\n[bold green]✅ Sincronización de dispositivos finalizada "
        f"contra PLC '{session.plc_seleccionado}'.[/bold green]"
    )
    input("\nPulsa Enter para volver al Menú Principal...")
