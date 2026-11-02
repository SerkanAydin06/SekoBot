extends Node
## Thin Godot adapter for the PowerShell/Win32 helper. No Godot .NET required.
## Only text/PNG files travel between processes; game memory is never accessed.

var _helper_pid := -1
var _session := ""
var _command_path := ""
var _status_path := ""
var _command_seq := 0
var _scan_seq := 0
var _capture_enabled := false
var _capture_hwnd := 0
var _capture_pid := 0
var _fps := 5
var _latest_frame := -1
var _last_error := ""
var _status: Dictionary = {}
var _windows: Array = []
var _native_windows: Array = []
var _fallback_windows: Array = []
var _fallback_scan_msec := -100000
var _fallback_error := ""
var _using_fallback := false
var _start_msec := 0


func _ready() -> void:
	if OS.get_name() != "Windows":
		_last_error = "Ekran yakalama yalnızca Windows üzerinde kullanılabilir."
		return

	var root := ProjectSettings.globalize_path("user://capture_bridge")
	if DirAccess.make_dir_recursive_absolute(root) != OK:
		_last_error = "Geçici çalışma klasörü oluşturulamadı."
		return

	_session = root.path_join("run_%d_%d" % [int(Time.get_unix_time_from_system()), Time.get_ticks_usec()])
	if DirAccess.make_dir_recursive_absolute(_session) != OK:
		_last_error = "Windows yardımcı işlem klasörü oluşturulamadı."
		return

	_command_path = _session.path_join("command.json")
	_status_path = _session.path_join("status.json")
	_write_command()
	var helper_script := ProjectSettings.globalize_path("res://tools/capture_helper.ps1")
	if not FileAccess.file_exists(helper_script):
		_last_error = "Windows görüntü yakalama yardımcısı bulunamadı."
		return

	_launch_helper()


func _launch_helper() -> void:
	_start_msec = Time.get_ticks_msec()
	var helper_script := ProjectSettings.globalize_path("res://tools/capture_helper.ps1")
	_helper_pid = OS.create_process(
		"powershell.exe",
		PackedStringArray([
			"-NoLogo", "-NoProfile", "-NonInteractive",
			"-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass",
			"-File", helper_script, "-Session", _session
		]),
		false
	)
	if _helper_pid <= 0:
		_last_error = "Windows PowerShell yardımcısı başlatılamadı. powershell.exe erişimini kontrol edin."


func RestartHelper() -> void:
	if OS.get_name() != "Windows" or _session.is_empty():
		return
	if _helper_pid > 0 and OS.is_process_running(_helper_pid):
		OS.kill(_helper_pid)
	_helper_pid = -1
	_last_error = ""
	_status.clear()
	_windows.clear()
	_native_windows.clear()
	_fallback_windows.clear()
	_fallback_scan_msec = -100000
	_using_fallback = false
	_latest_frame = -1
	_capture_enabled = false
	if FileAccess.file_exists(_status_path):
		DirAccess.remove_absolute(_status_path)
	_write_command()
	_launch_helper()


func GetStatus() -> Dictionary:
	_read_status()
	var alive := _helper_pid > 0 and OS.is_process_running(_helper_pid)
	var helper_ready := bool(_status.get("ready", false))
	var message := ""
	if not _last_error.is_empty() and not _using_fallback:
		message = _last_error
	elif _using_fallback and not _windows.is_empty():
		message = "Yedek tarama: %d pencere bulundu. Canlı görüntü için yardımcı işlem gereklidir." % _windows.size()
	elif not alive:
		message = "Windows yardımcısı kapalı. Yenile / Yeniden dene düğmesine bas."
	elif not helper_ready:
		message = "Windows yardımcısı başlatılıyor; ilk tarama hazırlanıyor..."
	elif int(_status.get("scan_done", -1)) < 0:
		message = "Windows pencereleri taranıyor..."
	elif _windows.is_empty():
		message = "Tarama tamamlandı; uygun pencere bulunamadı. Oyunu pencere modunda aç."
	else:
		message = "%d açık pencere bulundu. Listeden seçim yapabilirsin." % _windows.size()
	return {
		"alive": alive,
		"ready": helper_ready or (_using_fallback and not _windows.is_empty()),
		"scan_done": int(_status.get("scan_done", -1)),
		"count": _windows.size(),
		"message": message
	}


func ListWindows() -> Array:
	_read_status()
	_scan_seq += 1
	_write_command()
	if not _native_windows.is_empty():
		_windows = _native_windows.duplicate(true)
		_using_fallback = false
	else:
		if Time.get_ticks_msec() - _fallback_scan_msec >= 6000:
			_query_windows_directly()
		_windows = _fallback_windows.duplicate(true)
		_using_fallback = not _windows.is_empty()
	return _windows.duplicate(true)


