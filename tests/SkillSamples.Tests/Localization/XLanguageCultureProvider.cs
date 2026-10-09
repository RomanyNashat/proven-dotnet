using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace SkillSamples.Localization;

// The header picks the UI culture (text) only. The formatting culture is en-US for every request.
public sealed class XLanguageCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var ui = LanguageHeader.Parse(httpContext.Request.Headers[LanguageHeader.Name]) == LanguageHeader.Arabic ? "ar-SA" : "en-US";
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture: "en-US", uiCulture: ui));
    }
}
