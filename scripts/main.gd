extends Control
## SekoBot desktop shell: pure GDScript UI plus external Win32 helper.

const BACKGROUND := Color("#0B1020")
const SURFACE := Color("#141D30")
const SURFACE_DEEP := Color("#0C1424")
const BORDER := Color("#2A3952")
const WHITE := Color("#EDF3FF")
const MUTED := Color("#92A5C2")
const BLUE := Color("#3B82F6")
const GREEN := Color("#59DBB5")
const RED := Color("#FF8195")

@onready var _bridge: Node = $WindowCaptureBridge

var _windows: Array = []
var _running := false
var _active_hwnd: int = 0
var _active_pid: int = 0
var _last_image: Image
var _texture: ImageTexture
var _auto_attach_pending := true
var _last_backend_error := ""
var _interval := 0.1
var _elapsed := 0.0
var _fps_elapsed := 0.0
var _fps_frames := 0
var _total_frames := 0
var _refresh_timer: Timer
var _log_lines: Array[String] = []
var _settings := ConfigFile.new()

var _window_selector: ItemList
var _scan_details: Label
var _window_signature := ""
var _fps_selector: OptionButton
var _connect_button: Button
var _disconnect_button: Button
var _save_button: Button
var _status_text: Label
var _status_panel: PanelContainer
var _window_details: Label
var _preview_image: TextureRect
var _preview_empty: CenterContainer
var _preview_caption: Label
var _fps_metric: Label
var _resolution_metric: Label
var _frame_metric: Label
var _log_view: RichTextLabel


func _ready() -> void:
	# Force a compact window even when the editor keeps older run dimensions.
	get_window().mode = Window.MODE_WINDOWED
	get_window().min_size = Vector2i(800, 500)
	get_window().borderless = true
	get_window().unresizable = true
	get_window().size = Vector2i(900, 580)
	_build_interface()
	_settings.load("user://settings.cfg")
	var saved_fps: int = int(_settings.get_value("capture", "fps", 10))
	var selection := [5, 10, 15].find(saved_fps)
	_fps_selector.select(maxi(selection, 0))
	_on_fps_selected(_fps_selector.selected)
	_write_log("SekoBot başlatıldı. Windows pencereleri aranıyor.")
	_refresh_windows()
	_refresh_timer = Timer.new()
	_refresh_timer.wait_time = 2.0
	_refresh_timer.autostart = true
	_refresh_timer.timeout.connect(_refresh_windows)
	add_child(_refresh_timer)
	set_process(true)


func _process(delta: float) -> void:
	if not _running:
		return
	_elapsed += delta
	if _elapsed < _interval:
		return
	_elapsed = fmod(_elapsed, _interval)
	_capture_once()


func _build_interface() -> void:
	var background := ColorRect.new()
	background.color = BACKGROUND
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(background)

	var margins := MarginContainer.new()
	margins.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right"]:
		margins.add_theme_constant_override("margin_" + side, 12)
	for side in ["top", "bottom"]:
		margins.add_theme_constant_override("margin_" + side, 10)
	add_child(margins)

	var page := VBoxContainer.new()
	page.add_theme_constant_override("separation", 9)
	margins.add_child(page)
	_make_header(page)

	var body := HBoxContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 10)
	page.add_child(body)
	_make_sidebar(body)
	_make_workspace(body)


func _make_header(parent: VBoxContainer) -> void:
	var bar := HBoxContainer.new()
	bar.add_theme_constant_override("separation", 9)
	parent.add_child(bar)

	# Native Windows frame is removed; this becomes the draggable title bar.
	var drag_title := Button.new()
	drag_title.text = "◆  SEKOBOT    /    BOT PANELİ"
	drag_title.alignment = HORIZONTAL_ALIGNMENT_LEFT
	drag_title.flat = true
	drag_title.focus_mode = Control.FOCUS_NONE
	drag_title.custom_minimum_size.y = 38
	drag_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	drag_title.mouse_default_cursor_shape = Control.CURSOR_MOVE
	drag_title.add_theme_font_size_override("font_size", 17)
	drag_title.add_theme_color_override("font_color", WHITE)
	drag_title.add_theme_color_override("font_hover_color", WHITE)
	drag_title.gui_input.connect(_on_drag_header)
	drag_title.tooltip_text = "Buradan tutarak pencereyi taşı"
	bar.add_child(drag_title)

	_status_panel = _card(Color("#193447"), Color("#2C6375"), 11, 6)
	_status_panel.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	bar.add_child(_status_panel)
	_status_text = _text("●  HAZIR", 11, GREEN)
	_status_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_status_panel.add_child(_status_text)

	var minimize_button := _window_button("─", "Küçült")
	minimize_button.pressed.connect(_minimize_window)
	bar.add_child(minimize_button)

	var close_button := _window_button("×", "Kapat")
	close_button.add_theme_color_override("font_hover_color", RED)
	close_button.pressed.connect(_close_window)
	bar.add_child(close_button)


