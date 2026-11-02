"""SekoBot desktop application entry point."""

import os
import sys
from sekobot.windowing import enable_dpi_awareness


def main() -> int:
    if os.name != "nt":
        print("SekoBot şu anda Windows 10/11 için hazırlanmıştır.", file=sys.stderr)
        return 1
    enable_dpi_awareness()
    from PySide6.QtWidgets import QApplication
    from sekobot.ui.main_window import MainWindow

    app = QApplication(sys.argv)
    app.setApplicationName("SekoBot")
    app.setOrganizationName("SekoBot")
    app.setStyle("Fusion")
    window = MainWindow()
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
