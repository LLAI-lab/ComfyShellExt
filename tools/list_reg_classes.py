"""Lists the COM classes in a regasm generated .reg file."""
import re
import sys
import pathlib

text = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace")
pattern = re.compile(r"HKEY_CLASSES_ROOT\\CLSID\\(\{[0-9A-Fa-f-]+\})\]\s*\n@=\"([^\"]+)\"")
for clsid, name in pattern.findall(text):
    print("%s  %s" % (clsid, name))
print("InprocServer32 entries:", text.count("InprocServer32]"))
print("ThreadingModel entries:", text.count("ThreadingModel"))
