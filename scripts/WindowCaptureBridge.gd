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

	_start_msec = Time.get_ticks_msec()
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
		_last_error = "Windows PowerShell yardımcı işlemi başlatılamadı."


func ListWindows() -> Array:
	_read_status()
	_scan_seq += 1
	_write_command()
	return _windows.duplicate(true)


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
	return _last_error


func _read_status() -> void:
	if _status_path.is_empty():
		return
	if not FileAccess.file_exists(_status_path):
		if _helper_pid > 0 and Time.get_ticks_msec() - _start_msec > 10000:
			if not OS.is_process_running(_helper_pid):
				_last_error = "Windows görüntü yakalama yardımcısı beklenmedik şekilde kapandı."
		return

	var file := FileAccess.open(_status_path, FileAccess.READ)
	if file == null:
		return
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	file.close()
	if not (parsed is Dictionary):
		return

	_status = parsed
	_last_error = str(_status.get("error", ""))
	var reported_windows: Variant = _status.get("windows", [])
	if reported_windows is Array:
		_windows = reported_windows


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