func _window_button(symbol: String, hint: String) -> Button:
	var button := Button.new()
	button.text = symbol
	button.tooltip_text = hint
	button.custom_minimum_size = Vector2(34, 32)
	button.focus_mode = Control.FOCUS_NONE
	button.add_theme_font_size_override("font_size", 18)
	button.add_theme_color_override("font_color", MUTED)
	button.add_theme_color_override("font_hover_color", WHITE)
	button.add_theme_stylebox_override("normal", _panel_style(SURFACE, BORDER, 8, 4))
	button.add_theme_stylebox_override("hover", _panel_style(Color("#26364F"), BLUE, 8, 4))
	button.add_theme_stylebox_override("pressed", _panel_style(SURFACE_DEEP, BLUE, 8, 4))
	button.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	return button


func _on_drag_header(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			get_window().start_drag()


func _minimize_window() -> void:
	get_window().mode = Window.MODE_MINIMIZED


func _close_window() -> void:
	get_tree().quit()


func _make_sidebar(parent: HBoxContainer) -> void:
	var sidebar_scroll := ScrollContainer.new()
	sidebar_scroll.custom_minimum_size.x = 286
	sidebar_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(sidebar_scroll)
	var panel := _card(SURFACE, BORDER, 13, 12)
	panel.custom_minimum_size.x = 276
	sidebar_scroll.add_child(panel)

	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 8)
	panel.add_child(column)

	column.add_child(_text("01   /   PENCERE BAĞLANTISI", 11, BLUE))
	column.add_child(_text("Oyun penceresi", 19, WHITE))
	var intro := _text("Metin2 penceresini seç veya otomatik bağlan.", 11, MUTED)
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(intro)

	_window_selector = ItemList.new()
	_window_selector.custom_minimum_size.y = 105
	_window_selector.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_window_selector.add_theme_stylebox_override("panel", _panel_style(SURFACE_DEEP, BORDER, 9, 6))
	_window_selector.add_theme_stylebox_override("selected", _panel_style(Color("#254778"), Color("#5789C7"), 6, 5))
	_window_selector.add_theme_stylebox_override("selected_focus", _panel_style(Color("#254778"), Color("#5789C7"), 6, 5))
	_window_selector.add_theme_color_override("font_color", WHITE)
	_window_selector.add_theme_color_override("font_selected_color", WHITE)
	_window_selector.item_selected.connect(_on_window_selected)
	_window_selector.add_item("Pencereler taranıyor...")
	_window_selector.set_item_disabled(0, true)
	column.add_child(_window_selector)
	_scan_details = _text("Pencereler taranıyor...", 10, MUTED)
	_scan_details.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(_scan_details)

	var refresh := _button("↻   Yenile / Yeniden dene", "neutral")
	refresh.pressed.connect(_force_refresh_windows)
	column.add_child(refresh)

	_spacer(column, 3)
	column.add_child(_text("02   /   GÖRÜNTÜ AYARLARI", 11, BLUE))
	column.add_child(_text("Önizleme hızı", 15, WHITE))
	_fps_selector = OptionButton.new()
	_fps_selector.custom_minimum_size.y = 34
	for fps in [5, 10, 15]:
		_fps_selector.add_item("%d FPS" % fps, fps)
	_style_button(_fps_selector, "neutral")
	_fps_selector.item_selected.connect(_on_fps_selected)
	column.add_child(_fps_selector)

	_spacer(column, 3)
	_connect_button = _button("▶   Pencereye bağlan", "primary")
	_connect_button.pressed.connect(_begin_capture)
	column.add_child(_connect_button)

	_disconnect_button = _button("■   Bağlantıyı durdur", "danger")
	_disconnect_button.pressed.connect(_stop_capture)
	column.add_child(_disconnect_button)

	_save_button = _button("▣   Ekran görüntüsü kaydet", "neutral")
	_save_button.pressed.connect(_save_screenshot)
	column.add_child(_save_button)

	_spacer(column, 3)
	column.add_child(_text("BAĞLANTI BİLGİLERİ", 11, BLUE))
	_window_details = _text("Oyun penceresi aranıyor...", 11, MUTED)
	_window_details.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(_window_details)

	var flexible := Control.new()
	flexible.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(flexible)
	var separator := HSeparator.new()
	column.add_child(separator)
	var note := _text("Pencere izleme aktif. Farm henüz eklenmedi.", 10, MUTED)
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(note)


