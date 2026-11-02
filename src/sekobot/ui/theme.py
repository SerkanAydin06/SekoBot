"""Consistent dark-mode design tokens and Qt stylesheet."""

STYLESHEET = r"""
QWidget { background-color: #0B1020; color: #E6ECF8; font-family: "Segoe UI"; font-size: 13px; }
QMainWindow, QWidget#root { background: #0B1020; }
QFrame#sidebar, QFrame#previewCard, QFrame#logCard {
  background: #131B2D; border: 1px solid #29344B; border-radius: 14px;
}
QFrame#brandIcon { background: #2563EB; border-radius: 12px; }
QLabel#brandLetter { background: transparent; color: white; font-weight: 900; font-size: 26px; }
QLabel#brandTitle { color: #F8FAFC; font-size: 23px; font-weight: 800; }
QLabel#brandSub { color: #8CA0BF; font-size: 12px; }
QLabel#sectionTitle { background: transparent; color: #F3F7FF; font-size: 15px; font-weight: 700; }
QLabel#eyebrow { background: transparent; color: #7E98C0; font-weight: 800; font-size: 10px; }
QLabel#muted, QLabel#hint { background: transparent; color: #98A6BD; }
QLabel#statValue { background: transparent; color: #F2F7FF; font-size: 16px; font-weight: 700; }
QLabel#preview {
  background: #060B16; color: #778BAE; border: 1px dashed #33425D;
  border-radius: 10px; font-size: 15px;
}
QLabel#status {
  background: #1C3141; color: #7DDBCE; border: 1px solid #285462;
  border-radius: 12px; padding: 6px 12px; font-size: 12px; font-weight: 700;
}
QComboBox {
  background: #0E1729; border: 1px solid #34415A; border-radius: 9px;
  padding: 10px 12px; color: #E8EEF9; min-height: 20px;
}
QComboBox:hover { border-color: #5372A9; }
QComboBox::drop-down { border: none; width: 25px; }
QComboBox QAbstractItemView {
  background: #172238; color: #EDF4FF; selection-background-color: #2958A7;
  border: 1px solid #34415A; outline: none;
}
QPushButton {
  background: #253147; color: #E7EEFB; border: 1px solid #384864;
  border-radius: 9px; padding: 11px 13px; font-weight: 700; min-height: 19px;
}
QPushButton:hover { background: #304362; border-color: #6085BC; }
QPushButton:pressed { background: #1D2C43; }
QPushButton:disabled { color: #64758E; background: #172031; border-color: #263247; }
QPushButton#primary { background: #2563EB; color: #FFFFFF; border-color: #3B82F6; }
QPushButton#primary:hover { background: #397BFF; }
QPushButton#primary:disabled { background: #1A3154; color: #7C92B7; border-color: #243957; }
QPushButton#danger { background: #342031; color: #F2BAC8; border-color: #684056; }
QPushButton#danger:hover { background: #4A2A3D; }
QPushButton#danger:disabled { color: #746875; background: #231B27; border-color: #332638; }
QPlainTextEdit {
  background: #0D1525; border: 1px solid #2A3650; border-radius: 9px;
  color: #A7BDD8; padding: 10px; font-family: Consolas; font-size: 12px;
}
QScrollBar:vertical { background: #111D30; width: 9px; }
QScrollBar::handle:vertical { background: #394D6C; border-radius: 4px; min-height: 32px; }
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }
"""
