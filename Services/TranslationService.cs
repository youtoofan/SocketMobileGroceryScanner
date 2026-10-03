using System.Text.Json;
using System.Web;

namespace SocketMobileGroceryScanner.Services;

/// <summary>
/// Free, keyless translation via the MyMemory Translation API
/// (https://mymemory.translated.net). No cost, but subject to a daily rate
/// limit and the text is sent over plain HTTP to a third-party service.
/// </summary>
public class TranslationService : ITranslationService
{
    private readonly HttpClient _http;

    public TranslationService(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("TranslateApi");
    }

    public async Task<string?> TranslateToDutchAsync(string text, string? sourceLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = NormalizeLangCode(sourceLang);
        if (normalized == "nl") return null; // already known to be Dutch, nothing to do

        try
        {
            var query = HttpUtility.UrlEncode(text);
            var url = $"https://api.mymemory.translated.net/get?q={query}&langpair={normalized}|nl";

            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // MyMemory signals quota/errors via responseStatus (not always a valid HTTP
            // status) and sometimes embeds a warning string instead of real text.
            if (root.TryGetProperty("responseStatus", out var status))
            {
                var code = status.ValueKind == JsonValueKind.Number ? status.GetInt32() : 200;
                if (code != 200) return null;
            }

            var responseData = root.GetProperty("responseData");

            // When the source language was unknown we asked MyMemory to auto-detect it;
            // if it detected the text was already Dutch, there's nothing to change.
            if (normalized == "autodetect" &&
                responseData.TryGetProperty("detectedLanguage", out var detected) &&
                detected.ValueKind == JsonValueKind.String &&
                string.Equals(detected.GetString(), "nl", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var translated = responseData.GetProperty("translatedText").GetString();
            if (string.IsNullOrWhiteSpace(translated)) return null;
            if (translated.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(translated, text, StringComparison.OrdinalIgnoreCase)) return null;

            return translated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Translate] {ex.Message}");
            return null;
        }
    }

    // OpenFoodFacts/UpcItemDb language codes are already simple 2-letter codes,
    // but guard against unexpected formats (e.g. "en-US"). An unknown/missing
    // source language (e.g. manually-typed product names) falls back to asking
    // MyMemory to auto-detect it rather than wrongly assuming English.
    private static string NormalizeLangCode(string? lang) =>
        string.IsNullOrWhiteSpace(lang) ? "autodetect" : lang.Trim().ToLowerInvariant().Split('-', '_')[0];
}
