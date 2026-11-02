"""SekoBot v0.1 desktop dashboard: select, preview and save a game window."""

from __future__ import annotations

from datetime import datetime
from pathlib import Path

from PySide6.QtCore import Qt, QSettings, QTimer
from PySide6.QtGui import QCloseEvent, QImage, QPixmap
from PySide6.QtWidgets import (
    QComboBox,
    QFrame,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from sekobot.capture import CapturedFrame
from sekobot.ui.theme import STYLESHEET
from sekobot.ui.worker import CaptureWorker
from sekobot.windowing import WindowInfo, Win32WindowService, is_metin2_title


def label(text: str, kind: str = "muted") -> QLabel:
    widget = QLabel(text)
    widget.setObjectName(kind)
    widget.setWordWrap(True)
    return widget


def card(object_name: str) -> QFrame:
    widget = QFrame()
    widget.setObjectName(object_name)
    return widget


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self._windows = Win32WindowService()
        self._worker: CaptureWorker | None = None
        self._last_image: QImage | None = None
        self._active_window: WindowInfo | None = None
        self._error_on_last_run = False
        self._auto_connect_once = True
        self._previous_count = -1
        self._captured_frames = 0
        self._settings = QSettings("SekoBot", "Desktop")

        self.setWindowTitle("SekoBot  •  Oyun Otomasyon Merkezi")
        self.resize(1380, 850)
        self.setMinimumSize(1050, 690)
        self.setStyleSheet(STYLESHEET)
        self._create_layout()
        previous = self._settings.value("geometry")
        if previous is not None:
            self.restoreGeometry(previous)

        self._refresh_timer = QTimer(self)
        self._refresh_timer.setInterval(6000)
        self._refresh_timer.timeout.connect(self.refresh_windows)
        self._refresh_timer.start()
        QTimer.singleShot(200, self.refresh_windows)

    def _create_layout(self) -> None:
        root = QWidget()
        root.setObjectName("root")
        self.setCentralWidget(root)
        page = QVBoxLayout(root)
        page.setContentsMargins(26, 24, 26, 24)
        page.setSpacing(20)

        header = QHBoxLayout()
        header.setSpacing(14)
        logo = card("brandIcon")
        logo.setFixedSize(54, 54)
        logo_layout = QVBoxLayout(logo)
        logo_layout.setContentsMargins(0, 0, 0, 0)
        logo_letter = label("S", "brandLetter")
        logo_letter.setAlignment(Qt.AlignmentFlag.AlignCenter)
        logo_layout.addWidget(logo_letter)
        header.addWidget(logo)
        brand = QVBoxLayout()
        brand.setSpacing(0)
        brand.addWidget(label("SEKOBOT", "brandTitle"))
        brand.addWidget(label("OYUN OTOMASYON MERKEZİ  /  V0.1", "brandSub"))
        header.addLayout(brand)
        header.addStretch()
        self._status = label("●  HAZIR", "status")
        self._status.setAlignment(Qt.AlignmentFlag.AlignCenter)
        header.addWidget(self._status)
        page.addLayout(header)

        body = QHBoxLayout()
        body.setSpacing(19)
        page.addLayout(body, 1)

        sidebar = card("sidebar")
        sidebar.setFixedWidth(340)
        left = QVBoxLayout(sidebar)
        left.setContentsMargins(21, 21, 21, 21)
        left.setSpacing(14)
        left.addWidget(label("01  /  PENCERE BAĞLANTISI", "eyebrow"))
        left.addWidget(label("Oyun penceresi", "sectionTitle"))
        left.addWidget(label(
            "Metin2 pencereleri otomatik öne alınır. İstersen başka pencere de seçebilirsin.",
            "hint",
        ))

        self._window_combo = QComboBox()
        self._window_combo.setMinimumWidth(0)
        self._window_combo.setSizeAdjustPolicy(
            QComboBox.SizeAdjustPolicy.AdjustToMinimumContentsLengthWithIcon
        )
        self._window_combo.setMinimumContentsLength(22)
        self._window_combo.setToolTip("Bağlanılacak oyun penceresini seçin")
        self._window_combo.currentIndexChanged.connect(self._update_controls)
        left.addWidget(self._window_combo)

        self._refresh_button = QPushButton("↻   Pencereleri yenile")
        self._refresh_button.clicked.connect(self.refresh_windows)
        left.addWidget(self._refresh_button)

        left.addSpacing(10)
        left.addWidget(label("GÖRÜNTÜ YAKALAMA", "eyebrow"))
        left.addWidget(label("Önizleme hızı", "sectionTitle"))
        self._fps_combo = QComboBox()
        for value in (5, 10, 15):
            self._fps_combo.addItem(f"{value} FPS", userData=value)
        configured_fps = int(self._settings.value("fps", 10))
        selected_index = self._fps_combo.findData(configured_fps)
        self._fps_combo.setCurrentIndex(max(0, selected_index))
        left.addWidget(self._fps_combo)

        self._start_button = QPushButton("▶   Pencereye bağlan")
        self._start_button.setObjectName("primary")
        self._start_button.clicked.connect(self.start_capture)
        left.addWidget(self._start_button)
        self._stop_button = QPushButton("■   Bağlantıyı durdur")
        self._stop_button.setObjectName("danger")
        self._stop_button.clicked.connect(self.stop_capture)
        left.addWidget(self._stop_button)
        self._save_button = QPushButton("▣   Ekran görüntüsü kaydet")
        self._save_button.clicked.connect(self.save_screenshot)
        left.addWidget(self._save_button)

        left.addSpacing(12)
        left.addWidget(label("BAĞLANTI BİLGİSİ", "eyebrow"))
        self._connection_info = label("Henüz pencere seçilmedi.", "muted")
        left.addWidget(self._connection_info)
        left.addStretch()
        left.addWidget(label("BİLGİ", "eyebrow"))
        left.addWidget(label(
            "İlk sürüm yalnızca pencereyi izler ve PNG kaydeder. "
            "Oyuna tuş veya fare komutu göndermez.",
            "hint",
        ))
        body.addWidget(sidebar)

        right = QVBoxLayout()
        right.setSpacing(16)
        body.addLayout(right, 1)
        preview_card = card("previewCard")
        panel = QVBoxLayout(preview_card)
        panel.setContentsMargins(19, 19, 19, 19)
        panel.setSpacing(12)
        title_row = QHBoxLayout()
        titles = QVBoxLayout()
        titles.setSpacing(3)
        titles.addWidget(label("CANLI PENCERE GÖRÜNTÜSÜ", "eyebrow"))
        titles.addWidget(label("Oyun önizlemesi", "sectionTitle"))
        title_row.addLayout(titles)
        title_row.addStretch()
        self._view_label = label("Metin2 penceresi bekleniyor", "muted")
        title_row.addWidget(self._view_label)
        panel.addLayout(title_row)
        self._preview = QLabel(
            "Pencere bekleniyor\n\nOyun açıldığında otomatik algılanacak"
        )
        self._preview.setObjectName("preview")
        self._preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._preview.setMinimumHeight(340)
        self._preview.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding
        )
        panel.addWidget(self._preview, 1)

        stats = QHBoxLayout()
        stats.setSpacing(32)
        self._fps_value = self._stat(stats, "ANLIK FPS", "—")
        self._resolution_value = self._stat(stats, "ÇÖZÜNÜRLÜK", "—")
        self._frame_count_value = self._stat(stats, "İŞLENEN KARE", "0")
        stats.addStretch()
        panel.addLayout(stats)
        right.addWidget(preview_card, 1)

        log_card = card("logCard")
        log_layout = QVBoxLayout(log_card)
        log_layout.setContentsMargins(18, 16, 18, 17)
        log_layout.setSpacing(9)
        log_layout.addWidget(label("SİSTEM GÜNLÜĞÜ", "eyebrow"))
        self._log = QPlainTextEdit()
        self._log.setReadOnly(True)
        self._log.setMaximumBlockCount(250)
        self._log.setFixedHeight(100)
        log_layout.addWidget(self._log)
        right.addWidget(log_card)
        self._update_controls()
        self._append_log("SekoBot başlatıldı. Açık pencereler taranıyor.")

    @staticmethod
    def _stat(layout: QHBoxLayout, heading: str, default: str) -> QLabel:
        column = QVBoxLayout()
        column.setSpacing(5)
        column.addWidget(label(heading, "eyebrow"))
        value = label(default, "statValue")
        column.addWidget(value)
        layout.addLayout(column)
        return value

    def _append_log(self, message: str) -> None:
        timestamp = datetime.now().strftime("%H:%M:%S")
        self._log.appendPlainText(f"[{timestamp}] {message}")

    def _set_status(self, text: str) -> None:
        self._status.setText(f"●  {text}")

    def _selected_window(self) -> WindowInfo | None:
        value = self._window_combo.currentData()
        return value if isinstance(value, WindowInfo) else None

    def _update_controls(self, _index: int = -1) -> None:
        running = self._worker is not None
        self._window_combo.setEnabled(not running)
        self._refresh_button.setEnabled(not running)
        self._fps_combo.setEnabled(not running)
        self._start_button.setEnabled(
            not running and self._selected_window() is not None
        )
        self._stop_button.setEnabled(running)
        self._save_button.setEnabled(self._last_image is not None)
        selected = self._active_window if running else self._selected_window()
        if selected is None:
            self._connection_info.setText("Pencere seçimi bekleniyor.")
        else:
            self._connection_info.setText(
                f"{selected.title}\n"
                f"PID: {selected.process_id}   |   HWND: 0x{selected.hwnd:X}"
            )

    def refresh_windows(self) -> None:
        if self._worker is not None:
            return
        try:
            windows = self._windows.list_windows()
        except Exception as exc:
            self._append_log(f"Pencere araması başarısız: {exc}")
            self._set_status("PENCERE HATASI")
            return
        previous = self._selected_window()
        old_hwnd = previous.hwnd if previous else None
        self._window_combo.blockSignals(True)
        self._window_combo.clear()
        for item in windows:
            self._window_combo.addItem(item.label, userData=item)
        if old_hwnd is not None:
            for i, win in enumerate(windows):
                if win.hwnd == old_hwnd and win.process_id == previous.process_id:
                    self._window_combo.setCurrentIndex(i)
                    break
        self._window_combo.blockSignals(False)
        self._update_controls()
        if len(windows) != self._previous_count:
            self._append_log(f"{len(windows)} uygun pencere bulundu.")
            self._previous_count = len(windows)
        metin_windows = [win for win in windows if is_metin2_title(win.title)]
        if self._auto_connect_once and len(metin_windows) == 1:
            self._auto_connect_once = False
            match = metin_windows[0]
            for i, win in enumerate(windows):
                if win.hwnd == match.hwnd:
                    self._window_combo.setCurrentIndex(i)
                    break
            self._append_log("Tek Metin2 penceresi bulundu. Otomatik bağlantı başlatılıyor.")
            QTimer.singleShot(100, self.start_capture)
        elif not windows:
            self._set_status("PENCERE BEKLENİYOR")

    def start_capture(self) -> None:
        if self._worker is not None:
            return
        target = self._selected_window()
        if target is None:
            self._append_log("Bağlantı için bir pencere seçin.")
            return
        self._active_window = target
        self._error_on_last_run = False
        self._fps_value.setText("—")
        self._resolution_value.setText("—")
        self._view_label.setText(target.title)
        fps = int(self._fps_combo.currentData() or 10)
        self._settings.setValue("fps", fps)
        self._worker = CaptureWorker(target, fps, self)
        self._worker.frame_ready.connect(self._on_frame)
        self._worker.metrics_ready.connect(self._on_metrics)
        self._worker.failed.connect(self._on_error)
        self._worker.finished.connect(self._on_finished)
        self._worker.start()
        self._set_status("BAĞLANIYOR")
        self._append_log(
            f"Pencereye bağlanılıyor: {target.title} (HWND 0x{target.hwnd:X})"
        )
        self._update_controls()

    def stop_capture(self) -> None:
        if self._worker is not None:
            self._worker.request_stop()
            self._stop_button.setEnabled(False)
            self._set_status("DURDURULUYOR")
            self._append_log("Görüntü yakalama durduruluyor.")

    def _on_frame(self, frame: CapturedFrame) -> None:
        image = QImage(
            frame.rgb, frame.width, frame.height, frame.width * 3,
            QImage.Format.Format_RGB888,
        )
        self._last_image = image.copy()  # Detach from the temporary Python bytes.
        self._captured_frames += 1
        self._frame_count_value.setText(
            f"{self._captured_frames:,}".replace(",", ".")
        )
        self._resolution_value.setText(f"{frame.width} × {frame.height}")
        self._render_frame()
        self._save_button.setEnabled(True)
        if self._status.text() != "●  BAĞLI":
            self._set_status("BAĞLI")

    def _on_metrics(self, fps: float, width: int, height: int) -> None:
        self._fps_value.setText(f"{fps:.1f}")
        self._resolution_value.setText(f"{width} × {height}")

    def _render_frame(self) -> None:
        if self._last_image is None:
            return
        pixmap = QPixmap.fromImage(self._last_image)
        size = self._preview.contentsRect().size()
        if size.width() < 1 or size.height() < 1:
            return
        self._preview.setPixmap(
            pixmap.scaled(
                size,
                Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.FastTransformation,
            )
        )

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        if hasattr(self, "_preview"):
            self._render_frame()

    def _on_error(self, reason: str) -> None:
        self._error_on_last_run = True
        self._set_status("YAKALAMA HATASI")
        self._append_log(f"Yakalama hatası: {reason}")

    def _on_finished(self) -> None:
        finished_worker = self.sender()
        if finished_worker is not self._worker:
            return
        self._worker = None
        self._active_window = None
        if not self._error_on_last_run:
            self._set_status("BAĞLANTI KESİLDİ")
            self._append_log("Pencere bağlantısı sonlandırıldı.")
        self._update_controls()

    def save_screenshot(self) -> None:
        if self._last_image is None:
            self._append_log("Kaydedilecek görüntü henüz yok.")
            return
        folder = Path.home() / "Pictures" / "SekoBot"
        try:
            folder.mkdir(parents=True, exist_ok=True)
            name = datetime.now().strftime("sekobot_%Y%m%d_%H%M%S_%f.png")
            destination = folder / name
            if not self._last_image.save(str(destination), "PNG"):
                raise OSError("QImage PNG dosyasını yazamadı.")
        except OSError as exc:
            self._append_log(f"Kaydetme hatası: {exc}")
            QMessageBox.warning(
                self, "SekoBot", f"Ekran görüntüsü kaydedilemedi:\n{exc}"
            )
            return
        self._append_log(f"PNG kaydedildi: {destination}")
        self._set_status("PNG KAYDEDİLDİ")
        if self._worker is not None:
            QTimer.singleShot(1800, self._restore_connected_status)

    def _restore_connected_status(self) -> None:
        if self._worker is not None and not self._error_on_last_run:
            self._set_status("BAĞLI")

    def closeEvent(self, event: QCloseEvent) -> None:
        self._settings.setValue("geometry", self.saveGeometry())
        self._refresh_timer.stop()
        if self._worker is not None:
            self._worker.request_stop()
            if not self._worker.wait(5000):
                event.ignore()
                self._append_log(
                    "Görüntü yakalama sürüyor, kapanış tekrar denenebilir."
                )
                return
        super().closeEvent(event)
