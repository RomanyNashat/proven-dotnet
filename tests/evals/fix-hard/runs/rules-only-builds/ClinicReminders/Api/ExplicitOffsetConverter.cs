using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicReminders.Api;

/// <summary>
/// Requires a DateTimeOffset in a request to carry its offset ("Z" or "+03:00"). Without one, System.Text.Json
/// assumes the server's local offset: UTC in a container, so "10:00" meant for Riyadh would be stored three hours
/// late and the reminder would name the wrong time.
/// </summary>
public sealed class ExplicitOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !reader.TryGetDateTime(out var asDateTime)
            || asDateTime.Kind == DateTimeKind.Unspecified
            || !reader.TryGetDateTimeOffset(out var value))
        {
            throw new JsonException("Expected an ISO 8601 date and time with an offset, such as 2026-11-02T10:30:00+03:00.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
