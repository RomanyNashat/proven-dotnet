using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace SkillSamples.Localization;

public interface ITranslationSource
{
    Task<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken cancellationToken);
}

// i18n/{language}.json: { "Errors.AppointmentNotFound": "Appointment {0} was not found." }
public sealed class FileTranslationSource(IHostEnvironment environment) : ITranslationSource
{
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(string language, CancellationToken cancellationToken)
    {
        var path = Path.Combine(environment.ContentRootPath, "i18n", $"{language}.json");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
               ?? new Dictionary<string, string>();
    }
}

// Each language loads once and stays in memory. Missing Arabic → English; missing key → the key itself.
public sealed class TranslationCatalog(ITranslationSource source)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, string>>>> _languages = new();

    public async Task<string> GetAsync(string key, string language, CancellationToken cancellationToken = default)
    {
        var texts = await _languages
            .GetOrAdd(language, lang => new Lazy<Task<IReadOnlyDictionary<string, string>>>(() => source.LoadAsync(lang, CancellationToken.None)))
            .Value;
        if (texts.TryGetValue(key, out var text)) return text;
        return language == LanguageHeader.English ? key : await GetAsync(key, LanguageHeader.English, cancellationToken);
    }
}

public sealed class RequestLanguage(IHttpContextAccessor accessor)
{
    public string Value => LanguageHeader.Parse(accessor.HttpContext?.Request.Headers[LanguageHeader.Name]);
}
