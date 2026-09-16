using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Deterministic, culture-invariant JSON for Segment payloads.</summary>
internal static class SegmentJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new CombatScaledAmountConverter());
        options.Converters.Add(new DamageTypeConverter());
        options.Converters.Add(new ReadOnlyListConverterFactory());
        options.Converters.Add(new GameplaySessionIdConverter());
        options.Converters.Add(new CharacterRecordIdConverter());
        options.Converters.Add(new MonitoringContextIdConverter());
        options.Converters.Add(new MetricConverterFactory());
        options.Converters.Add(new MetricRefConverterFactory());
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"Segment JSON deserialized to null {typeof(T).Name}.");
}

internal sealed class CombatScaledAmountConverter : JsonConverter<CombatScaledAmount>
{
    public override CombatScaledAmount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return new CombatScaledAmount(reader.GetInt64());
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("CombatScaledAmount must be an object with hundredths.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty("hundredths", out var hundredths))
        {
            throw new JsonException("CombatScaledAmount is missing hundredths.");
        }

        return new CombatScaledAmount(hundredths.GetInt64());
    }

    public override void Write(Utf8JsonWriter writer, CombatScaledAmount value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("hundredths", value.Hundredths);
        writer.WriteEndObject();
    }
}

internal sealed class DamageTypeConverter : JsonConverter<DamageType>
{
    public override DamageType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var text = document.RootElement.GetProperty("text").GetString() ?? string.Empty;
        var unresistable = document.RootElement.TryGetProperty("isUnresistable", out var unresistableElement)
            && unresistableElement.GetBoolean();
        var unique = document.RootElement.TryGetProperty("isUnique", out var uniqueElement)
            && uniqueElement.GetBoolean();
        return new DamageType(text, unresistable, unique);
    }

    public override void Write(Utf8JsonWriter writer, DamageType value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("text", value.Text);
        writer.WriteBoolean("isUnresistable", value.IsUnresistable);
        writer.WriteBoolean("isUnique", value.IsUnique);
        writer.WriteEndObject();
    }
}

internal sealed class ReadOnlyListConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(ReadOnlyListConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class ReadOnlyListConverter<T> : JsonConverter<IReadOnlyList<T>>
{
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [];

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.ToArray(), options);
}

internal sealed class GameplaySessionIdConverter : JsonConverter<GameplaySessionId>
{
    public override GameplaySessionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        GameplaySessionId.FromGuid(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, GameplaySessionId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class CharacterRecordIdConverter : JsonConverter<CharacterRecordId>
{
    public override CharacterRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        CharacterRecordId.FromGuid(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, CharacterRecordId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class MonitoringContextIdConverter : JsonConverter<MonitoringContextId>
{
    public override MonitoringContextId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        MonitoringContextId.FromGuid(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, MonitoringContextId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class MetricConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Metric<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(MetricConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class MetricConverter<T> : JsonConverter<Metric<T>> where T : struct
{
    public override Metric<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var availability = root.GetProperty("availability").Deserialize<MetricAvailability>(options);
        var evidence = root.TryGetProperty("evidence", out var evidenceElement)
            ? evidenceElement.Deserialize<MetricEvidence>(options)
            : MetricEvidence.None;
        MetricConfidence? confidence = root.TryGetProperty("confidence", out var confidenceElement)
            ? confidenceElement.Deserialize<MetricConfidence>(options)
            : null;
        RateDenominatorKind? denominator = root.TryGetProperty("denominator", out var denominatorElement)
            ? denominatorElement.Deserialize<RateDenominatorKind>(options)
            : null;
        CoverageInfo? coverage = root.TryGetProperty("coverage", out var coverageElement)
            ? coverageElement.Deserialize<CoverageInfo>(options)
            : null;
        T? value = root.TryGetProperty("value", out var valueElement)
            ? valueElement.Deserialize<T>(options)
            : null;

        return availability switch
        {
            MetricAvailability.Available when value is { } present =>
                Metric<T>.Available(present, evidence, confidence, denominator, coverage),
            MetricAvailability.Incomplete when value is { } present =>
                Metric<T>.Incomplete(present, evidence, coverage, confidence),
            MetricAvailability.Unsupported => Metric<T>.Unsupported(),
            _ => Metric<T>.NotCaptured()
        };
    }

    public override void Write(Utf8JsonWriter writer, Metric<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("availability");
        JsonSerializer.Serialize(writer, value.Availability, options);
        writer.WritePropertyName("evidence");
        JsonSerializer.Serialize(writer, value.Evidence, options);
        if (value.Confidence is { } confidence)
        {
            writer.WritePropertyName("confidence");
            JsonSerializer.Serialize(writer, confidence, options);
        }

        if (value.Denominator is { } denominator)
        {
            writer.WritePropertyName("denominator");
            JsonSerializer.Serialize(writer, denominator, options);
        }

        if (value.Coverage is { } coverage)
        {
            writer.WritePropertyName("coverage");
            JsonSerializer.Serialize(writer, coverage, options);
        }

        if (value.Value is { } present)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, present, options);
        }

        writer.WriteEndObject();
    }
}

internal sealed class MetricRefConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(MetricRef<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(MetricRefConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class MetricRefConverter<T> : JsonConverter<MetricRef<T>> where T : class
{
    public override MetricRef<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var availability = root.GetProperty("availability").Deserialize<MetricAvailability>(options);
        var evidence = root.TryGetProperty("evidence", out var evidenceElement)
            ? evidenceElement.Deserialize<MetricEvidence>(options)
            : MetricEvidence.None;
        MetricConfidence? confidence = root.TryGetProperty("confidence", out var confidenceElement)
            ? confidenceElement.Deserialize<MetricConfidence>(options)
            : null;
        CoverageInfo? coverage = root.TryGetProperty("coverage", out var coverageElement)
            ? coverageElement.Deserialize<CoverageInfo>(options)
            : null;
        var value = root.TryGetProperty("value", out var valueElement)
            ? valueElement.Deserialize<T>(options)
            : null;

        return availability switch
        {
            MetricAvailability.Available when value is not null =>
                MetricRef<T>.Available(value, evidence, confidence, coverage),
            MetricAvailability.Incomplete when value is not null && coverage is { } limited =>
                MetricRef<T>.Incomplete(value, limited),
            MetricAvailability.Unsupported => MetricRef<T>.Unsupported(),
            _ => MetricRef<T>.NotCaptured()
        };
    }

    public override void Write(Utf8JsonWriter writer, MetricRef<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("availability");
        JsonSerializer.Serialize(writer, value.Availability, options);
        writer.WritePropertyName("evidence");
        JsonSerializer.Serialize(writer, value.Evidence, options);
        if (value.Confidence is { } confidence)
        {
            writer.WritePropertyName("confidence");
            JsonSerializer.Serialize(writer, confidence, options);
        }

        if (value.Coverage is { } coverage)
        {
            writer.WritePropertyName("coverage");
            JsonSerializer.Serialize(writer, coverage, options);
        }

        if (value.Value is { } present)
        {
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, present, options);
        }

        writer.WriteEndObject();
    }
}
