using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Diagnostics;

public static class EvidenceExporter
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static SimulationEvidence Capture(
        SceneDocument scene,
        SimulationEngine engine,
        TagRegistry tags,
        EventJournal journal,
        DateTimeOffset? exportedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(journal);
        SceneValidator.ValidateAndThrow(scene);

        var tagEvidence = tags.GetAll()
            .Select(snapshot => new TagEvidence(
                snapshot.Definition.Id,
                snapshot.Definition.Name,
                snapshot.Definition.CanonicalName,
                snapshot.Definition.DataType,
                snapshot.Definition.Direction,
                snapshot.Definition.Address,
                snapshot.RawValue.ToString(),
                snapshot.EffectiveValue.ToString(),
                snapshot.Quality,
                snapshot.IsForced,
                snapshot.ForcedValue?.ToString(),
                snapshot.Fault,
                snapshot.LastChangedTick,
                snapshot.LastChangedAtUtc))
            .ToArray();

        var metrics = engine.Metrics;
        return new SimulationEvidence(
            CurrentSchemaVersion,
            (exportedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            scene.SceneId,
            scene.Name,
            new RuntimeEvidence(
                engine.State,
                engine.Clock.Tick,
                engine.Clock.Step.TotalMilliseconds,
                engine.Clock.DroppedStepCount,
                journal.Count,
                metrics.ExecutedSteps,
                metrics.LastLogicStepMilliseconds,
                metrics.AverageLogicStepMilliseconds,
                metrics.MaximumLogicStepMilliseconds),
            tagEvidence,
            journal.Events);
    }

    public static async ValueTask WriteJsonAsync(
        Stream destination,
        SimulationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable(destination);
        ArgumentNullException.ThrowIfNull(evidence);

        await JsonSerializer.SerializeAsync(destination, evidence, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask WriteEventsCsvAsync(
        Stream destination,
        SimulationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable(destination);
        ArgumentNullException.ThrowIfNull(evidence);

        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1_024,
            leaveOpen: true);
        await writer.WriteLineAsync("sequence,tick,occurredAtUtc,category,message,tagId")
            .ConfigureAwait(false);

        foreach (var simulationEvent in evidence.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = string.Join(
                ',',
                simulationEvent.Sequence.ToString(CultureInfo.InvariantCulture),
                simulationEvent.Tick.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(simulationEvent.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                EscapeCsv(simulationEvent.Category),
                EscapeCsv(simulationEvent.Message),
                EscapeCsv(simulationEvent.TagId?.ToString("D") ?? string.Empty));
            await writer.WriteLineAsync(row).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureWritable(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }
    }

    private static string EscapeCsv(string value)
    {
        if (!value.ContainsAny([',', '"', '\r', '\n']))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
