from dataclasses import dataclass
import pytest
from sekobot.capture import capture_window
from sekobot.windowing import Region, WindowInfo

TARGET = WindowInfo(hwnd=123, title="Metin2", process_id=456, width=640, height=480)

class FakeWindows:
    def __init__(self): self.calls = []
    def client_region(self, hwnd, expected_pid=None):
        self.calls.append((hwnd, expected_pid))
        return Region(-100, 55, 3, 2)

@dataclass
class FakeShot:
    width: int = 3
    height: int = 2
    rgb: bytes = b"\x01\x02\x03" * 6

class FakeGrabber:
    def __init__(self, shot=None):
        self.shot = shot if shot is not None else FakeShot()
        self.regions = []
    def grab(self, region):
        self.regions.append(region)
        return self.shot

def test_capture_uses_client_region_and_original_pid():
    windows = FakeWindows()
    grabber = FakeGrabber()
    frame = capture_window(TARGET, windows, grabber)
    assert windows.calls == [(123, 456)]
    assert grabber.regions == [{"left": -100, "top": 55, "width": 3, "height": 2}]
    assert frame.width == 3 and frame.height == 2
    assert frame.rgb == b"\x01\x02\x03" * 6

def test_capture_detects_wrong_dimensions():
    with pytest.raises(RuntimeError, match="boyutları"):
        capture_window(TARGET, FakeWindows(), FakeGrabber(FakeShot(width=4)))

def test_capture_detects_truncated_image_buffer():
    with pytest.raises(RuntimeError, match="eksik"):
        capture_window(TARGET, FakeWindows(), FakeGrabber(FakeShot(rgb=b"bad")))
