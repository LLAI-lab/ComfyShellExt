"""Probe which thumbnail provider Windows resolves for a set of extensions."""
import ctypes
from ctypes import wintypes
import winreg

IID_THUMB = "{e357fccd-a995-4576-b01f-234630154e96}"
ASSOCSTR_SHELLEXTENSION = 16
ASSOCF_NOTRUNCATE = 0x00000020

shlwapi = ctypes.windll.shlwapi
shlwapi.AssocQueryStringW.argtypes = [
    wintypes.DWORD, ctypes.c_int, wintypes.LPCWSTR, wintypes.LPCWSTR,
    wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]


def assoc_shellex(ext):
    n = wintypes.DWORD(1024)
    buf = ctypes.create_unicode_buffer(1024)
    hr = shlwapi.AssocQueryStringW(ASSOCF_NOTRUNCATE, ASSOCSTR_SHELLEXTENSION,
                                   ext, IID_THUMB, buf, ctypes.byref(n))
    return buf.value if hr == 0 else "HRESULT 0x%08X" % (hr & 0xFFFFFFFF)


def rd(root, path, value=""):
    try:
        with winreg.OpenKey(root, path) as k:
            return winreg.QueryValueEx(k, value)[0]
    except OSError:
        return None


def clsid_name(clsid):
    if not clsid or not clsid.startswith("{"):
        return ""
    n = rd(winreg.HKEY_CLASSES_ROOT, r"CLSID\%s" % clsid) or ""
    dll = rd(winreg.HKEY_CLASSES_ROOT, r"CLSID\%s\InprocServer32" % clsid) or ""
    return "%s | %s" % (n, dll)


EXTS = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif",
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".flac", ".mp3"]

for ext in EXTS:
    progid = rd(winreg.HKEY_CLASSES_ROOT, ext) or ""
    uc = rd(winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\%s\UserChoice" % ext,
            "ProgId") or ""
    pt = rd(winreg.HKEY_CLASSES_ROOT, ext, "PerceivedType") or ""
    resolved = assoc_shellex(ext)
    print("=== %s   progid=%r userchoice=%r perceived=%r" % (ext, progid, uc, pt))
    print("    AssocQueryString -> %s   %s" % (resolved, clsid_name(resolved)))
    cands = []
    if uc:
        cands.append(r"%s\ShellEx\%s" % (uc, IID_THUMB))
    if progid and progid != uc:
        cands.append(r"%s\ShellEx\%s" % (progid, IID_THUMB))
    cands.append(r"SystemFileAssociations\%s\ShellEx\%s" % (ext, IID_THUMB))
    cands.append(r"%s\ShellEx\%s" % (ext, IID_THUMB))
    if pt:
        cands.append(r"SystemFileAssociations\%s\ShellEx\%s" % (pt, IID_THUMB))
    cands.append(r"*\ShellEx\%s" % IID_THUMB)
    for c in cands:
        v = rd(winreg.HKEY_CLASSES_ROOT, c)
        if v is not None:
            print("      HKCR\\%s = %s   %s" % (c, v, clsid_name(v)))
