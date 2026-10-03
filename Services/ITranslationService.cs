namespace SocketMobileGroceryScanner.Services;

public interface ITranslationService
{
    /// <summary>
    /// Translates <paramref name="text"/> from <paramref name="sourceLang"/> into Dutch.
    /// Pass null/empty for <paramref name="sourceLang"/> when the source language is
    /// unknown (e.g. manually-typed product names) — the language will be auto-detected.
    /// Returns null if translation wasn't possible (service down, quota exceeded, text was
    /// already Dutch, etc.) — callers should keep the original text in that case.
    /// </summary>
    Task<string?> TranslateToDutchAsync(string text, string? sourceLang, CancellationToken ct = default);
}
