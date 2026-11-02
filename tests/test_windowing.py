from sekobot.windowing import Region, WindowInfo, is_metin2_title

def test_metin2_title_ignores_case_and_spaces():
    assert is_metin2_title("METIN2 - Server")
    assert is_metin2_title("  Metin 2 Client ")
    assert not is_metin2_title("Notepad")

def test_window_label_identifies_process_and_dimensions():
    item = WindowInfo(100, "Metin2", 200, 800, 600)
    assert "Metin2" in item.label
    assert "PID 200" in item.label
    assert "800×600" in item.label

def test_region_dict_preserves_negative_multi_monitor_coordinates():
    assert Region(-1920, 10, 1280, 720).as_dict() == {
        "left": -1920, "top": 10, "width": 1280, "height": 720
    }
