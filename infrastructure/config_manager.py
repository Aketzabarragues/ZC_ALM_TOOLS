"""
Infrastructure Layer - Configuration Manager
============================================
Gestiona la persistencia de la configuración del usuario (JSON).
"""

import json
from pathlib import Path

CONFIG_FILE = Path("config.json")

# Valores por defecto de la configuración.
# El log se separa en file_level (verbosidad archivo) y console_level (verbosidad consola).
# Asi podemos tener DEBUG en el archivo (para diagnostico) y WARNING en consola
# (para no contaminar la TUI de Rich).
DEFAULT_CONFIG: dict[str, object] = {
    "template_path": None,
    "log": {
        "file_level": "DEBUG",
        "console_level": "WARNING",
    },
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
