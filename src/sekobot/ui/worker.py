"""Background capture thread with graceful cancellation and GUI signals."""

from __future__ import annotations

import threading
import time
from PySide6.QtCore import QThread, Signal
from sekobot.capture import CapturedFrame, capture_window
from sekobot.windowing import Win32WindowService, WindowInfo


class CaptureWorker(QThread):
    frame_ready = Signal(object)
    metrics_ready = Signal(float, int, int)
    failed = Signal(str)

    def __init__(self, target: WindowInfo, target_fps: int = 10, parent=None) -> None:
        super().__init__(parent)
        self._target = target
        self._interval = 1.0 / max(1, min(30, target_fps))
        self._stop = threading.Event()

    def request_stop(self) -> None:
        self._stop.set()

    def run(self) -> None:
        try:
            from mss import MSS

            windows = Win32WindowService()
            samples = 0
            sample_start = time.monotonic()
            with MSS() as grabber:
                while not self._stop.is_set():
                    started = time.monotonic()
                    frame: CapturedFrame = capture_window(self._target, windows, grabber)
                    self.frame_ready.emit(frame)
                    samples += 1
                    elapsed = time.monotonic() - sample_start
                    if elapsed >= 1.0:
                        self.metrics_ready.emit(samples / elapsed, frame.width, frame.height)
                        samples = 0
                        sample_start = time.monotonic()
                    remaining = self._interval - (time.monotonic() - started)
                    if remaining > 0:
                        self._stop.wait(remaining)
        except Exception as exc:
            if not self._stop.is_set():
                self.failed.emit(str(exc))