func _make_workspace(parent: HBoxContainer) -> void:
	var workspace := VBoxContainer.new()
	workspace.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	workspace.add_theme_constant_override("separation", 9)
	parent.add_child(workspace)

	var preview_card := _card(SURFACE, BORDER, 13, 12)
	preview_card.size_flags_vertical = Control.SIZE_EXPAND_FILL
	workspace.add_child(preview_card)

	var content := VBoxContainer.new()
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 8)
	preview_card.add_child(content)

	var preview_header := HBoxContainer.new()
	content.add_child(preview_header)
	var titles := VBoxContainer.new()
	titles.add_theme_constant_override("separation", 4)
	preview_header.add_child(titles)
	titles.add_child(_text("CANLI PENCERE GÖRÜNTÜSÜ", 11, BLUE))
	titles.add_child(_text("Oyun önizlemesi", 15, WHITE))
	var fill := Control.new()
	fill.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	preview_header.add_child(fill)
	_preview_caption = _text("Pencere bekleniyor", 12, MUTED)
	_preview_caption.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	preview_header.add_child(_preview_caption)

	var screen := PanelContainer.new()
	screen.add_theme_stylebox_override("panel", _panel_style(SURFACE_DEEP, Color("#344865"), 12, 5))
	screen.custom_minimum_size = Vector2(0, 170)
	screen.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_child(screen)

	var canvas := Control.new()
	canvas.clip_contents = true
	screen.add_child(canvas)

	_preview_image = TextureRect.new()
	_preview_image.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_preview_image.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_preview_image.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_preview_image.visible = false
	canvas.add_child(_preview_image)

	_preview_empty = CenterContainer.new()
	_preview_empty.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	canvas.add_child(_preview_empty)
	var empty_column := VBoxContainer.new()
	empty_column.add_theme_constant_override("separation", 13)
	_preview_empty.add_child(empty_column)
	var empty_icon := _text("▣", 38, Color("#5074A6"))
	empty_icon.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	empty_column.add_child(empty_icon)
	var empty_title := _text("Metin2 penceresi bekleniyor", 18, WHITE)
	empty_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	empty_column.add_child(empty_title)
	var empty_help := _text("Oyunu pencere modunda aç. SekoBot onu otomatik bulacak.", 12, MUTED)
	empty_help.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	empty_column.add_child(empty_help)

	var metrics := HBoxContainer.new()
	metrics.add_theme_constant_override("separation", 18)
	content.add_child(metrics)
	_fps_metric = _metric(metrics, "ANLIK FPS", "—")
	_resolution_metric = _metric(metrics, "ÇÖZÜNÜRLÜK", "—")
	_frame_metric = _metric(metrics, "TOPLAM KARE", "0")
	var metric_flex := Control.new()
	metric_flex.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	metrics.add_child(metric_flex)

	var logs := _card(SURFACE, BORDER, 13, 9)
	logs.custom_minimum_size.y = 108
	workspace.add_child(logs)
	var log_column := VBoxContainer.new()
	log_column.add_theme_constant_override("separation", 4)
	logs.add_child(log_column)
	log_column.add_child(_text("SİSTEM GÜNLÜĞÜ", 11, BLUE))
	_log_view = RichTextLabel.new()
	_log_view.bbcode_enabled = false
	_log_view.scroll_following = true
	_log_view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_log_view.add_theme_color_override("default_color", MUTED)
	_log_view.add_theme_font_size_override("normal_font_size", 12)
	log_column.add_child(_log_view)


