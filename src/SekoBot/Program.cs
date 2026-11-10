namespace SekoBot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Instance.Claim();
        Application.Run(new UI.MainForm());
    }
}
