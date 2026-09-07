using System.Text.Json;
using System.Text.Json.Serialization;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Serialization;

public static class ArcJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new DealerUrnConverter());
        options.Converters.Add(new CycleIdConverter());
        options.Converters.Add(new CorrelationIdConverter());
        options.Converters.Add(new MoneyConverter());
        options.Converters.Add(new GateDecisionConverter());
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}.");
}

internal sealed class DealerUrnConverter : JsonConverter<DealerUrn>
{
    public override DealerUrn Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("DealerUrn is null."));

    public override void Write(Utf8JsonWriter writer, DealerUrn value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class CycleIdConverter : JsonConverter<CycleId>
{
    public override CycleId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("CycleId is null."));

    public override void Write(Utf8JsonWriter writer, CycleId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class CorrelationIdConverter : JsonConverter<CorrelationId>
{
    public override CorrelationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("CorrelationId is null."));

    public override void Write(Utf8JsonWriter writer, CorrelationId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class MoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new Money(reader.GetDecimal());

        using var doc = JsonDocument.ParseValue(ref reader);
        var amount = doc.RootElement.GetProperty("amount").GetDecimal();
        var currency = doc.RootElement.TryGetProperty("currency", out var c) ? c.GetString() ?? "INR" : "INR";
        return new Money(amount, currency);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}

internal sealed class GateDecisionConverter : JsonConverter<GateDecision>
{
    public override GateDecision Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var e = doc.RootElement;
        return GateDecision.Create(
            Enum.Parse<GateId>(e.GetProperty("gate").GetString()!, ignoreCase: true),
            e.GetProperty("actorUpn").GetString()!,
            Enum.Parse<ActorRole>(e.GetProperty("actorRole").GetString()!, ignoreCase: true),
            Enum.Parse<GateDecisionStatus>(e.GetProperty("decision").GetString()!, ignoreCase: true),
            e.GetProperty("reason").GetString()!,
            new CorrelationId(e.GetProperty("correlationId").GetString()!),
            e.TryGetProperty("recommendedAction", out var rec) ? rec.GetString() : null,
            e.TryGetProperty("decidedUtc", out var ts) ? ts.GetDateTimeOffset() : null);
    }

    public override void Write(Utf8JsonWriter writer, GateDecision value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("gate", value.Gate.ToString());
        writer.WriteString("actorUpn", value.ActorUpn);
        writer.WriteString("actorRole", value.ActorRole.ToString());
        writer.WriteString("decision", value.Decision.ToString());
        writer.WriteString("reason", value.Reason);
        if (value.RecommendedAction is not null)
            writer.WriteString("recommendedAction", value.RecommendedAction);
        writer.WriteString("decidedUtc", value.DecidedUtc);
        writer.WriteString("correlationId", value.CorrelationId.Value);
        writer.WriteEndObject();
    }
}
