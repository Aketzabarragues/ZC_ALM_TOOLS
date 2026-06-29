"""
Infrastructure Layer - Tabla de Variables Injector
==================================================
Editor QUIRURGICO de la Tabla de Variables PLC para inyectar los valores
N_MAX antes de importarla a TIA Portal.

POR QUE EXISTE:
  TIA Portal Openness tiene un bug de "Histéresis de Compilación": si las
  constantes N_MAX cambian DESPUES de importar la tabla, la compilación
  no recalcula las dimensiones de los DBs. Solución: inyectar los valores
  N_MAX del Excel directamente en el archivo XML fisico ANTES de importar.

ESTRATEGIA (regex pura, consistente con ProcesoXMLModifier):
  - Lee el archivo como texto plano UTF-8.
  - Para cada constante esperada (N_MAX_PREAL, N_MAX_PINT, N_MAX_ALARMAS,
    N_MAX_ALARMAS_HMI), busca el patron:
        <Member Name="<UID>_N_MAX_XXX">
          ...
          <StartValue>0</StartValue>
          ...
        </Member>
  - Sustituye SOLO el inner text de <StartValue> por el valor del Excel.
  - NO toca <Name>, <ID>, <Handle>, <SystemId>, etc.

Si el archivo no contiene la constante, log warning (algunos proyectos
no tienen todas las N_MAX).
"""
import logging
import re
from pathlib import Path


class TablaVariablesInjector:
    """
    Inyecta los valores N_MAX del Excel en el archivo XML de la Tabla
    de Variables generado en .build/, ANTES de importarlo a TIA Portal.

    Uso:
        TablaVariablesInjector.inyectar_en_build(
            ruta_build=".build",
            constantes={
                "1620_N_MAX_PREAL": 25,
                "1620_N_MAX_PINT": 10,
                "1620_N_MAX_ALARMAS": 32,
                "1620_N_MAX_ALARMAS_HMI": 1,
            },
        )
    """

    _logger: logging.Logger = logging.getLogger(f"{__name__}.{__qualname__}")

    @classmethod
    def inyectar_en_build(
        cls,
        ruta_build: str,
        constantes: dict[str, int],
    ) -> bool:
        """
        Busca el archivo de Tabla de Variables en .build/ y le inyecta
        los StartValue de las constantes indicadas.

        Args:
            ruta_build: ruta al directorio .build/.
            constantes: dict {nombre_constante: valor} (ej. N_MAX_PREAL -> 25).

        Returns:
            True si al menos una constante fue modificada.
            False si no se encontro ninguna (warning loggeado).
        """
        build_dir = Path(ruta_build)
        if not build_dir.exists():
            cls._logger.error(f"Ruta build no existe: {ruta_build}")
            return False

        # Buscar el archivo XML de tabla de variables. La estructura
        # tipica es: .build/Tablas/<UID>_<CODIGO>.xml, pero por
        # robustez hacemos rglob.
        candidatos = list(build_dir.rglob("*.xml"))
        # Filtrar los que tengan la pinta de tabla de variables (tag table)
        candidatos = [
            p for p in candidatos
            if "PlcTagTable" in p.read_text(encoding="utf-8", errors="ignore")
        ]

        if not candidatos:
            cls._logger.warning(
                f"No se encontro archivo de Tabla de Variables en {ruta_build}."
            )
            return False

        # Tomar el mas reciente (por si hay varios)
        candidatos.sort(key=lambda f: f.stat().st_mtime, reverse=True)
        tabla_path = candidatos[0]
        cls._logger.info(
            f"Inyectando N_MAX en tabla de variables: {tabla_path.name}"
        )

        contenido = tabla_path.read_text(encoding="utf-8")
        cambios = 0

        for nombre_const, valor in constantes.items():
            nuevo_contenido, n = cls._reemplazar_start_value(
                contenido, nombre_const, int(valor)
            )
            if n > 0:
                contenido = nuevo_contenido
                cambios += n
                cls._logger.debug(
                    f"  - {nombre_const} = {valor} (sustituido {n} vez/veces)"
                )
            else:
                cls._logger.warning(
                    f"  - {nombre_const} no encontrada en la tabla (se omite)"
                )

        if cambios > 0:
            tabla_path.write_text(contenido, encoding="utf-8")
            cls._logger.info(
                f"✅ {cambios} StartValue(s) actualizados en {tabla_path.name}."
            )
            return True

        cls._logger.warning("Ninguna constante fue modificada.")
        return False

    @staticmethod
    def _reemplazar_start_value(
        contenido: str, nombre_constante: str, nuevo_valor: int
    ) -> tuple[str, int]:
        """
        Busca la constante `nombre_constante` y reemplaza su <StartValue>
        por `nuevo_valor`.

        ESTRUCTURA REAL del XML de TIA (PlcTagTable):
          <SW.Tags.PlcUserConstant ID="1">
            <AttributeList>
              <Name>1620_N_MAX_PREAL</Name>      <- match por este nombre
              <DataTypeName>Int</DataTypeName>
              <Value>0</Value>                   <- inner text a sustituir
            </AttributeList>
          </SW.Tags.PlcUserConstant>

        ESTRATEGIA (regex con DOTALL):
          - Buscamos <Name>nombre_constante</Name> y el <Value> hermano
            (no usamos el lookhead de </AttributeList> aqui porque en la
            Tag Table <Name> y <Value> son hermanos directos y
            consecutivos en la mayoria de los casos).
          - Patron sugerido por el usuario:
              rf'(<Name>{constante}</Name>\\s*<Value>)([^<]*)(</Value>)'
          - Usamos \\g<1> y \\g<3> en re.sub para preservar exactamente
            los grupos capturados sin riesgo de corrupcion.
        """
        # Patron: <Name>CONST</Name> ... <Value>X</Value>
        # Entre <Name> y <Value> puede haber otras etiquetas hermanas
        # (ej. <DataTypeName>Int</DataTypeName>), asi que usamos DOTALL
        # y un patron que captura todo el contenido hasta el primer
        # <Value> siguiente. NO usamos limite por </AttributeList> para
        # no romper con espacios/saltos de linea adicionales.
        patron = rf'(<Name>{re.escape(nombre_constante)}</Name>.*?<Value>)([^<]*)(</Value>)'

        def mutar(m: re.Match) -> str:
            # \g<1> = apertura completa hasta <Value>
            # \g<2> = valor antiguo (descartado)
            # \g<3> = </Value>
            return f"{m.group(1)}{nuevo_valor}{m.group(3)}"

        nuevo_contenido, n = re.subn(
            patron, mutar, contenido,
            count=1,  # solo el primer match
            flags=re.DOTALL | re.IGNORECASE,
        )
        return nuevo_contenido, n