func _text(value: String, font_size: int, tint: Color) -> Label:
	var result := Label.new()
	result.text = value
	result.add_theme_font_size_override("font_size", font_size)
	result.add_theme_color_override("font_color", tint)
	return result


func _panel_style(fill: Color, border: Color, radius: int, padding: int) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = fill
	style.border_color = border
	style.set_border_width_all(1)
	style.set_corner_radius_all(radius)
	style.content_margin_left = padding
	style.content_margin_right = padding
	style.content_margin_top = padding
	style.content_margin_bottom = padding
	return style


func _card(fill: Color, border: Color, radius: int, padding: int) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", _panel_style(fill, border, radius, padding))
	return panel


func _style_button(button: Button, variant: String) -> void:
	var fill := Color("#22324D")
	var hover := Color("#304664")
	var outline := Color("#3B526F")
	var font_color := WHITE
	if variant == "primary":
		fill = Color("#2563EB")
		hover = Color("#3B82F6")
		outline = Color("#538DFB")
	elif variant == "danger":
		fill = Color("#392435")
		hover = Color("#542B42")
		outline = Color("#6C4057")
		font_color = Color("#FFC4D0")
	button.add_theme_stylebox_override("normal", _panel_style(fill, outline, 9, 9))
	button.add_theme_stylebox_override("hover", _panel_style(hover, outline, 9, 9))
	button.add_theme_stylebox_override("pressed", _panel_style(hover.darkened(0.2), outline, 9, 9))
	button.add_theme_stylebox_override("disabled", _panel_style(Color("#172233"), Color("#25334A"), 9, 9))
	button.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	button.add_theme_color_override("font_color", font_color)
	button.add_theme_color_override("font_hover_color", WHITE)
	button.add_theme_color_override("font_disabled_color", Color("#62758F"))
	button.add_theme_font_size_override("font_size", 13)


func _button(title: String, variant: String) -> Button:
	var result := Button.new()
	result.text = title
	result.custom_minimum_size.y = 35
	_style_button(result, variant)
	return result


func _spacer(column: VBoxContainer, height: float) -> void:
	var space := Control.new()
	space.custom_minimum_size.y = height
	column.add_child(space)


func _metric(parent: HBoxContainer, header: String, value: String) -> Label:
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 4)
	parent.add_child(column)
	column.add_child(_text(header, 10, MUTED))
	var text := _text(value, 17, WHITE)
	column.add_child(text)
	return text


func _on_window_selected(_index: int) -> void:
	_update_controls()


func _on_fps_selected(index: int) -> void:
	if index < 0:
		return
	var fps: int = _fps_selector.get_item_id(index)
	_interval = 1.0 / float(fps)
	_settings.set_value("capture", "fps", fps)
	_settings.save("user://settings.cfg")


func _selected_window() -> Dictionary:
	if _window_selector == null:
		return {}
	var chosen: PackedInt32Array = _window_selector.get_selected_items()
	if chosen.is_empty():
		return {}
	var index: int = chosen[0]
	if index < 0 or index >= _windows.size():
		return {}
	return _windows[index]


func _force_refresh_windows() -> void:
	if _running:
		return
	if _bridge.has_method("RestartHelper"):
		var current: Dictionary = _bridge.call("GetStatus")
		if not bool(current.get("alive", false)):
			_bridge.call("RestartHelper")
			_window_signature = ""
	_refresh_windows()


