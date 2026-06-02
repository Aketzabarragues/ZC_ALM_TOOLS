"""
Infrastructure Layer - XML Modifier
===================================
Edita bloques de datos (DB) XML de TIA Portal para inyectar comentarios.
Implementación basada en minidom para evitar la contaminación de namespaces (Namespace Pollution).
"""

import logging
from pathlib import Path
from xml.dom import minidom


class XMLModifier:
    """Editor de DBs XML para inyectar comentarios de variables usando minidom."""

    def __init__(self, xml_path: str) -> None:
        self.xml_path = Path(xml_path)
        self._logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # minidom preserva la estructura exacta sin ensuciar el namespace del Root
        self.doc = minidom.parse(str(self.xml_path))

    def _find_member(self, name: str):
        """Busca un elemento Member por nombre."""
        for node in self.doc.getElementsByTagName("Member"):
            if node.getAttribute("Name") == name:
                return node
        return None

    def set_comment(
        self,
        array_name: str,
        index: int | str,
        comment: str,
        es_parametro: bool = False
    ) -> bool:
        """Establece el comentario de una variable en el DB XML."""
        is_modified = False

        member = self._find_member(array_name)
        if member and self._update_or_add_comment(member, index, comment):
            is_modified = True

        if es_parametro:
            for extra_array in ["ValorAnterior", "Vis"]:
                extra_member = self._find_member(extra_array)
                if extra_member and self._update_or_add_comment(extra_member, index, comment):
                    is_modified = True

        return is_modified

    def _update_or_add_comment(self, member, index: int | str, text: str) -> bool:
        """Actualiza o añade un comentario a un Subelement."""
        if not text:
            return False

        # 1. Buscar Subelement (usamos localName para ignorar namespaces en la búsqueda)
        sub_element = None
        for child in member.childNodes:
            if child.nodeType == child.ELEMENT_NODE and child.localName == "Subelement" and child.getAttribute("Path") == str(index):
                sub_element = child
                break

        if not sub_element:
            sub_element = self.doc.createElement("Subelement")
            sub_element.setAttribute("Path", str(index))
            member.appendChild(sub_element)

        # 2. Buscar Comment
        comment_node = None
        for child in sub_element.childNodes:
            if child.nodeType == child.ELEMENT_NODE and child.localName == "Comment":
                comment_node = child
                break

        if not comment_node:
            comment_node = self.doc.createElement("Comment")
            if sub_element.firstChild:
                sub_element.insertBefore(comment_node, sub_element.firstChild)
            else:
                sub_element.appendChild(comment_node)

        # 3. Buscar MultiLanguageText
        ml_node = None
        for child in comment_node.childNodes:
            if child.nodeType == child.ELEMENT_NODE and child.localName == "MultiLanguageText" and child.getAttribute("Lang") == "es-ES":
                ml_node = child
                break

        if ml_node:
            # Actualizar texto existente
            if ml_node.firstChild and ml_node.firstChild.nodeType == ml_node.TEXT_NODE:
                if ml_node.firstChild.nodeValue != text:
                    ml_node.firstChild.nodeValue = text # type: ignore
                    return True
            else:
                ml_node.appendChild(self.doc.createTextNode(text))
                return True
        else:
            # Crear nuevo nodo de texto
            new_ml = self.doc.createElement("MultiLanguageText")
            new_ml.setAttribute("Lang", "es-ES")
            new_ml.appendChild(self.doc.createTextNode(text))
            comment_node.appendChild(new_ml)
            return True

        return False

    def save(self) -> None:
        """Guarda los cambios al archivo XML usando encoding UTF-8 binario."""
        with open(self.xml_path, "wb") as f:
            f.write(self.doc.toxml(encoding="utf-8"))
