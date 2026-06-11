"""
Infrastructure Layer - Proceso XML Modifier
==========================================
Editor QUIRURGICO de bloques de proceso (DB/FC/FB/OB) para inyeccion en TIA Portal.

POR QUE NO USAMOS xml.etree.ElementTree NI minidom:
  - ET inyecta prefijos sinteticos `ns_xxx:` al guardar (ensucia los namespaces).
  - minidom reordena los atributos de los elementos (altera la firma del XML).
  - TIA Portal Openness rechaza ambos formatos con
    "Cannot create the object..." al importar.

SOLUCION: BISTURI CON DOBLE PASADA.

  FASE 1 - FUERZA BRUTA SEGURA (str.replace global sobre TODO el archivo):
    Las cadenas exactas del diccionario (nombres completos de bloques,
    codigos de proceso, constantes N_MAX, etc.) son 100% seguras para
    reemplazar de forma global. NO pueden colisionar con un <ID> interno
    de Siemens porque los <ID> son puramente numericos (ej. ID="51400") y
    los nombres completos son alfanumericos largos (DB51400_PREP_COCINA2).
    Esta fase captura:
      - Codigo SCL interno (Statements, Text, etc.)
      - Datatypes: <Attribute Name="Datatype">Array[1.."50100_N_MAX_PREAL"]</Attribute>
      - Nombres de arrays, parametros, variables embebidas en codigo
      - Cualquier otra referencia textual basada en nombres del diccionario.

  FASE 2 - CIRUGIA MATEMATICA (re.sub restringido a la whitelist):
    La suma del delta sobre `5\\d{4}` (numeros 5xxxx sueltos) sigue siendo
    PELIGROSA en zonas no whitelisted (puede haber offsets o parametros
    que no son numeros de bloque). Por eso se restringe a:
      - <Name>...</Name>
      - <Number>...</Number>
      - <Comment>...</Comment>
      - <Title>...</Title>
      - <Attribute Name="Name">...</Attribute>
      - <Attribute Name="Number">...</Attribute>
      - <Attribute Name="Comment">...</Attribute>
      - <Attribute Name="Title">...</Attribute>
    Asi NO tocamos:
      - <ID>          (ID interno de Siemens)
      - <Handle>      (handle del objeto COM)
      - <SystemId>    (UUID interno)
      - <RefId>       (cross-references)
      - <Link>        (enlaces a otros objetos)
      - Atributos prohibidos en general

  Los numeros 5\\d{4} que quedan sueltos (no capturados por Fase 1) son
  tipicamente numeros de bloque en <Number> o sub-cadenas en <Name>,
  donde SI deben mutarse. Por eso Fase 2 los procesa.
"""
import logging
import re
from pathlib import Path


