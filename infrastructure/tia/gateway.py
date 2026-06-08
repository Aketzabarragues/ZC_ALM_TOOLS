"""
Infrastructure Layer - TIA Portal Gateway
==========================================
Gestiona el ciclo de vida de la instancia COM de TIA Portal:
  - Conexion (attach) y desconexion (detach) garantista.
  - Apertura de nuevas instancias con proyectos .apXX.
  - Resolucion de objetos COM (Project, Plc) del wrapper nativo.

Es la frontera entre el mundo Python y el wrapper siemens_tia_scripting.
"""

import logging
import re
from contextlib import contextmanager
from pathlib import Path
from typing import TYPE_CHECKING, Any, Self

import psutil

from infrastructure import config_manager
from infrastructure.tia.importer import TIAImporter
from infrastructure.tia.scanner import TIAScanner
from infrastructure.tia_runtime_loader import load_siemens_tia

if TYPE_CHECKING:
    # Solo para linter (Pylance/Mypy) - no se ejecuta en runtime
    import siemens_tia_scripting as ts
else:
    # En runtime, cargamos dinamicamente desde _MEIPASS (PyInstaller) o site-packages
    ts = load_siemens_tia()  # type: ignore[assignment]

__all__ = [
    "TIAPortalGateway",
    "TIAServiceError",
    "PortalNotRunningError",
    "ConnectionFailedError",
    "NoProjectOpenError",
]


class TIAServiceError(Exception):
    """Base exception for TIA Service errors."""
    pass


class PortalNotRunningError(TIAServiceError):
    """Raised when TIA Portal process is not running."""
    pass


class ConnectionFailedError(TIAServiceError):
    """Raised when attach_portal() fails."""
    pass


class NoProjectOpenError(TIAServiceError):
    """Raised when no project is currently open in TIA Portal."""
    pass


