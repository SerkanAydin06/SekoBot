namespace SekoBot.Input;

/// <summary>
/// Which SekoBots are doing a job with the real mouse now (throwing items away, selling, reviving,
/// writing a message): each marks itself with a named event (Local\SekoBot.MouseBusy-[number]) from
/// the job's start until a few seconds after its last mouse action. The market scan, which opens
/// stalls all the time with short cursor moves, waits while another SekoBot is marked, so it never
/// gets in the way of that job.
/// </summary>
internal static class MouseUse
{
    private const int Max = 8;
    /// <summary>A job counts as still going this long after its last mouse action (the next click
    /// of the same job usually follows within a second or two).</summary>
    internal static readonly TimeSpan Linger = TimeSpan.FromSeconds(4);

    private static readonly object Gate = new();
    private static EventWaitHandle? _mark;
    private static int _holds;
    private static long _endsAt;

    private static EventWaitHandle Mark()
    {
        lock (Gate)
            return _mark ??= new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\SekoBot.MouseBusy-{Instance.Number}");
    }

    /// <summary>A job with the mouse begins (until <see cref="End"/>).</summary>
    public static void Begin()
    {
        lock (Gate)
        {
            _holds++;
            Mark().Set();
        }
    }

    /// <summary>The job ends; the mark goes a few seconds later unless another one begins.</summary>
    public static void End()
    {
        long ends;
        lock (Gate)
        {
            _holds = Math.Max(0, _holds - 1);
            ends = _endsAt = Environment.TickCount64 + (long)Linger.TotalMilliseconds;
        }
        _ = Task.Delay(Linger).ContinueWith(_ =>
        {
            lock (Gate)
                if (_holds == 0 && _endsAt == ends)
                    Mark().Reset();
        }, TaskScheduler.Default);
    }

    /// <summary>The numbers of the other SekoBots doing a job with the mouse now (empty: none).</summary>
    public static List<int> Others()
    {
        var busy = new List<int>();
        for (int n = 1; n <= Max; n++)
        {
            if (n == Instance.Number || !EventWaitHandle.TryOpenExisting($@"Local\SekoBot.MouseBusy-{n}", out var mark))
                continue;
            using (mark)
                if (mark.WaitOne(0))
                    busy.Add(n);
        }
        return busy;
    }
}
