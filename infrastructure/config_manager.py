"""
Infrastructure Layer - Configuration Manager
============================================
Gestiona la persistencia de la configuración del usuario (JSON).
"""

import json
from pathlib import Path

CONFIG_FILE = Path("config.json")


def _load_config() -> dict[str, str]:
    """Carga la configuración desde el archivo JSON."""
    if CONFIG_FILE.exists():
        try:
            return json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            return {}
    return {}


def _save_config(config: dict[str, str]) -> None:
    """Guarda la configuración en el archivo JSON."""
    CONFIG_FILE.write_text(json.dumps(config, indent=4), encoding="utf-8")


def get_template_path() -> str | None:
    """Devuelve la ruta de plantillas configurada o None."""
    return _load_config().get("template_path")


def set_template_path(path: str) -> None:
    """Guarda la ruta de plantillas en el JSON."""
    config = _load_config()
    config["template_path"] = path
    _save_config(config)