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

    def _get_next_id_int(self) -> int:
        """
        Avanza el contador y devuelve el siguiente ID como entero.

        Usado por la estructura canonica de Siemens para PlcUserConstant
        + MultilingualText + MultilingualTextItem, cada uno con su
        propio ID unico en formato hexadecimal.

        Returns:
            Siguiente entero del contador (ya incrementado).
        """
        self._max_id += 1
        return self._max_id

    def _next_id(self) -> str:
        """Genera un nuevo ID en formato Siemens (hex mayuscula PURO)."""
        return f"{self._get_next_id_int():X}"

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

    def _ensure_object_list(self) -> ET.Element | None:
        """
        Busca un <ObjectList> en el XML o lo crea bajo el contenedor
        principal (PlcUserConstantTable / PlcTagTable) si la tabla
        exportada de TIA Portal está vacía y carece de él.

        Returns:
            El ObjectList encontrado/creado, o None si no hay contenedor
            válido (xml no es una PlcTagTable).
        """
        object_list = self.root.find(".//ObjectList")
        if object_list is not None:
            return object_list
        container = self.root.find(".//SW.Tags.PlcUserConstantTable")
        if container is None:
            container = self.root.find(".//SW.Tags.PlcTagTable")
        if container is not None:
            object_list = ET.SubElement(container, "ObjectList")
            self._logger.debug(
                "Nodo <ObjectList> creado dinamicamente para tabla vacia."
            )
            return object_list
        self._logger.error(
            f"No se encontro el contenedor principal en {self.xml_path}. "
            "Xml no es una PlcTagTable valida."
        )
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
        # Garantizar que existe <ObjectList>. Si la tabla exportada desde
        # TIA Portal estaba vacia, el nodo no existe y hay que crearlo
        # bajo el contenedor principal.
        #
        # CRITICO: TIA Portal es ESTRICTO con el orden de los nodos XML.
        # El orden canonico dentro de PlcTagTable / PlcUserConstantTable
        # es: <AttributeList> ... <ObjectList> ... <LinkList> ...
        # Si anadimos ObjectList con `append` (al final) rompemos la
        # validacion COM y TIA rechaza la importacion con error críptico.
        # Por eso, cuando creamos ObjectList dinamicamente, lo insertamos
        # en la posicion 1 (justo despues de AttributeList).
        if self._object_list is None:
            # 1. Buscar un ObjectList existente (agnostico a namespaces).
            object_list: ET.Element | None = None
            for node in self.root.iter():
                if node.tag.endswith("ObjectList"):
                    object_list = node
                    break

            if object_list is None:
                # 2. No existe. Buscar el contenedor principal
                # (PlcUserConstantTable o PlcTagTable) tambien agnóstico.
                container: ET.Element | None = None
                for node in self.root.iter():
                    if (
                        node.tag.endswith("PlcUserConstantTable")
                        or node.tag.endswith("PlcTagTable")
                    ):
                        container = node
                        break

                if container is not None:
                    # 3. Crear el ObjectList y posicionarlo correctamente:
                    #    - Si el container tiene un hijo AttributeList,
                    #      insertar DESPUES (posición 1) para mantener
                    #      el orden canónico.
                    #    - Si no, append al final.
                    object_list = ET.Element("ObjectList")
                    attribute_list_idx = None
                    for idx, child in enumerate(list(container)):
                        if child.tag.endswith("AttributeList"):
                            attribute_list_idx = idx
                            break
                    if attribute_list_idx is not None:
                        container.insert(attribute_list_idx + 1, object_list)
                        self._logger.debug(
                            "Nodo <ObjectList> creado e insertado "
                            "despues de <AttributeList> (orden canonico TIA)."
                        )
                    else:
                        container.append(object_list)
                        self._logger.debug(
                            "Nodo <ObjectList> creado e insertado al final "
                            "(container sin AttributeList hijo)."
                        )
                else:
                    self._logger.error(
                        f"No se encontro el contenedor principal "
                        f"(PlcUserConstantTable / PlcTagTable) en {self.xml_path}. "
                        "Xml no es una PlcTagTable valida."
                    )
                    return
            self._object_list = object_list
        assert self._object_list is not None

        # 1. Obtener ID para la constante
        const_id_int = self._get_next_id_int()
        const_id_hex = f"{const_id_int:X}"

        # Construimos el nodo raiz como Element (no SubElement) para tener
        # control explicito del orden de los hijos. Luego lo añadimos al
        # final con append() (que respeta la posicion canonica de ObjectList).
        constant_node = ET.Element(
            "SW.Tags.PlcUserConstant",
            {"ID": const_id_hex, "CompositionName": "UserConstants"},
        )
        attr_list = ET.SubElement(constant_node, "AttributeList")

        # Atributos basicos
        ET.SubElement(attr_list, "Name").text = name
        ET.SubElement(attr_list, "DataTypeName").text = "Int"
        ET.SubElement(attr_list, "Value").text = str(value)

        # 2. Inyectar el comentario con la estructura canonica de
        # MultilingualText + MultilingualTextItem, cada uno con su propio
        # ID unico en formato hexadecimal mayuscula.
        # Esta estructura anidada (no un <Comment> simple dentro de
        # AttributeList) es la que TIA Portal Openness EXIGE. Si no
        # se respeta, la importacion del XML falla con error silencioso
        # y la transaccion COM queda corrupta (rollback forzado).
        # Inicializamos los IDs fuera del `if` para que el log final
        # siempre pueda referenciarlos (evita UnboundLocalError en Pylance).
        mlt_id_hex: str = "N/A"
        mlti_id_hex: str = "N/A"
        if comment:
            mlt_id_int = self._get_next_id_int()
            mlti_id_int = self._get_next_id_int()
            mlt_id_hex = f"{mlt_id_int:X}"
            mlti_id_hex = f"{mlti_id_int:X}"

            obj_list = ET.SubElement(constant_node, "ObjectList")

            mlt = ET.SubElement(
                obj_list,
                "MultilingualText",
                {"ID": mlt_id_hex, "CompositionName": "Comment"},
            )
            mlt_obj_list = ET.SubElement(mlt, "ObjectList")

            mlti = ET.SubElement(
                mlt_obj_list,
                "MultilingualTextItem",
                {"ID": mlti_id_hex, "CompositionName": "Items"},
            )
            mlti_attr_list = ET.SubElement(mlti, "AttributeList")

            ET.SubElement(mlti_attr_list, "Culture").text = "es-ES"
            ET.SubElement(mlti_attr_list, "Text").text = comment

        self._object_list.append(constant_node)
        # DEBUG (no info) para evitar ruido: si hay 300 variables, este log
        # se imprimiría 300 veces en el log file. Solo nos interesa para
        # diagnosticar problemas de IDs duplicados o canonicos.
        self._logger.debug(
            f"Constante '{name}' (value={value}) con estructura canonica. "
            f"IDs: const={const_id_hex}, mlt={mlt_id_hex}, mlti={mlti_id_hex}."
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
