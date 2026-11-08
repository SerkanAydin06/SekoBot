using System.Net.Http.Json;
using System.Text.Json;

namespace SekoBot.Update;

/// <summary>
/// Sends messages to the user's own Telegram bot (made with @BotFather): the bot's token and the
/// user's chat number are set in the "Bildirim" tab. Only api.telegram.org is contacted.
/// </summary>
internal static class TelegramNotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static string Url(string token, string method) => $"https://api.telegram.org/bot{token.Trim()}/{method}";

    /// <returns>Null when sent, else what went wrong (Turkish, for the log).</returns>
    public static async Task<string?> SendAsync(string token, string chatId, string text)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            return "Telegram ayarları eksik (anahtar ve sohbet no).";
        try
        {
            using var response = await Http.PostAsJsonAsync(Url(token, "sendMessage"),
                new { chat_id = chatId.Trim(), text });
            if (response.IsSuccessStatusCode)
                return null;
            return $"Telegram mesajı gönderilemedi ({(int)response.StatusCode}). Anahtarı ve sohbet numarasını kontrol et.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Telegram'a ulaşılamadı: " + ex.Message;
        }
    }

    /// <summary>The chat of the last message sent to the bot (the user writes to it once).</summary>
    public static async Task<(string? ChatId, string? Error)> FindChatAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, "Önce bot anahtarını (token) yapıştır.");
        try
        {
            using var response = await Http.GetAsync(Url(token, "getUpdates"));
            if (!response.IsSuccessStatusCode)
                return (null, $"Anahtar kabul edilmedi ({(int)response.StatusCode}). @BotFather'ın verdiği anahtarı tam yapıştır.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string? chat = null;
            foreach (var update in json.RootElement.GetProperty("result").EnumerateArray())
                if (update.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("chat", out var c) && c.TryGetProperty("id", out var id))
                    chat = id.GetRawText();
            return chat == null
                ? (null, "Sohbet bulunamadı: Telegram'da kendi botuna bir mesaj (ör. \"merhaba\") yaz, sonra tekrar bas.")
                : (chat, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (null, "Telegram'a ulaşılamadı: " + ex.Message);
        }
    }
}