func _refresh_windows() -> void:
	if _running:
		return
	if not _bridge.has_method("ListWindows"):
		_set_status("YARDIMCI MODÜL EKSİK", RED)
		_scan_details.text = "GDScript Windows köprüsü yüklenemedi."
		_write_log("Windows yardımcı modülü yüklenemedi.")
		return

	var previous := _selected_window()
	var old_handle: int = int(previous.get("hwnd", 0))
	var old_pid: int = int(previous.get("pid", 0))
	var new_windows: Array = _bridge.call("ListWindows")
	var info: Dictionary = _bridge.call("GetStatus")
	var backend_error: String = str(_bridge.call("GetLastError"))
	_scan_details.text = str(info.get("message", "Windows pencereleri taranıyor..."))

	if not backend_error.is_empty():
		_set_status("WINDOWS YARDIMCISI HATASI", RED)
		if backend_error != _last_backend_error:
			_write_log(backend_error)
			_last_backend_error = backend_error
		_preview_caption.text = "Pencere listesi okunamadı"
		if _window_signature != "error":
			_windows.clear()
			_window_selector.clear()
			_window_selector.add_item("Tarama hatası — Yeniden dene")
			_window_selector.set_item_disabled(0, true)
			_window_signature = "error"
		_update_controls()
		return

	_last_backend_error = ""
	var signature := JSON.stringify(new_windows)
	if signature != _window_signature:
		_window_signature = signature
		_windows = new_windows
		_window_selector.clear()
		var selected_index := -1
		var metin_indices: Array[int] = []
		for i in range(_windows.size()):
			var item: Dictionary = _windows[i]
			var title: String = str(item.get("title", "Pencere"))
			var pid: int = int(item.get("pid", 0))
			var width: int = int(item.get("width", 0))
			var height: int = int(item.get("height", 0))
			var minimized_suffix := " [küçültülmüş]" if item.get("minimized", false) else ""
			var dimensions := ("%dx%d" % [width, height]) if width > 0 and height > 0 else "boyut bekleniyor"
			_window_selector.add_item("%s · %s%s" % [title, dimensions, minimized_suffix])
			_window_selector.set_item_tooltip(i, "PID %d  |  %s" % [pid, title])
			if item.get("metin2", false):
				metin_indices.append(i)
			if old_handle == int(item.get("hwnd", 0)) and old_pid == pid:
				selected_index = i
		if _windows.is_empty():
			var placeholder := "Tarama sürüyor..." if not bool(info.get("ready", false)) else "Uygun pencere bulunamadı"
			_window_selector.add_item(placeholder)
			_window_selector.set_item_disabled(0, true)
		elif selected_index >= 0:
			_window_selector.select(selected_index)
		elif not metin_indices.is_empty():
			_window_selector.select(metin_indices[0])
		else:
			_window_selector.select(0)
		if not _windows.is_empty():
			_write_log("%d açık pencere listelendi." % _windows.size())

	if _windows.is_empty():
		if bool(info.get("ready", false)):
			_set_status("PENCERE BEKLENİYOR", MUTED)
			_window_selector.set_item_text(0, "Uygun pencere bulunamadı")
		else:
			_set_status("TARANIYOR", MUTED)
			_window_selector.set_item_text(0, "Windows pencereleri taranıyor...")
		_preview_caption.text = "Pencere bulunamadı"
	else:
		_set_status("HAZIR", GREEN)
		_preview_caption.text = "%d açık pencere bulundu" % _windows.size()

	_update_controls()
	var selected_metin: Array[int] = []
	for i in range(_windows.size()):
		if bool(_windows[i].get("metin2", false)):
			selected_metin.append(i)
	if _auto_attach_pending and selected_metin.size() == 1:
		_auto_attach_pending = false
		_window_selector.select(selected_metin[0])
		_update_controls()
		_write_log("Tek Metin2 penceresi bulundu. Otomatik bağlanılıyor.")
		call_deferred("_begin_capture")


func _begin_capture() -> void:
	if _running:
		return
	var chosen := _selected_window()
	if chosen.is_empty():
		_write_log("Önce oyun penceresi seçilmeli.")
		return
	if chosen.get("minimized", false):
		_write_log("Oyun simge durumunda. Pencereyi açıp yeniden deneyin.")
		_set_status("PENCERE KÜÇÜLTÜLMÜŞ", RED)
		return
	_active_hwnd = int(chosen.get("hwnd", 0))
	_active_pid = int(chosen.get("pid", 0))
	_bridge.call("StartCapture", _active_hwnd, _active_pid, int(_fps_selector.get_item_id(_fps_selector.selected)))
	_elapsed = _interval
	_fps_elapsed = 0.0
	_fps_frames = 0
	_running = true
	_preview_caption.text = str(chosen.get("title", "Oyun"))
	_set_status("BAĞLANIYOR", MUTED)
	_write_log("Bağlantı başlatıldı: %s (PID %d)" % [_preview_caption.text, _active_pid])
	_update_controls()