class ProcesoXMLModifier:
    """
    Editor quirurgico de XMLs de bloques de proceso con doble pasada.

    Uso:
        modifier = ProcesoXMLModifier("ruta/al/bloque.xml")
        if modifier.modificar(
            reemplazos={"DB51400_PREP_COCINA2": "DB1620_PREP_COC2_1"},
            delta=-49780,
        ):
            modifier.save()

    CRITICO: el modifier MUTA el archivo. Hacer copia previa si necesitas
    el original (ej. en .build/_original/).
    """

    def __init__(self, xml_path: str) -> None:
        self.xml_path = Path(xml_path)
        self._logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # Lectura como texto plano UTF-8 (sin parser XML para no ensuciar
        # los namespaces).
        with open(self.xml_path, "r", encoding="utf-8") as f:
            self.xml_content = f.read()

    def modificar(
        self,
        reemplazos: dict[str, str],
        delta: int = 0,
    ) -> bool:
        """
        Doble pasada sobre el contenido XML:
          1. FUERZA BRUTA: str.replace del diccionario sobre TODO el archivo.
             Captura codigo SCL, Datatypes, nombres de arrays, etc.
          2. CIRUGIA: re.sub del delta sobre 5\\d{4} SOLO en la whitelist
             (etiquetas Name, Number, Comment, Title + sus variantes Attribute).
             Evita tocar <ID>, <Handle>, <SystemId>, <RefId>, <Link>.

        Args:
            reemplazos: dict[str, str] con los reemplazos literales
                (mismo formato que `calcular_diccionario_reemplazos`).
            delta: desplazamiento numerico a aplicar a cualquier `5\\d{4}`
                encontrado dentro de las etiquetas seguras.

        Returns:
            True si se realizo algun cambio, False en caso contrario.
        """
        original_content = self.xml_content

        # ------------------------------------------------------------------ #
        #  FASE 1: FUERZA BRUTA SEGURA
        #  str.replace del diccionario sobre TODO el archivo.
        #  Ordenamos por longitud descendente para evitar colisiones
        #  tipo "DB5" antes que "DB51400_PREP_COCINA2".
        # ------------------------------------------------------------------ #
        reemplazos_validos = sorted(
            ((k, v) for k, v in reemplazos.items() if not k.startswith("__")),
            key=lambda kv: -len(kv[0])
        )
        matches_fase1 = 0
        for old, new in reemplazos_validos:
            before = self.xml_content
            self.xml_content = self.xml_content.replace(old, new)
            if self.xml_content != before:
                matches_fase1 += 1

        # ------------------------------------------------------------------ #
        #  FASE 2: CIRUGIA MATEMATICA (delta en whitelist)
        #  Solo si hay delta != 0, aplicamos re.sub sobre 5\\d{4} SUELTOS
        #  dentro de las etiquetas seguras. Las que ya fueron capturadas
        #  por Fase 1 ya no matchearan el regex.
        # ------------------------------------------------------------------ #
        matches_fase2 = 0
        if delta != 0:
            def sumar_delta(m: re.Match) -> str:
                return str(int(m.group(0)) + delta)

            def mutar_numeros_inner(match: re.Match) -> str:
                """Callback: solo muta numeros en el inner text."""
                opening_tag = match.group(1)
                inner_text = match.group(2)
                closing_tag = match.group(3)
                # Solo sumamos el delta (Fase 1 ya hizo los literales)
                inner_text = re.sub(r"5\d{4}", sumar_delta, inner_text)
                return opening_tag + inner_text + closing_tag

            # Procesar PRIMERO <Attribute Name="..."> (mas especifico) para
            # evitar solapamientos con las etiquetas simples.
            for tag in ["Name", "Number", "Comment", "Title"]:
                patron = rf'(<Attribute\b[^>]*Name="{tag}"[^>]*>)(.*?)(</Attribute>)'
                nuevo_contenido, n = re.subn(
                    patron, mutar_numeros_inner, self.xml_content,
                    flags=re.DOTALL | re.IGNORECASE,
                )
                self.xml_content = nuevo_contenido
                matches_fase2 += n

            # Procesar DESPUES las etiquetas simples con word boundary \\b
            # (evita falsos positivos con <Names>, <NameID>, etc.).
            for tag in ["Name", "Number", "Comment", "Title"]:
                patron = rf"(<{tag}\b[^>]*>)(.*?)(</{tag}>)"
                nuevo_contenido, n = re.subn(
                    patron, mutar_numeros_inner, self.xml_content,
                    flags=re.DOTALL | re.IGNORECASE,
                )
                self.xml_content = nuevo_contenido
                matches_fase2 += n

        # ------------------------------------------------------------------ #
        #  FASE 3: LIMPIEZA DE ETIQUETAS READ-ONLY (InstanceDB fix)
        #  TIA Portal Openness rechaza la importacion de InstanceDBs si
        #  contienen las etiquetas HeaderAuthor, HeaderFamily, HeaderVersion
        #  o HeaderName porque son de solo lectura (las gestiona TIA).
        #  Las eliminamos EXCLUSIVAMENTE si el archivo es un InstanceDB.
        #
        #  La regex \\s*<{tag}>[^<]*</{tag}> consume los espacios y saltos
        #  de linea previos para no dejar lineas vacias en el XML
        #  resultante. [^<]* es seguro porque las cabeceras no contienen <.
        # ------------------------------------------------------------------ #
        matches_fase3 = 0
        if "<SW.Blocks.InstanceDB" in self.xml_content:
            etiquetas_problematicas = [
                "HeaderAuthor", "HeaderFamily", "HeaderVersion", "HeaderName",
            ]
            for tag in etiquetas_problematicas:
                self.xml_content, n = re.subn(
                    rf'\s*<{tag}>[^<]*</{tag}>',
                    '',
                    self.xml_content,
                    flags=re.IGNORECASE,
                )
                matches_fase3 += n

        modificado = self.xml_content != original_content
        if modificado:
            self._logger.debug(
                f"ProcesoXMLModifier (doble pasada + fase 3): XML mutado. "
                f"Fase1 (literal global)={matches_fase1} matches, "
                f"Fase2 (delta whitelist)={matches_fase2} matches, "
                f"Fase3 (InstanceDB read-only)={matches_fase3} matches, "
                f"delta={delta}."
            )
        return modificado

    def save(self) -> None:
        """Guarda el contenido modificado en el mismo archivo (UTF-8)."""
        with open(self.xml_path, "w", encoding="utf-8") as f:
            f.write(self.xml_content)
        self._logger.debug(f"XML guardado (Doble Pasada): {self.xml_path}")
