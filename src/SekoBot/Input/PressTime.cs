namespace SekoBot.Input;

internal static class PressTime
{
    // Keep a profile's calibrated duration close to its setting, including zero.
    internal static int Vary(int milliseconds)
    {
        int spread = Math.Min(80, milliseconds / 10);
        return milliseconds + Random.Shared.Next(-spread, spread + 1);
    }

    internal static int Tap() => Random.Shared.Next(90, 151);
    internal static int Button(bool fast) => fast ? Random.Shared.Next(120, 181) : Random.Shared.Next(250, 341);

    internal static async Task HoldAsync(Action<bool> press, int milliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        press(true);
        try { await Task.Delay(milliseconds, token); }
        finally { press(false); }
    }
}
