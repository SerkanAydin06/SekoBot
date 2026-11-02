"""Capture the client area of one selected window, never the whole desktop."""

from __future__ import annotations
from dataclasses import dataclass
from typing import Protocol

from sekobot.windowing import Region, WindowInfo


class WindowRegionProvider(Protocol):
    def client_region(self, hwnd: int, expected_pid: int | None = None) -> Region: ...


class Screenshot(Protocol):
    width: int
    height: int

    @property
    def rgb(self) -> bytes: ...


class Grabber(Protocol):
    def grab(self, monitor: dict[str, int]) -> Screenshot: ...


@dataclass(frozen=True, slots=True)
class CapturedFrame:
    width: int
    height: int
    rgb: bytes


def capture_window(target: WindowInfo, windows: WindowRegionProvider, grabber: Grabber) -> CapturedFrame:
    """Track a window as it moves and resizes; excludes titlebar and frame."""
    region = windows.client_region(target.hwnd, target.process_id)
    screenshot = grabber.grab(region.as_dict())
    if screenshot.width != region.width or screenshot.height != region.height:
        raise RuntimeError("Yakalanan görüntünün boyutları pencereyle eşleşmiyor.")
    rgb = screenshot.rgb
    if len(rgb) != region.width * region.height * 3:
        raise RuntimeError("Yakalanan görüntü verisi eksik.")
    return CapturedFrame(region.width, region.height, rgb)
