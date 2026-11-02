using SekoBot.Native;

namespace SekoBot.Input;

/// <summary>System-wide hotkeys that work while the game has focus.</summary>
internal sealed class GlobalHotkeys : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public GlobalHotkeys() => CreateHandle(new CreateParams());

    public bool Register(Keys key, Action action)
    {
        int id = _nextId++;
        if (!Win32.RegisterHotKey(Handle, id, Win32.MOD_NOREPEAT, (uint)key))
            return false;
        _actions[id] = action;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
            action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (int id in _actions.Keys)
            Win32.UnregisterHotKey(Handle, id);
        _actions.Clear();
        DestroyHandle();
    }
}