func _query_windows_directly() -> void:
	_fallback_scan_msec = Time.get_ticks_msec()
	_fallback_windows.clear()
	_fallback_error = ""
	if OS.get_name() != "Windows":
		return
	var script_path := ProjectSettings.globalize_path("res://tools/list_windows.ps1")
	if not FileAccess.file_exists(script_path):
		_fallback_error = "Yedek pencere tarama dosyası eksik."
		return
	var output: Array = []
	var exit_code := OS.execute(
		"powershell.exe",
		PackedStringArray([
			"-NoLogo", "-NoProfile", "-NonInteractive",
			"-ExecutionPolicy", "Bypass", "-File", script_path
		]), output, true, false
	)
	if output.is_empty():
		_fallback_error = "PowerShell taramasından yanıt alınamadı (kod %d)." % exit_code
		return
	var full_output: String = str(output[0])
	var marker := "SEKOBOT_WINDOWS="
	var marker_pos := full_output.rfind(marker)
	if marker_pos < 0:
		_fallback_error = "PowerShell taraması başarısız: " + full_output.strip_edges().left(180)
		return
	var json_line := full_output.substr(marker_pos + marker.length()).get_slice("\n", 0).strip_edges()
	var parsed: Variant = JSON.parse_string(json_line)
	if parsed is Array:
		_fallback_windows = parsed
	elif parsed is Dictionary:
		_fallback_windows = [parsed]
	else:
		_fallback_error = "PowerShell pencere listesi çözümlenemedi."


func StartCapture(window_handle: int, expected_pid: int, fps: int) -> void:
	_capture_hwnd = window_handle
	_capture_pid = expected_pid
	_fps = clampi(fps, 1, 15)
	_capture_enabled = true
	_latest_frame = -1
	_last_error = ""
	_write_command()


func StopCapture() -> void:
	_capture_enabled = false
	_write_command()


func CaptureFrame(_window_handle: int, _expected_pid: int) -> Image:
	_read_status()
	if not _last_error.is_empty():
		return null
	if not _capture_enabled:
		return null

	var frame_no := int(_status.get("frame_no", 0))
	if frame_no <= _latest_frame:
		return null

	var relative_path := str(_status.get("frame_file", ""))
	if relative_path.is_empty() or relative_path.get_file() != relative_path:
		return null

	var absolute_path := _session.path_join(relative_path)
	if not FileAccess.file_exists(absolute_path):
		return null

	var frame_bytes := FileAccess.get_file_as_bytes(absolute_path)
	if frame_bytes.is_empty():
		return null

	var frame := Image.new()
	if frame.load_png_from_buffer(frame_bytes) != OK or frame.is_empty():
		return null

	_latest_frame = frame_no
	return frame


func GetLastError() -> String:
	_read_status()
	if not _capture_enabled and _using_fallback and not _windows.is_empty():
		return ""
	if _last_error.is_empty() and not _fallback_error.is_empty() and _windows.is_empty():
		return _fallback_error
	return _last_error


func _read_status() -> void:
	if _status_path.is_empty():
		return
	if FileAccess.file_exists(_status_path):
		var file := FileAccess.open(_status_path, FileAccess.READ)
		if file != null:
			var parsed: Variant = JSON.parse_string(file.get_as_text())
			file.close()
			if parsed is Dictionary:
				_status = parsed
				_last_error = str(_status.get("error", ""))
				var reported_windows: Variant = _status.get("windows", [])
				if reported_windows is Array:
					_native_windows = reported_windows
	if _helper_pid > 0 and Time.get_ticks_msec() - _start_msec > 7000:
		if not OS.is_process_running(_helper_pid):
			if _last_error.is_empty():
				_last_error = "Windows yardımcısı kapandı. Windows güvenlik ayarlarını ve PowerShell erişimini kontrol edin."
	if _helper_pid > 0 and Time.get_ticks_msec() - _start_msec > 16000:
		if _status.is_empty() and _last_error.is_empty():
			_last_error = "Windows yardımcısından 16 saniyedir yanıt alınamadı."


func _write_command() -> void:
	if _command_path.is_empty():
		return
	_command_seq += 1
	var command := {
		"seq": _command_seq,
		"scan": _scan_seq,
		"capture": _capture_enabled,
		"hwnd": _capture_hwnd,
		"pid": _capture_pid,
		"fps": _fps,
		"quit": false
	}
	var temp_path := _command_path + ".new"
	var file := FileAccess.open(temp_path, FileAccess.WRITE)
	if file == null:
		_last_error = "Windows yardımcı işlemine komut gönderilemedi."
		return
	file.store_string(JSON.stringify(command))
	file.close()
	if FileAccess.file_exists(_command_path):
		DirAccess.remove_absolute(_command_path)
	if DirAccess.rename_absolute(temp_path, _command_path) != OK:
		_last_error = "Windows yardımcı işlem komut dosyası değiştirilemedi."


func _exit_tree() -> void:
	if _helper_pid > 0 and OS.is_process_running(_helper_pid):
		_capture_enabled = false
		OS.kill(_helper_pid)
