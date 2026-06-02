import siemens_tia_scripting as ts
import inspect

def dump_api():
    with open("tia_api_real.txt", "w") as f:
        f.write("=== API REAL DE SIEMENS TIA SCRIPTING ===\n\n")
        
        for name, obj in inspect.getmembers(ts):
            # Si es una clase, listamos sus métodos
            if inspect.isclass(obj):
                f.write(f"Clase: {name}\n")
                try:
                    doc = inspect.getdoc(obj)
                    if doc:
                        f.write(f"  Doc: {doc}\n")
                except Exception:
                    pass
                
                # Extraer atributos/métodos de la clase
                for attr_name, attr_obj in inspect.getmembers(obj):
                    # Filtramos los dunder methods (__init__, __str__, etc.) para no meter ruido
                    if not attr_name.startswith("__"):
                        f.write(f"  -> Método/Atributo: {attr_name}\n")
                
                f.write("-" * 60 + "\n")
                
            # Si es una función global (como attach_portal)
            elif inspect.isroutine(obj) and not name.startswith("__"):
                f.write(f"Función Global: {name}\n")
                try:
                    doc = inspect.getdoc(obj)
                    if doc:
                        f.write(f"  Doc: {doc}\n")
                except Exception:
                    pass
                f.write("-" * 60 + "\n")

if __name__ == "__main__":
    dump_api()