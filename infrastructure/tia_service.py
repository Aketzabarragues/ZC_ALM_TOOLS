"""
Infrastructure Layer - TIA Portal Service (Facade)
==================================================
ÚNICO módulo que importa siemens_tia_scripting.
Maneja conexión/desconexión con TIA Portal como Context Manager.
Delega el escaneo de bloques en TIAScanner (patrón Facade).
"""

import logging
import os
import re
from contextlib import contextmanager
from pathlib import Path
from typing import Any, Self

import psutil

from core.models import BloquePLC
from infrastructure.tia_scanner import TIAScanner
from infrastructure.tia_importer import TIAImporter
from infrastructure.tia_runtime_loader import load_siemens_tia
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    # Solo para linter (Pylance/Mypy) - no se ejecuta en runtime
    import siemens_tia_scripting as ts
else:
    # En runtime, cargamos dinámicamente desde _MEIPASS (PyInstaller) o site-packages
    ts = load_siemens_tia()  # type: ignore[assignment]

__all__ = ["TIAServiceError", "TIAService"]


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


class TIAService:
    """
    Context Manager for TIA Portal scripting (Facade).

    Usage:
        with TIAService(version="18.0") as tia:
            project = tia.get_project()
            # work with project...
        # automatically detaches on exit

    Attribarutes:
        _portal: Internal Portal instance (private, not exposed).
        _version: TIA Portal version string (e.g., "18.0").
        scanner: TIAScanner instance for block extraction.
    """

    PROCESS_NAME = "Siemens.Automation.Portal.exe"

    def __init__(self, version: str | None = None, scanner: TIAScanner | None = None) -> None:
        """
        Initialize TIA Service.

        Args:
            version: TIA Portal version in format "major.minor" (e.g., "18.0").
                     If None, attaches to any running version.
            scanner: Optional TIAScanner injected (Composition Root).
        """
        self._logger: logging.Logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        self._portal: ts.Portal | None = None
        self._version: str | None = version
        # El scanner se inyecta desde el Composition Root, no se instancia internamente
        self._scanner: TIAScanner | None = scanner
        self._importer: TIAImporter = TIAImporter(
            export_with_defaults_enum=ts.Enums.ExportOptions.WithDefaults
        )
        self._project: ts.Project | None = None

    def __enter__(self) -> Self:
        """Establish connection to TIA Portal.

        Idempotente: si ya hay un portal abierto (p.ej. tras open_new_portal),
        no re-engancha. Esto permite usar el context manager tanto para attach
        como para instancias nuevas.
        """
        if self._portal is None:
            self._attach()
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: object | None
    ) -> bool:
        """Guarantee portal detachment on exit. Blindar estado del scanner si hay excepción."""
        if exc_type is not None and self._scanner is not None:
            self._logger.warning("Excepción detectada. Limpiando caché de seguridad...")
            self._scanner.clear_cache()
        self._detach()
        return False  # do not suppress exceptions

    def _get_scanner_internal(self) -> TIAScanner:
        """
        Internal: get Scanner instance or raise error if not injected.
        Este método actúa como guard clause para evitar OptionalMemberAccess de Pylance.
        """
        if self._scanner is None:
            raise TIAServiceError("TIAScanner no ha sido inyectado en TIAService.")
        return self._scanner

    def _is_portal_running(self) -> bool:
        """Check if Siemens.Automation.Portal.exe is running on Windows."""
        for proc in psutil.process_iter(["name"]):
            if proc.info["name"] == self.PROCESS_NAME:
                return True
        return False

    def _attach(self) -> None:
        """Attach to running TIA Portal instance."""
        self._logger.info("Checking if TIA Portal is running...")
        if not self._is_portal_running():
            msg = f"Process '{self.PROCESS_NAME}' is not running."
            self._logger.error(msg)
            raise PortalNotRunningError(msg)

        # Blindar conexión: apagar consola antes de Attach
        log_path = str(Path(".build/tia_wrapper_native.log").absolute())
        Path(".build").mkdir(exist_ok=True)
        ts.set_logging(path=log_path, console=False)

        self._logger.info(f"Attaching to TIA Portal (version={self._version})...")
        try:
            if self._version:
                self._portal = ts.attach_portal(
                    portal_mode=ts.Enums.PortalMode.WithGraphicalUserInterface,  # type: ignore[arg-type]
                    version=self._version
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
            msg = f"Failed to attach to TIA Portal: {e}"
            self._logger.error(msg)
            raise ConnectionFailedError(msg) from e
        finally:
            # Restaurar consola inmediatamente después de la conexión
            ts.set_logging(path=log_path, console=True)

    def _detach(self) -> None:
        """Detach from TIA Portal instance."""
        if self._portal is not None:
            self._logger.info("Detaching from TIA Portal...")
            # Silenciar log nativo durante desconexión
            log_path = str(Path(".build/tia_wrapper_native.log").absolute())
            Path(".build").mkdir(exist_ok=True)
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
        """Check if currently attached to TIA Portal."""
        return self._portal is not None

    def get_project_name(self) -> str:
        """Get the name of the currently open project."""
        project = self._get_project_internal()
        try:
            return str(project.get_property(name="Name"))  # type: ignore[call-arg]
        except Exception:
            return "Proyecto Activo"

    def get_plc_names(self) -> list[str]:
        """Get names of all PLCs in the current project."""
        project = self._get_project_internal()
        plcs: list[ts.Plc] = project.get_plcs()
        return [plc.get_name() for plc in plcs]

    def _extraer_version_de_proyecto(self, project_path: Path) -> str:
        """
        Extrae la versión de TIA Portal a partir de la extensión del archivo de proyecto.

        Manejo especial para V15.1 cuya extensión es .ap15_1.
        Para el resto (.ap18, .ap17, etc.) extrae el número y añade '.0'.
        """
        suffix = project_path.suffix.lower()
        if suffix == '.ap15_1':
            return '15.1'
        # El resto: .ap15, .ap16, .ap17, .ap18, .ap19, .ap20, .ap21
        match = re.match(r'^\.ap(\d+)$', suffix)
        if not match:
            raise TIAServiceError(
                f"Extension invalida: '{suffix}'. Se esperaba .ap15 a .ap21 (o .ap15_1)"
            )
        return f"{match.group(1)}.0"

    def open_new_portal(self, project_path: Path) -> None:
        """
        Abre una nueva instancia de TIA Portal y carga el proyecto indicado.

        Args:
            project_path: Ruta completa al archivo de proyecto (.apXX o .ap15_1).

        Raises:
            TIAServiceError: Si la extensión no es válida o falla la apertura.
            RuntimeError: Si ya hay un portal activo (proteccion anti-zombi).
        """
        # Proteccion anti-zombi: no abrir otra instancia si ya hay un portal
        # enganchado. El usuario debe primero hacer _detach() o salir del with.
        if self._portal is not None:
            raise RuntimeError(
                "Ya hay un portal activo. Llama a _detach() o sal del context manager "
                "antes de abrir uno nuevo."
            )

        if not project_path.exists():
            raise TIAServiceError(f"El proyecto no existe: {project_path}")

        version_str = self._extraer_version_de_proyecto(project_path)
        self._logger.info(f"Abriendo nueva instancia de TIA Portal v{version_str}...")

        # Apagar consola para no contaminar con logs nativos durante la carga
        log_path = str(Path(".build/tia_wrapper_native.log").absolute())
        Path(".build").mkdir(exist_ok=True)
        ts.set_logging(path=log_path, console=False)

        try:
            self._portal = ts.open_portal(
                portal_mode=ts.Enums.PortalMode.WithGraphicalUserInterface,  # type: ignore[arg-type]
                version=version_str
            )
            if self._portal is None:
                raise RuntimeError(
                    f"Fallo critico: TIA Portal retorno None al abrir v{version_str}"
                )

            self._project = self._portal.open_project(
                project_file_path=str(project_path),
                server_project_view=False
            )
            self._logger.info(
                f"Proyecto '{project_path.name}' abierto en nueva instancia TIA v{version_str}."
            )
        except Exception as e:
            msg = f"Error abriendo nueva instancia de TIA Portal: {e}"
            self._logger.error(msg)
            raise TIAServiceError(msg) from e
        finally:
            ts.set_logging(path=log_path, console=True)

    def _get_project_internal(self) -> ts.Project:
        """Internal: get Project instance or raise. Also caches it in self.project."""
        if self._portal is None:
            raise TIAServiceError("Not connected to TIA Portal. Use context manager.")
        project = self._portal.get_project()
        if project is None:
            raise NoProjectOpenError("No project is currently open in TIA Portal.")
        self._project = project  # Cache for transactions
        return project

    def _get_plc(self, plc_name: str) -> ts.Plc | None:
        """
        Obtiene el objeto Plc por nombre usando la API de Python.

        Args:
            plc_name: Nombre del PLC a buscar.

        Returns:
            Objeto Plc o None si no se encuentra.
        """
        project = self._get_project_internal()
        plcs: list[ts.Plc] = project.get_plcs()

        for plc in plcs:
            if plc.get_name() == plc_name:
                return plc

        self._logger.error(f"PLC '{plc_name}' no encontrado en el proyecto.")
        return None

    def get_existing_blocks(self, plc_name: str) -> dict[str, BloquePLC]:
        """
        Obtiene los bloques existentes del caché del scanner.

        Args:
            plc_name: Nombre del PLC objetivo.

        Returns:
            Dict con los nombres como clave y objetos BloquePLC como valores.
        """
        return self._get_scanner_internal().get_cached_blocks()

    def build_cache(self, plc_name: str, force: bool = False) -> dict[str, BloquePLC]:
        """
        Construye el caché de bloques del PLC.

        Args:
            plc_name: Nombre del PLC a escanear.
            force: Si True, fuerza el re-escaneo.

        Returns:
            Diccionario con bloques cacheados.
        """
        plc = self._get_plc(plc_name)
        if not plc:
            raise TIAServiceError(f"No se pudo acceder al PLC: {plc_name}")
        return self._get_scanner_internal().build_cache(plc_name, plc, force)

    def clear_cache(self) -> None:
        """Vacía el caché de bloques del scanner."""
        self._get_scanner_internal().clear_cache()

    def force_rescan(self, plc_name: str) -> dict[str, BloquePLC]:
        """Fuerza un re-escaneo completo del PLC."""
        return self.build_cache(plc_name, force=True)

    def is_bloque_consistente(self, plc_name: str, block_name: str) -> bool:
        """
        Verifica si un bloque está compilado y consistente.
        IMPORTANTE: Envuelve en try/except para manejar bloques protegidos
        (Know-How Protect) o bloques de sistema cerrados.
        Si no podemos leerlo, asumimos TRUE para no forzar compilación innecesaria.
        """
        try:
            bloque_dto = self._get_scanner_internal().find_block_case_insensitive(block_name)
            if not bloque_dto:
                return False
            
            plc = self._get_plc(plc_name)
            if not plc:
                return False
                
            program_blocks = plc.get_program_blocks()
            com_block = self._importer.find_block_in_group(program_blocks, bloque_dto.nombre)
            
            if com_block:
                # is_consistent() es método directo - puede lanzar en bloques protegidos
                return bool(com_block.is_consistent())
            return False
        except Exception as e:
            self._logger.warning(f"No se pudo verificar consistencia de '{block_name}' (bloque protegido o inaccesible): {e}")
            # Por seguridad, asumimos TRUE (no forzar compilación)
            return True

    def compilar_software(self, plc_name: str) -> bool:
        """
        Compila el software del PLC.
        Retorna True si la compilación fue EXITOSA (0 errores).
        
        Nota: En Siemens, compile_software() retorna True si hay errores,
        False si fue exitoso. Se invierte la lógica aquí para semantics claras.
        """
        plc = self._get_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False
            
        self._logger.info(f"⏳ Iniciando compilación de software para '{plc_name}'...")
        try:
            has_errors: bool = plc.compile_software()
            
            if has_errors:
                self._logger.error(f"❌ Fallo de compilación en el PLC '{plc_name}'. Revisa TIA Portal.")
                return False
                
            self._logger.info(f"✅ Compilación exitosa para '{plc_name}'.")
            return True
        except Exception as e:
            self._logger.error(f"Fallo al invocar la compilación: {e}")
            return False

    def bloque_existe(self, plc_name: str, block_name: str) -> bool:
        """Verifica si un bloque existe en el PLC usando el buscador normalizado del scanner."""
        try:
            # Usamos la búsqueda case-insensitive para ignorar mayúsculas de Excel
            return self._get_scanner_internal().find_block_case_insensitive(block_name) is not None
        except Exception as e:
            self._logger.error(f"Error comprobando existencia del bloque {block_name}: {e}")
            return False

    def obtener_ruta_bloque(self, plc_name: str, block_name: str) -> str | None:
        """
        Busca el bloque en la caché y devuelve su ruta (String puro, sin llamadas COM).
        """
        try:
            bloque_dto = self._get_scanner_internal().find_block_case_insensitive(block_name)
            if bloque_dto:
                return bloque_dto.ruta
            return None
        except Exception as e:
            self._logger.error(f"Error obteniendo ruta del bloque {block_name}: {e}")
            return None

    def importar_bloques_generados(
        self,
        plc_name: str,
        ruta_build: str,
        proceso_nombre: str = "desconocido"
    ) -> bool:
        """
        Delega la importación de archivos XML al TIAImporter.

        Args:
            plc_name: Nombre del PLC objetivo.
            ruta_build: Ruta al directorio .build/ con los XML mutados.
            proceso_nombre: Nombre del proceso a generar (para el dialog_text).

        Returns:
            True si la importación fue exitosa.
        """
        self._logger.info(f"Delegando importación al TIAImporter para PLC '{plc_name}'...")
        plc = self._get_plc(plc_name)
        if not plc:
            raise TIAServiceError(f"No se pudo acceder al PLC: {plc_name}")

        if not self._project:
            raise TIAServiceError("No hay un proyecto de TIA Portal abierto para iniciar la transacción.")

        return self._importer.importar_proyecto(self._project, plc, ruta_build, proceso_nombre)

    def exportar_bloque(self, plc_name: str, block_name: str, target_dir: str) -> bool:
        """
        Exporta un bloque PLC a un directorio de forma plana.

        Args:
            plc_name: Nombre del PLC.
            block_name: Nombre del bloque a exportar.
            target_dir: Directorio destino para la exportación.

        Returns:
            True si la exportación fue exitosa.
        """
        plc = self._get_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False
        return self._importer.exportar_bloque(plc, block_name, target_dir)

    def importar_bloque_single(
        self,
        plc_name: str,
        xml_file_path: str,
        target_relative_path: str = ""
    ) -> bool:
        """
        Importa un único bloque XML usando carpeta staging.

        Args:
            plc_name: Nombre del PLC objetivo.
            xml_file_path: Ruta al archivo XML a importar.
            target_relative_path: Ruta relativa del grupo destino.

        Returns:
            True si la importación fue exitosa.
        """
        if not self._project:
            self._logger.error("No hay proyecto abierto para importar bloque.")
            return False
        plc = self._get_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False
        return self._importer.import_single_block(
            self._project, plc, xml_file_path, target_relative_path
        )

    def sanitize_import_path(self, full_path: str) -> str:
        """Delega la sanitización de ruta al importer."""
        return self._importer.sanitize_import_path(full_path)

    def importar_bloque_override(
        self,
        plc_name: str,
        xml_path: str,
        target_folder: str | None = None
    ) -> bool:
        """
        Importa un bloque XML en TIA Portal preservando la estructura de carpetas.

        Args:
            plc_name: Nombre del PLC objetivo.
            xml_path: Ruta al archivo XML a importar.
            target_folder: Ruta del grupo original del bloque.

        Returns:
            True si la importación fue exitosa.
        """
        if not self._project:
            self._logger.error("No hay proyecto abierto para importar bloque.")
            return False
        plc = self._get_plc(plc_name)
        if not plc:
            self._logger.error(f"PLC '{plc_name}' no encontrado.")
            return False
        return self._importer.importar_bloque_override(
            self._project, plc, xml_path, target_folder
        )

    def actualizar_constantes_proceso(
        self,
        plc_name: str,
        nombre_tabla: str,
        constantes_dict: dict[str, Any]
    ) -> bool:
        """
        Modifica en vivo (RAM) los valores de las constantes de usuario en una tabla específica.
        La tabla debe estar ubicada en la carpeta '003_Proceso' de las variables del PLC.

        Args:
            plc_name: Nombre del PLC objetivo.
            nombre_tabla: Nombre de la tabla de variables (ej: '100_CPR').
            constantes_dict: Dict con pares nombre_constante -> nuevo_valor.

        Returns:
            True si se actualizaron una o más constantes.
        """
        plc = self._get_plc(plc_name)
        if not plc:
            return False

        try:
            self._logger.info(f"Buscando tabla de variables '{nombre_tabla}' en '003_Proceso'...")
            # 1. Obtener tablas de la carpeta directamente usando la API del wrapper
            tablas = plc.get_plc_tag_tables(folder_path="003_Proceso")

            # Buscar la tabla por nombre (asumiendo que tablas es una lista de objetos iterables del wrapper)
            tabla = next((t for t in tablas if t.get_property(name="Name") == nombre_tabla), None)

            if not tabla:
                self._logger.error(f"Tabla '{nombre_tabla}' no encontrada en '003_Proceso'.")
                return False

            # 2. Obtener constantes de usuario
            user_constants = tabla.get_user_constants()

            # 3. Iterar y actualizar usando set_property("Value", str)
            cambios = 0
            for constante in user_constants:
                # Usar keyword argument 'name='
                nombre_const = constante.get_property(name="Name")

                if nombre_const in constantes_dict:
                    nuevo_valor = str(constantes_dict[nombre_const])

                    # Usar keyword arguments 'name=' y 'value=' para el seteo
                    constante.set_property(name="Value", value=nuevo_valor)

                    self._logger.debug(f"Constante {nombre_const} actualizada a {nuevo_valor}")
                    cambios += 1

            if cambios > 0:
                self._logger.info(f"✅ {cambios} constantes actualizadas en '{nombre_tabla}'")
                return True
            else:
                self._logger.warning(f"No se modificó ninguna constante. Verifica los nombres en el diccionario.")
                return False

        except Exception as e:
            self._logger.error(f"Fallo actualizando constantes en {nombre_tabla}: {e}")
            return False

    @contextmanager
    def silenciar_ruido(self):
        """
        Administrador de contexto para silenciar el output nativo del wrapper de C#
        solo durante un bloque de código específico, restaurando la consola al salir.
        """
        log_path = str(Path(".build/tia_wrapper_native.log").absolute())
        Path(".build").mkdir(exist_ok=True)

        # 1. Al ENTRAR al bloque 'with': Desactivar consola
        ts.set_logging(path=log_path, console=False)
        try:
            yield  # Aquí se ejecuta el código que esté dentro del 'with'
        finally:
            # 2. Al SALIR del bloque 'with': Restaurar consola (incluso si hubo excepciones)
            ts.set_logging(path=log_path, console=True)
