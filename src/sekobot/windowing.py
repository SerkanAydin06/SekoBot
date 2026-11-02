"""Win32 window discovery and DPI-correct client area coordinates."""

from __future__ import annotations

import ctypes
import os
from ctypes import wintypes
from dataclasses import dataclass


class WindowUnavailable(RuntimeError):
    """The selected window cannot currently be captured."""


@dataclass(frozen=True, slots=True)
class WindowInfo:
    hwnd: int
    title: str
    process_id: int
    width: int
    height: int

    @property
    def label(self) -> str:
        return f"{self.title}  •  PID {self.process_id}  •  {self.width}×{self.height}"


@dataclass(frozen=True, slots=True)
class Region:
    left: int
    top: int
    width: int
    height: int

    def as_dict(self) -> dict[str, int]:
        return {"left": self.left, "top": self.top, "width": self.width, "height": self.height}


def is_metin2_title(title: str) -> bool:
    return "metin2" in "".join(title.casefold().split())


def enable_dpi_awareness() -> None:
    """Use physical Win32 pixels, not DPI-virtualized coordinates."""
    if os.name != "nt":
        return
    try:
        if ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4)):
            return
    except (AttributeError, OSError):
        pass
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
    except (AttributeError, OSError):
        pass


class Win32WindowService:
    """Enumerate top-level windows and validate selected HWND/PID on capture."""

    def __init__(self) -> None:
        if os.name != "nt":
            raise OSError("SekoBot pencere yakalama şu anda yalnızca Windows destekler.")
        self.user32 = ctypes.windll.user32
        api = self.user32
        api.IsWindow.argtypes = [wintypes.HWND]
        api.IsWindow.restype = wintypes.BOOL
        api.IsWindowVisible.argtypes = [wintypes.HWND]
        api.IsWindowVisible.restype = wintypes.BOOL
        api.IsIconic.argtypes = [wintypes.HWND]
        api.IsIconic.restype = wintypes.BOOL
        api.GetWindowTextLengthW.argtypes = [wintypes.HWND]
        api.GetWindowTextLengthW.restype = ctypes.c_int
        api.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        api.GetWindowTextW.restype = ctypes.c_int
        api.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
        api.GetClientRect.restype = wintypes.BOOL
        api.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
        api.ClientToScreen.restype = wintypes.BOOL
        api.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        api.GetWindowThreadProcessId.restype = wintypes.DWORD
        self._cbtype = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        api.EnumWindows.argtypes = [self._cbtype, wintypes.LPARAM]
        api.EnumWindows.restype = wintypes.BOOL

    def process_id(self, hwnd: int) -> int:
        pid = wintypes.DWORD(0)
        self.user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        return int(pid.value)

    def client_region(self, hwnd: int, expected_pid: int | None = None) -> Region:
        if not self.user32.IsWindow(hwnd):
            raise WindowUnavailable("Oyun penceresi kapatıldı veya artık bulunamıyor.")
        if expected_pid is not None and self.process_id(hwnd) != expected_pid:
            raise WindowUnavailable("Pencere kimliği değişti. Lütfen tekrar bağlanın.")
        if self.user32.IsIconic(hwnd):
            raise WindowUnavailable("Oyun simge durumunda. Önce pencereyi geri yükleyin.")
        rect = wintypes.RECT()
        origin = wintypes.POINT(0, 0)
        if not self.user32.GetClientRect(hwnd, ctypes.byref(rect)):
            raise WindowUnavailable("Oyun penceresinin boyutları okunamadı.")
        if not self.user32.ClientToScreen(hwnd, ctypes.byref(origin)):
            raise WindowUnavailable("Oyun penceresinin konumu okunamadı.")
        width = int(rect.right - rect.left)
        height = int(rect.bottom - rect.top)
        if width < 32 or height < 32:
            raise WindowUnavailable("Oyun penceresinin görüntü alanı çok küçük.")
        return Region(int(origin.x), int(origin.y), width, height)

    def list_windows(self) -> list[WindowInfo]:
        found: list[WindowInfo] = []

        @self._cbtype
        def callback(hwnd: int, unused: int) -> bool:
            del unused
            if not self.user32.IsWindowVisible(hwnd):
                return True
            length = self.user32.GetWindowTextLengthW(hwnd)
            if length < 2:
                return True
            buffer = ctypes.create_unicode_buffer(length + 1)
            self.user32.GetWindowTextW(hwnd, buffer, length + 1)
            title = buffer.value.strip()
            if not title:
                return True
            try:
                rect = self.client_region(hwnd)
            except WindowUnavailable:
                if not self.user32.IsIconic(hwnd):
                    return True
                raw_rect = wintypes.RECT()
                if not self.user32.GetClientRect(hwnd, ctypes.byref(raw_rect)):
                    return True
                rect = Region(0, 0, raw_rect.right - raw_rect.left, raw_rect.bottom - raw_rect.top)
            if rect.width < 300 or rect.height < 200:
                return True
            found.append(WindowInfo(int(hwnd), title, self.process_id(hwnd), rect.width, rect.height))
            return True

        if not self.user32.EnumWindows(callback, 0):
            raise WindowUnavailable("Windows pencere listesi okunamadı.")
        return sorted(found, key=lambda w: (not is_metin2_title(w.title), w.title.casefold()))