class TIAPortalGateway:
    """
    Gateway (Context Manager) del ciclo de vida de TIA Portal.

    Responsabilidades:
      - Abrir/cerrar la instancia COM.
      - Resolver objetos Project/Plc.
      - Ofrecer utilidades (silenciar_ruido, parseo de version).

    NO manipula bloques, NO compila, NO importa: de eso se encarga
    SoftwareRepository.
    """

    PROCESS_NAME = "Siemens.Automation.Portal.exe"

    def __init__(
        self,
        version: str | None = None,
        scanner: TIAScanner | None = None,
    ) -> None:
        """
        Args:
            version: TIA Portal version en formato "major.minor" (ej. "18.0").
                     Si es None, engancha cualquier version abierta.
            scanner: TIAScanner inyectado (Composition Root). Opcional para
                     gateway puro (sin cache de bloques).
        """
        self._logger: logging.Logger = logging.getLogger(
            f"{__name__}.{self.__class__.__name__}"
        )
        self._portal: ts.Portal | None = None
        self._version: str | None = version
        self._scanner: TIAScanner | None = scanner
        self._importer: TIAImporter = TIAImporter(
            export_with_defaults_enum=ts.Enums.ExportOptions.WithDefaults
        )
        self._project: ts.Project | None = None
        # Flag reentrante: TIA Portal NO soporta transacciones anidadas.
        # Si el Gateway ya tiene una transacción abierta, un nuevo
        # `with transaccion(...)` se vuelve no-op para evitar colisión.
        self._transaction_active: bool = False

    # ------------------------------------------------------------------ #
    #  Context Manager
    # ------------------------------------------------------------------ #

    def __enter__(self) -> Self:
        """Idempotente: si open_new_portal() ya poblado self._portal, no re-engancha."""
        if self._portal is None:
            self._attach()
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: object | None,
    ) -> bool:
        """Garantiza detach al salir. Limpia cache del scanner si hubo excepcion."""
        if exc_type is not None and self._scanner is not None:
            self._logger.warning("Excepción detectada. Limpiando caché de seguridad...")
            self._scanner.clear_cache()
        self._detach()
        return False  # do not suppress exceptions

    # ------------------------------------------------------------------ #
    #  Scanner guard
    # ------------------------------------------------------------------ #

    def _get_scanner_internal(self) -> TIAScanner:
        if self._scanner is None:
            raise TIAServiceError("TIAScanner no ha sido inyectado en TIAPortalGateway.")
        return self._scanner

    # ------------------------------------------------------------------ #
    #  Attach / Detach
    # ------------------------------------------------------------------ #

    def _is_portal_running(self) -> bool:
        for proc in psutil.process_iter(["name"]):
            if proc.info["name"] == self.PROCESS_NAME:
                return True
        return False

    def _attach(self) -> None:
        self._logger.info("Checking if TIA Portal is running...")
        if not self._is_portal_running():
            raise PortalNotRunningError(
                f"Process '{self.PROCESS_NAME}' is not running."
            )

        log_path = str((Path(config_manager.get_build_root()) / "tia_wrapper_native.log").absolute())
        Path(config_manager.get_build_root()).mkdir(exist_ok=True)
        ts.set_logging(path=log_path, console=False)

        self._logger.info(f"Attaching to TIA Portal (version={self._version})...")
        try:
            if self._version:
                self._portal = ts.attach_portal(
                    portal_mode=ts.Enums.PortalMode.WithGraphicalUserInterface,  # type: ignore[arg-type]
                    version=self._version,
                )
            else:
                self._portal = ts.attach_portal(
                    portal_mode=ts.Enums.PortalMode.WithGraphicalUserInterface  # type: ignore[arg-type]
                )

            if self._portal is None:
                raise RuntimeError("Fallo critico: El objeto Portal es None tras attach.")

            pid = self._portal.get_process_id()
            self._logger.info(f"Successfully attached to TIA Portal (PID={pid}).")
        except Exception as e:
            raise ConnectionFailedError(f"Failed to attach to TIA Portal: {e}") from e
        finally:
            ts.set_logging(path=log_path, console=True)

    def _detach(self) -> None:
        if self._portal is not None:
            self._logger.info("Detaching from TIA Portal...")
            log_path = str((Path(config_manager.get_build_root()) / "tia_wrapper_native.log").absolute())
            Path(config_manager.get_build_root()).mkdir(exist_ok=True)
            ts.set_logging(path=log_path, console=False)
            try:
                self._portal.detach()
                self._logger.info("Successfully detached from TIA Portal.")
            except Exception as e:
                self._logger.warning(f"Error during detach: {e}")
            finally:
                self._portal = None
                ts.set_logging(path=log_path, console=True)

    @property
    def is_connected(self) -> bool:
        return self._portal is not None

    # ------------------------------------------------------------------ #
    #  Open New Portal
    # ------------------------------------------------------------------ #

    def _extraer_version_de_proyecto(self, project_path: Path) -> str:
        """Manejo especial para V15.1 (.ap15_1). Resto -> 'XX.0'."""
        suffix = project_path.suffix.lower()
        if suffix == '.ap15_1':
            return '15.1'
        match = re.match(r'^\.ap(\d+)$', suffix)
        if not match:
            raise TIAServiceError(
                f"Extension invalida: '{suffix}'. Se esperaba .ap15 a .ap21 (o .ap15_1)"
            )
        return f"{match.group(1)}.0"

    def open_new_portal(self, project_path: Path) -> None:
        """Abre una nueva instancia de TIA Portal con el proyecto dado."""
        if self._portal is not None:
            raise RuntimeError(
                "Ya hay un portal activo. Llama a _detach() o sal del context manager "
                "antes de abrir uno nuevo."
            )
        if not project_path.exists():
            raise TIAServiceError(f"El proyecto no existe: {project_path}")

        version_str = self._extraer_version_de_proyecto(project_path)
        self._logger.info(f"Abriendo nueva instancia de TIA Portal v{version_str}...")

        log_path = str((Path(config_manager.get_build_root()) / "tia_wrapper_native.log").absolute())
        Path(config_manager.get_build_root()).mkdir(exist_ok=True)
        ts.set_logging(path=log_path, console=False)

        try:
            self._portal = ts.open_portal(
                portal_mode=ts.Enums.PortalMode.WithGraphicalUserInterface,  # type: ignore[arg-type]
                version=version_str,
            )
            if self._portal is None:
                raise RuntimeError(
                    f"Fallo critico: TIA Portal retorno None al abrir v{version_str}"
                )

            self._project = self._portal.open_project(
                project_file_path=str(project_path),
                server_project_view=False,
            )
            self._logger.info(
                f"Proyecto '{project_path.name}' abierto en nueva instancia TIA v{version_str}."
            )
        except Exception as e:
            raise TIAServiceError(
                f"Error abriendo nueva instancia de TIA Portal: {e}"
            ) from e
        finally:
            ts.set_logging(path=log_path, console=True)

    # ------------------------------------------------------------------ #
    #  Resolucion COM
    # ------------------------------------------------------------------ #

    def _get_project_internal(self) -> ts.Project:
        if self._portal is None:
            raise TIAServiceError("Not connected to TIA Portal. Use context manager.")
        project = self._portal.get_project()
        if project is None:
            raise NoProjectOpenError("No project is currently open in TIA Portal.")
        self._project = project
        return project

    def get_project_name(self) -> str:
        project = self._get_project_internal()
        try:
            return str(project.get_property(name="Name"))  # type: ignore[call-arg]
        except Exception:
            return "Proyecto Activo"

    def get_plc_names(self) -> list[str]:
        project = self._get_project_internal()
        plcs: list[ts.Plc] = project.get_plcs()
        return [plc.get_name() for plc in plcs]

    # ------------------------------------------------------------------ #
    #  API publica para subpaquete (resolve_*)
    # ------------------------------------------------------------------ #

    def resolve_plc(self, plc_name: str) -> Any:
        """
        Resuelve y devuelve el objeto COM Plc.
        Para uso interno del subpaquete (ej. SoftwareRepository).
        Lanza TIAServiceError si el PLC no existe.
        """
        plc = self._get_plc(plc_name)
        if plc is None:
            raise TIAServiceError(f"PLC '{plc_name}' no encontrado en el proyecto.")
        return plc

    @contextmanager
    def transaccion(self, undo_text: str) -> Any:
        """
        Gestor de transacciones reentrante.

        Si ya hay una transacción abierta en el Gateway, cede el
        control (no-op) para no anidar (TIA no lo soporta).

        Si no la hay, abre la transacción, hace COMMIT en éxito
        o ROLLBACK en excepción, y libera el flag en finally.
        """
        if self._transaction_active:
            self._logger.debug(
                f"Transacción ya activa. Omitiendo start_transaction para: {undo_text}"
            )
            yield
            return

        project = self.resolve_project()
        self._logger.info(f"Iniciando transacción global: {undo_text}")
        # dialog_text es OBLIGATORIO segun el manual (sec 2.37.27):
        #   "start_transaction(undo_text: str, dialog_text: str)"
        # Lo reutilizamos del undo_text para que el usuario vea el mismo
        # mensaje en el dialogo de confirmacion de TIA.
        project.start_transaction(undo_text=undo_text, dialog_text=undo_text)
        self._transaction_active = True
        # Flag de éxito: si yield levanta una excepción, exito queda False
        # y el finally NO ejecutará el COMMIT (solo el ROLLBACK ya hecho
        # en el except). Asi evitamos el bug clásico de hacer CommitOnDispose
        # o end_transaction(rollback=False) despues de un error, lo que
        # puede dejar TIA Portal en estado inconsistente o bloquearlo.
        exito: bool = False
        try:
            yield
            exito = True
        except Exception as e:
            self._logger.error(f"Transacción abortada por excepción: {e}")
            try:
                project.end_transaction(rollback=True)
                self._logger.info("ROLLBACK ejecutado correctamente.")
            except Exception as rollback_err:
                self._logger.critical(
                    f"Fallo crítico durante el rollback: {rollback_err}. "
                    "TIA Portal podría estar bloqueado."
                )
            raise
        finally:
            if exito:
                try:
                    project.end_transaction(rollback=False)
                    self._logger.info("Transacción exitosa. COMMIT confirmado por TIA Portal.")
                except Exception as commit_err:
                    self._logger.critical(
                        f"Fallo crítico durante el commit: {commit_err}. "
                        "TIA Portal podría estar bloqueado."
                    )
            # El flag se libera SIEMPRE, haya habido exito, excepcion
            # o incluso un fallo en el commit/rollback. Asi la proxima
            # transaccion no queda atrapada como no-op.
            self._transaction_active = False

    def resolve_project(self) -> Any:
        """Resuelve y devuelve el objeto COM Project activo."""
        return self._get_project_internal()

    def _get_plc(self, plc_name: str) -> ts.Plc | None:
        project = self._get_project_internal()
        plcs: list[ts.Plc] = project.get_plcs()

        for plc in plcs:
            if plc.get_name() == plc_name:
                return plc

        self._logger.error(f"PLC '{plc_name}' no encontrado en el proyecto.")
        return None

    # ------------------------------------------------------------------ #
    #  Utilidad base
    # ------------------------------------------------------------------ #

    @contextmanager
    def silenciar_ruido(self):
        """Silencia el output nativo del wrapper durante un bloque 'with'."""
        log_path = str((Path(config_manager.get_build_root()) / "tia_wrapper_native.log").absolute())
        Path(config_manager.get_build_root()).mkdir(exist_ok=True)

        ts.set_logging(path=log_path, console=False)
        try:
            yield
        finally:
            ts.set_logging(path=log_path, console=True)
