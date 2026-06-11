"""
Infrastructure Layer - Configuration Manager
============================================
Gestiona la persistencia de la configuración del usuario (JSON).
"""

import json
from dataclasses import dataclass
from pathlib import Path

CONFIG_FILE = Path("config.json")

# Valores por defecto de la configuración.
# El log se separa en file_level (verbosidad archivo) y console_level (verbosidad consola).
# Asi podemos tener DEBUG en el archivo (para diagnostico) y WARNING en consola
# (para no contaminar la TUI de Rich).
DEFAULT_CONFIG: dict[str, object] = {
    "template_path": "C:\\Plantilla",
    "log": {
        "file_level": "DEBUG",
        "console_level": "WARNING"
    },
    "tia_folders": {
        "proceso": "003_Proceso",
        "dispositivos_ed": "2000_Dispositivos"
    },
    "build_folders": {
        "root": ".build"
    },
    "hardware": {
        "ed": {
            "db_name": "DB2000_ED",
            "db_array_name": "ED",
            "tag_table": "2000_Disp_ED",
            "config_table": "000_Config_Dispositivos",
            "config_constant": "N_MAX_DISP_ED"
        },
        "ea": {
            "db_name": "DB2001_EA",
            "db_array_name": "EA",
            "tag_table": "2000_Disp_EA",
            "config_table": "000_Config_Dispositivos",
            "config_constant": "N_MAX_DISP_EA"
        },
        "sa": {
            "db_name": "DB2006_SA",
            "db_array_name": "SA",
            "tag_table": "2000_Disp_SA",
            "config_table": "000_Config_Dispositivos",
            "config_constant": "N_MAX_DISP_SA"
        },
        "v": {
            "db_name": "DB2010_V",
            "db_array_name": "V",
            "tag_table": "2000_Disp_V",
            "config_table": "000_Config_Dispositivos",
            "config_constant": "N_MAX_DISP_V"
        },
        "m": {
            "db_name": "DB2015_M",
            "db_array_name": "M",
            "tag_table": "2000_Disp_M",
            "config_table": "000_Config_Dispositivos",
            "config_constant": "N_MAX_DISP_M"
        },
        "m_vf": {
            "db_name": "DB2016_M_VF",
            "db_array_name": "M_VF",
            "tag_table": "2000_Disp_M_VF",
            "config_table": "000_Config_Dispositivos",
            "config_constant": "N_MAX_DISP_M_VF"
        }
    }
}


def _migrate_legacy_log_level(config: dict[str, object]) -> dict[str, object]:
    """
    Migracion retrocompatible: si el JSON antiguo tenia "log_level" simple
    (string), lo movemos a la nueva estructura "log": {...}.
    """
    legacy_level = config.pop("log_level", None)
    if legacy_level is not None and "log" not in config:
        # Mapeo de comportamiento historico:
        #   - "DEBUG"/"INFO" => file=DEBUG, console=WARNING (no inunda TUI)
        #   - "WARNING"/"ERROR"/"CRITICAL" => console=legacy, file=legacy
        lvl = str(legacy_level).upper()
        if lvl in ("DEBUG", "INFO"):
            config["log"] = {"file_level": "DEBUG", "console_level": "WARNING"}
        else:
            config["log"] = {"file_level": lvl, "console_level": lvl}
    return config


def _load_config() -> dict[str, object]:
    """Carga la configuración desde el archivo JSON."""
    if not CONFIG_FILE.exists():
        return {}
    try:
        raw: dict[str, object] = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {}
    return _migrate_legacy_log_level(raw)


def _save_config(config: dict[str, object]) -> None:
    """Guarda la configuración en el archivo JSON."""
    CONFIG_FILE.write_text(json.dumps(config, indent=4), encoding="utf-8")


def get_template_path() -> str | None:
    """Devuelve la ruta de plantillas configurada o None."""
    value = _load_config().get("template_path")
    return str(value) if value else None


def set_template_path(path: str) -> None:
    """Guarda la ruta de plantillas en el JSON."""
    config = _load_config()
    config["template_path"] = path
    _save_config(config)


# --- LOGGING CONFIG (nueva estructura desacoplada) ---

