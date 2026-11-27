using SekoBot.Bot;
using SekoBot.Update;

namespace SekoBot.UI;

internal sealed partial class MainForm
{
    private readonly Guid _fleetId = Guid.NewGuid();
    private static readonly (string Title, string Action)[] FleetActions =
    [
        ("Ekran görüntüsü al", "ekran"), ("Durum", "durum"), ("Başlat", "basla"),
        ("Durdur", "dur"), ("Bu PC'deki tüm SekoBot'ları güncelle", "guncelle")
    ];

    private static bool FleetError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private async Task FleetMenuAsync(string folder, string token, string chat, long id, string text, string replyTo)
    {
        string message;
        string[][]? buttons = null;
        try
        {
            var fleet = await Task.Run(() => new TelegramFleet(folder, token, chat));
            var online = await Task.Run(fleet.Online);
            var target = online.FirstOrDefault(n => n.Button == text && online.Count(other => other.Button == text) == 1);
            if (WhisperSender.WhisperFrom(replyTo) is string sender)
            {
                string? character = WhisperInbox.CharacterOf(replyTo);
                var matches = online.Where(n => n.Character == character && !string.IsNullOrEmpty(character)).ToArray();
                if (matches.Length == 1)
                {
                    var node = matches[0];
                    await Task.Run(() => fleet.Send(new(node.Id, node.Character, "whisper", id, DateTimeOffset.UtcNow, sender + "\n" + text)));
                    message = $"{node.Label} için yanıt iletildi.";
                }
                else message = "Fısıltının karakteri tek bir açık botla eşleşmedi.";
            }
            else if (target != null)
            {
                message = $"{target.Label} için işlem seç.";
                buttons = FleetActions.Select(a => new[] { a.Title + " · " + target.Button })
                    .Append(["/botlar"]).ToArray();
            }
            else
            {
                var choice = (from n in online from a in FleetActions
                              where text == a.Title + " · " + n.Button && online.Count(other => other.Button == n.Button) == 1 select (Node: n, a.Action)).SingleOrDefault();
                if (choice.Node != null)
                {
                    await Task.Run(() => fleet.Send(new(choice.Node.Id, choice.Node.Character, choice.Action, id, DateTimeOffset.UtcNow)));
                    message = $"{choice.Node.Label} için istek iletildi.";
                }
                else
                {
                    message = online.Length == 0 ? "Çevrimiçi SekoBot görünmüyor. /botlar ile yenile."
                        : "İşlem yapmak istediğin PC ve karakteri seç.";
                    buttons = online.Select(n => new[] { n.Button }).Append(["/botlar"]).ToArray();
                }
            }
        }
        catch (Exception ex) when (FleetError(ex)) { message = "Ortak klasöre erişilemedi: " + ex.Message; }
        if (TelegramUpdateAllowed(token, chat) && _profile.TelegramFleetFolder == folder && _profile.TelegramFleetHub
            && await TelegramNotifier.SendAsync(token, chat, message, buttons) is string error) WriteLog(error);
    }

    private async Task TelegramFleetWorkerAsync(CancellationToken cancel)
    {
        long lastCommand = -1;
        DateTime nextHeartbeat = DateTime.MinValue, nextPrune = DateTime.MinValue;
        string? previousError = null;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                string folder = _profile.TelegramFleetFolder, token = _profile.TelegramToken, chat = _profile.TelegramChatId.Trim();
                if (folder.Length > 0 && TelegramUpdateAllowed(token, chat))
                {
                    string character = _engine?.CharacterName ?? "";
                    var node = new TelegramFleet.Node(_fleetId, Environment.MachineName, Instance.Number, character, DateTimeOffset.UtcNow);
                    try
                    {
                        var commands = await Task.Run(() =>
                        {
                            var fleet = new TelegramFleet(folder, token, chat);
                            if (DateTime.UtcNow >= nextHeartbeat)
                            {
                                fleet.Announce(node);
                                nextHeartbeat = DateTime.UtcNow.AddSeconds(6);
                            }
                            if (DateTime.UtcNow >= nextPrune)
                            {
                                fleet.Prune();
                                nextPrune = DateTime.UtcNow.AddHours(1);
                            }
                            return fleet.Take(_fleetId);
                        }, cancel);
                        previousError = null;
                        foreach (var cmd in commands)
                        {
                            if (cmd.Update <= lastCommand || cancel.IsCancellationRequested || folder != _profile.TelegramFleetFolder
                                || !TelegramUpdateAllowed(token, chat)) continue;
                            lastCommand = cmd.Update;
                            string? result;
                            if (cmd.Character != (_engine?.CharacterName ?? ""))
                                result = "Bağlı karakter değişti. /botlar ile yeniden seç.";
                            else if (cmd.Action == "ekran") result = await TelegramScreenshotAsync(token, chat, cancel, node.Label);
                            else if (cmd.Action == "durum") result = TelegramStatus();
                            else if (cmd.Action is "basla" or "dur") result = TelegramCommand(cmd.Action);
                            else if (cmd.Action == "whisper" && cmd.Text.Split('\n', 2) is [var recipient, var body])
                                result = _engine?.QueueWhisper(recipient, body) ?? "Oyun bağlı değil.";
                            else if (cmd.Action == "guncelle")
                            {
                                if (_updating || _updatingAll || _checkingUpdate) result = "Güncelleme zaten sürüyor.";
                                else
                                {
                                    WindowLayout.TellAll(WindowLayout.AllUpdate, Handle);
                                    await UpdateAndResumeAsync(chat);
                                    result = null;
                                }
                            }
                            else continue;
                            if (result != null && TelegramUpdateAllowed(token, chat))
                                await UpdateNoticeAsync(chat, node.Label + "\n" + result);
                        }
                    }
                    catch (Exception ex) when (FleetError(ex))
                    {
                        if (previousError != ex.Message) WriteLog("Telegram ortak klasör: " + ex.Message);
                        previousError = ex.Message;
                    }
                }
                await Task.Delay(2000, cancel);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
    }
}
