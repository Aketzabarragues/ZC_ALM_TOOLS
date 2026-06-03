"""
Infrastructure Layer - UI Dialogs
=================================
Componentes de interfaz de usuario nativa (Tkinter aislado).
"""

import tkinter as tk
from tkinter import filedialog


def seleccionar_excel() -> str | None:
    """Abre un diálogo nativo para seleccionar el Excel Maestro."""
    root = tk.Tk()
    root.withdraw()
    root.attributes('-topmost', True)
    ruta = filedialog.askopenfilename(
        title="Selecciona el Excel Maestro de Configuración",
        filetypes=[("Archivos Excel", "*.xlsx *.xlsm"), ("Todos los archivos", "*.*")]
    )
    root.destroy()
    return ruta if ruta else None


def seleccionar_carpeta(titulo: str = "Selecciona la carpeta de Plantillas XML") -> str | None:
    """Abre un diálogo nativo para seleccionar la carpeta de plantillas XML."""
    root = tk.Tk()
    root.withdraw()
    root.attributes('-topmost', True)
    ruta = filedialog.askdirectory(title=titulo)
    root.destroy()
    return ruta if ruta else None


def seleccionar_proyecto_tia() -> str | None:
    """Abre un diálogo nativo para seleccionar el archivo de proyecto de TIA Portal."""
    root = tk.Tk()
    root.withdraw()
    root.attributes('-topmost', True)
    ruta = filedialog.askopenfilename(
        title="Selecciona el proyecto de TIA Portal",
        filetypes=[
            ("TIA Portal Projects", "*.ap15 *.ap15_1 *.ap16 *.ap17 *.ap18 *.ap19 *.ap20 *.ap21"),
            ("Todos los archivos", "*.*"),
        ],
    )
    root.destroy()
    return ruta if ruta else None
