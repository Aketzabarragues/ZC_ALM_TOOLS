"""
Infrastructure Layer - XML Tag Table Modifier
==============================================
Modifica tablas de tags PLC (UserConstants) en archivos XML
exportados desde TIA Portal.

A diferencia de XMLModifier (que ataca DBs con minidom), este
modificador usa xml.etree.ElementTree porque el formato de las
PlcTagTable es mas rigido y queremos preservar el orden de los
atributos y el namespace.

Convencion de IDs de Siemens: `ID="1"`, `ID="A"`, `ID="1C"`,
hexadecimal mayuscula PURO, SIN prefijo 0x ni padding a 8 chars.
"""

import logging
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import cast

__all__ = ["TagTableModifier"]


# NOTA sobre namespaces:
# TIA Portal v1.2.1 exporta las PlcTagTable SIN prefijo de namespace
# (los tags aparecen como "SW.Tags.PlcUserConstant", "ObjectList", etc.
# directamente bajo la raiz). Anadir un namespace rompe la importacion
# de vuelta, asi que trabajamos con strings planos.


class TagTableModifier:
    """Editor offline de XML de PlcTagTable (UserConstants)."""

    def __init__(self, xml_path: str) -> None:
        self.xml_path = Path(xml_path)
        self._logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # minidom NO es ideal aqui porque reordena atributos y rompe
        # el formato Siemens. ET es mas predecible.
        self.tree = cast(ET.ElementTree, ET.parse(str(self.xml_path)))
        # Un Xml de TIA siempre tiene root; assert para satisfacer a Pylance.
        self.root = cast(ET.Element, self.tree.getroot())
        assert self.root is not None
        self._max_id: int = self._compute_max_id()
        self._object_list: ET.Element | None = self._find_object_list()

    # ------------------------------------------------------------------ #
    #  Helpers internos
    # ------------------------------------------------------------------ #

    def _local(self, tag: str) -> str:
        """Devuelve el nombre local de un tag (sin namespace)."""
        return tag.split("}", 1)[-1] if "}" in tag else tag

    def _compute_max_id(self) -> int:
        """
        Recorre todos los atributos ID= del XML y devuelve el maximo
        entero (parseado desde hex). Convierte a uppercase antes de
        parsear por si viene en minusculas.
        """
        max_id = -1
        for elem in self.root.iter():
            for attr, value in elem.attrib.items():
                if self._local(attr) == "ID" and value:
                    try:
                        max_id = max(max_id, int(value, 16))
                    except ValueError:
                        # Si no es hex valido, lo ignoramos silenciosamente
                        self._logger.debug(
                            f"Atributo ID con valor no hex ignorado: {value!r}"
                        )
        return max_id

    def _next_id(self) -> str:
        """Genera un nuevo ID en formato Siemens (hex mayuscula PURO)."""
        self._max_id += 1
        return f"{self._max_id:X}"

    def _find_object_list(self) -> ET.Element | None:
        """
        Busca el <ObjectList> hijo directo de <SW.Tags.PlcTagTable>.
        Retorna None si no se encuentra.
        """
        for child in self.root:
            if self._local(child.tag) == "ObjectList":
                return child
        # Fallback: buscar el primer ObjectList del documento
        for elem in self.root.iter():
            if self._local(elem.tag) == "ObjectList":
                return elem
        return None

    # ------------------------------------------------------------------ #
    #  API publica
    # ------------------------------------------------------------------ #

    def add_user_constant(self, name: str, value: int, comment: str) -> None:
        """
        Anade un nuevo SW.Tags.PlcUserConstant al ObjectList.

        Estructura objetivo (basada en Xmls reales de TIA Portal):
            <ObjectList>
              <SW.Tags.PlcUserConstant ID="1D" CompositionName="UserConstants">
                <AttributeList>
                  <Name>...</Name>
                  <DataTypeName>Int</DataTypeName>
                  <Value>5</Value>
                  <Comment>
                    <MultiLanguageText Lang="es-ES">...</MultiLanguageText>
                  </Comment>
                </AttributeList>
                <ObjectList>
                  <SW.Tags.PlcUserConstant ID="1E" CompositionName="Instances">
                    <AttributeList>
                      <Name>...</Name>
                    </AttributeList>
                  </SW.Tags.PlcUserConstant>
                </ObjectList>
                <LinkList />
              </SW.Tags.PlcUserConstant>
            </ObjectList>
        """
        if self._object_list is None:
            self._logger.error(
                f"No se encontro <ObjectList> en {self.xml_path}. "
                "Xml no es una PlcTagTable valida."
            )
            return

        # ID nuevo (hex mayuscula PURO) para la constante
        const_id = self._next_id()

        # ---------- Bloque principal ----------
        uc_elem = ET.SubElement(
            self._object_list,
            "SW.Tags.PlcUserConstant",
        )
        uc_elem.set("ID", const_id)
        uc_elem.set("CompositionName", "UserConstants")

        attr_list = ET.SubElement(uc_elem, "AttributeList")
        ET.SubElement(attr_list, "Name").text = name
        ET.SubElement(attr_list, "DataTypeName").text = "Int"
        ET.SubElement(attr_list, "Value").text = str(value)

        # Comment / MultiLanguageText (solo si hay comentario)
        if comment:
            comment_elem = ET.SubElement(attr_list, "Comment")
            mlt = ET.SubElement(comment_elem, "MultiLanguageText")
            mlt.set("Lang", "es-ES")
            mlt.text = comment

        self._logger.info(
            f"Constante '{name}' (value={value}) preparada para insercion con ID={const_id}."
        )

    def save(self) -> None:
        """Persiste el arbol modificado en disco."""
        # method="xml" fuerza el serializador XML 1.0 (no el simple
        # html-style). xml_declaration=True incluye <?xml ... ?>.
        self.tree.write(
            str(self.xml_path),
            xml_declaration=True,
            encoding="utf-8",
            method="xml",
        )
        self._logger.info(f"Tag table guardada en: {self.xml_path}")