func _capture_once() -> void:
	var frame: Image = _bridge.call("CaptureFrame", _active_hwnd, _active_pid)
	if frame == null or frame.is_empty():
		var reason: String = str(_bridge.call("GetLastError"))
		if reason.is_empty():
			return  # Asynchronous helper has not yet produced a new frame.
		_stop_capture()
		_set_status("YAKALAMA HATASI", RED)
		_write_log(reason if not reason.is_empty() else "Görüntü yakalama başarısız.")
		return

	_last_image = frame
	if _texture == null or _texture.get_width() != frame.get_width() or _texture.get_height() != frame.get_height():
		_texture = ImageTexture.create_from_image(frame)
		_preview_image.texture = _texture
	else:
		_texture.update(frame)
	_preview_image.visible = true
	_preview_empty.visible = false
	_total_frames += 1
	_fps_frames += 1
	_frame_metric.text = str(_total_frames)
	_resolution_metric.text = "%d × %d" % [frame.get_width(), frame.get_height()]
	_fps_elapsed += maxf(_interval, get_process_delta_time())
	if _fps_elapsed >= 1.0:
		_fps_metric.text = "%.1f" % (float(_fps_frames) / _fps_elapsed)
		_fps_elapsed = 0.0
		_fps_frames = 0
	if _status_text.text != "●  BAĞLI":
		_set_status("BAĞLI", GREEN)
	_update_controls()


func _stop_capture() -> void:
	if not _running:
		return
	_running = false
	_bridge.call("StopCapture")
	_active_hwnd = 0
	_active_pid = 0
	_set_status("DURDURULDU", MUTED)
	_write_log("Pencere bağlantısı durduruldu.")
	_update_controls()


func _save_screenshot() -> void:
	if _last_image == null or _last_image.is_empty():
		_write_log("Kaydedilecek bir ekran görüntüsü yok.")
		return
	var folder := OS.get_environment("USERPROFILE").path_join("Pictures").path_join("SekoBot")
	if OS.get_environment("USERPROFILE").is_empty():
		folder = ProjectSettings.globalize_path("user://captures")
	var result := DirAccess.make_dir_recursive_absolute(folder)
	if result != OK:
		_write_log("Klasör oluşturulamadı: %s" % folder)
		return
	var filename := "SekoBot_%s_%d.png" % [
		Time.get_datetime_string_from_system().replace(":", "-"),
		Time.get_ticks_msec() % 1000
	]
	var destination := folder.path_join(filename)
	result = _last_image.save_png(destination)
	if result != OK:
		_write_log("PNG kaydedilemedi (hata %d)." % result)
		return
	_write_log("PNG kaydedildi: %s" % destination)
	_set_status("PNG KAYDEDİLDİ", GREEN)


func _update_controls() -> void:
	if _window_selector == null:
		return
	_window_selector.mouse_filter = Control.MOUSE_FILTER_IGNORE if _running else Control.MOUSE_FILTER_STOP
	_window_selector.modulate.a = 0.7 if _running else 1.0
	_fps_selector.disabled = _running
	_connect_button.disabled = _running or _selected_window().is_empty()
	_disconnect_button.disabled = not _running
	_save_button.disabled = _last_image == null
	var chosen := _selected_window()
	if chosen.is_empty():
		_window_details.text = "Bağlanılacak pencere seçilmedi."
	else:
		_window_details.text = "%s\nPID: %d   ·   HWND: %s" % [
			chosen.get("title", ""), chosen.get("pid", 0), str(chosen.get("hwnd", 0))
		]


func _set_status(message: String, tint: Color) -> void:
	_status_text.text = "●  " + message
	_status_text.add_theme_color_override("font_color", tint)
	_status_panel.add_theme_stylebox_override("panel", _panel_style(
		SURFACE_DEEP, tint.darkened(0.5), 14, 9
	))


func _write_log(message: String) -> void:
	var now := Time.get_time_string_from_system()
	_log_lines.append("[%s] %s" % [now, message])
	if _log_lines.size() > 100:
		_log_lines.pop_front()
	if _log_view != null:
		_log_view.text = "\n".join(_log_lines)