def _get_log_dict(config: dict[str, object]) -> dict[str, str]:
    """Extrae el dict 'log' del config, con defaults si falta alguna clave."""
    raw_log = config.get("log", {})
    if not isinstance(raw_log, dict):
        raw_log = {}
    defaults = DEFAULT_CONFIG["log"]  # type: ignore[arg-type]
    return {
        "file_level": str(raw_log.get("file_level", defaults["file_level"])),  # type: ignore[index]
        "console_level": str(raw_log.get("console_level", defaults["console_level"])),  # type: ignore[index]
    }


def get_log_file_level() -> str:
    """Devuelve el nivel de log para el archivo (default: DEBUG)."""
    return _get_log_dict(_load_config())["file_level"]


def get_log_console_level() -> str:
    """Devuelve el nivel de log para la consola (default: WARNING)."""
    return _get_log_dict(_load_config())["console_level"]


def set_log_levels(file_level: str, console_level: str) -> None:
    """
    Persiste ambos niveles de log en el JSON bajo la clave "log".

    Args:
        file_level: Nivel para el archivo (ej. "DEBUG").
        console_level: Nivel para la consola (ej. "WARNING").
    """
    config = _load_config()
    config["log"] = {
        "file_level": file_level.upper(),
        "console_level": console_level.upper(),
    }
    _save_config(config)


# --- RUTAS DE TIA PORTAL (carpetas virtuales del proyecto) ---


def get_tia_folder_proceso() -> str:
    """Ruta de la carpeta de tablas del proceso (N_MAX). Default: "003_Proceso"."""
    config: dict = _load_config()
    tia_folders: dict = config.get("tia_folders", {})
    return str(tia_folders.get("proceso", "003_Proceso"))


def get_tia_folder_dispositivos_ed() -> str:
    """Ruta de la carpeta de dispositivos ED. Default: "2000_Dispositivos"."""
    config: dict = _load_config()
    tia_folders: dict = config.get("tia_folders", {})
    return str(tia_folders.get("dispositivos_ed", "2000_Dispositivos"))


# --- DIRECTORIOS TEMPORALES DE BUILD ---


def get_build_root() -> str:
    """Raíz de los directorios temporales de build. Default: ".build"."""
    config: dict = _load_config()
    build_folders: dict = config.get("build_folders", {})
    return str(build_folders.get("root", ".build"))


# --- CONFIGURACIÓN TIA POR TIPO DE HARDWARE (escalable a EA, SD, etc.) ---


@dataclass(frozen=True)
class HardwareTIAConfig:
    """Rutas y constantes de TIA Portal para un tipo de hardware concreto."""
    db_name: str
    db_array_name: str
    tag_table: str
    config_table: str
    config_constant: str


def get_hardware_tia_config(hw_type: str) -> HardwareTIAConfig:
    """
    Buscador DINÁMICO de configuración de TIA Portal por tipo de hardware.
    Permite escalar a EA, SD, ANA, etc. sin tocar el codigo.

    Back-compat 100%: si el JSON no tiene la clave, devuelve los defaults
    historicos (los mismos que estaban hardcodeados en DispED.TIA_*).
    """
    hw_type = hw_type.lower()
    config: dict = _load_config()
    hardware_dict: dict = config.get("hardware", {})
    hw_data: dict = hardware_dict.get(hw_type, {})

    if hw_type == "ed":
        return HardwareTIAConfig(
            db_name=hw_data.get("db_name", "DB2000_ED"),
            db_array_name=hw_data.get("db_array_name", "ED"),
            tag_table=hw_data.get("tag_table", "2000_Disp_ED"),
            config_table=hw_data.get("config_table", "000_Config_Dispositivos"),
            config_constant=hw_data.get("config_constant", "N_MAX_DISP_ED"),
        )

    # Para futuros tipos (ea, sd, etc.) si no están en config:
    return HardwareTIAConfig(
        db_name=hw_data.get("db_name", f"DB_{hw_type.upper()}"),
        db_array_name=hw_data.get("db_array_name", hw_type.upper()),
        tag_table=hw_data.get("tag_table", f"TagTable_{hw_type.upper()}"),
        config_table=hw_data.get("config_table", "000_Config_Dispositivos"),
        config_constant=hw_data.get("config_constant", f"N_MAX_{hw_type.upper()}"),
    )
