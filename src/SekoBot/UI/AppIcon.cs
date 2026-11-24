namespace SekoBot.UI;

/// <summary>PARDUS's icon (the gold, leopard-spotted P), taken from the program file itself.</summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon?> Shared = new(() =>
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return null; }
    });

    public static void Use(Form form)
    {
        if (Shared.Value is { } icon)
            form.Icon = icon;
    }
}
