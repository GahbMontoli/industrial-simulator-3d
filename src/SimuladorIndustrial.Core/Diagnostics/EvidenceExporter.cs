using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Diagnostics;

public static class EvidenceExporter
{
    public const int CurrentSchemaVersion = 2;
    public const int CurrentTelemetryManifestSchemaVersion = 1;

    private const string TelemetryHeader =
        "schema_version,run_id,scene_id,tick,observed_at_utc,tag_id,canonical_name,data_type,direction,raw_value,effective_value,quality,fault,is_forced,last_changed_tick,last_changed_at_utc";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static SimulationEvidence Capture(
        SceneDocument scene,
        SimulationEngine engine,
        TagRegistry tags,
        EventJournal journal,
        DateTimeOffset? exportedAtUtc = null,
        string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(journal);
        SceneValidator.ValidateAndThrow(scene);

        var timestamp = (exportedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var resolvedRunId = runId is null
            ? CreateRunId(timestamp)
            : ValidateRunId(runId);
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
            resolvedRunId,
            timestamp,
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

    public static string CreateRunId(DateTimeOffset? startedAtUtc = null)
    {
        var timestamp = (startedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return $"simulation-{timestamp:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
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

    public static async ValueTask WriteTagTelemetryCsvAsync(
        Stream destination,
        SimulationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable(destination);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Telemetry schema version {evidence.SchemaVersion} is not supported; expected {CurrentSchemaVersion}.");
        }

        var runId = ValidateRunId(evidence.RunId);
        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1_024,
            leaveOpen: true);
        await writer.WriteLineAsync(TelemetryHeader)
            .ConfigureAwait(false);

        foreach (var tag in evidence.Tags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = string.Join(
                ',',
                evidence.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(runId),
                evidence.SceneId.ToString("D"),
                evidence.Runtime.Tick.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(evidence.ExportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                tag.Id.ToString("D"),
                EscapeCsv(tag.CanonicalName),
                FormatEnum(tag.DataType),
                FormatEnum(tag.Direction),
                EscapeCsv(tag.RawValue),
                EscapeCsv(tag.EffectiveValue),
                FormatEnum(tag.Quality),
                FormatEnum(tag.Fault),
                tag.IsForced.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                tag.LastChangedTick.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(tag.LastChangedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            await writer.WriteLineAsync(row).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask WriteTagTelemetryManifestAsync(
        Stream destination,
        SimulationEvidence evidence,
        string telemetryPath,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable(destination);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(telemetryPath);
        if (evidence.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Telemetry schema version {evidence.SchemaVersion} is not supported; expected {CurrentSchemaVersion}.");
        }

        var fullTelemetryPath = Path.GetFullPath(telemetryPath);
        if (!File.Exists(fullTelemetryPath))
        {
            throw new FileNotFoundException(
                "The telemetry snapshot must exist before its manifest is written.",
                fullTelemetryPath);
        }

        ValidateTelemetrySnapshotFile(fullTelemetryPath, evidence);

        string sha256;
        await using (var source = new FileStream(
                         fullTelemetryPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 81_920,
                         useAsync: true))
        {
            sha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false))
                .ToLowerInvariant();
        }

        var file = new FileInfo(fullTelemetryPath);
        var manifest = new TagTelemetryExportManifest(
            CurrentTelemetryManifestSchemaVersion,
            CurrentSchemaVersion,
            "industrial-simulator-3d",
            "synthetic",
            "Snapshot temporal do software; não é histórico contínuo nem evidência de equipamento real.",
            ValidateRunId(evidence.RunId),
            evidence.SceneId,
            evidence.Runtime.Tick,
            evidence.ExportedAtUtc.ToUniversalTime(),
            file.Name,
            evidence.Tags.Count,
            file.Length,
            sha256);

        await JsonSerializer.SerializeAsync(destination, manifest, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateTelemetrySnapshotFile(
        string telemetryPath,
        SimulationEvidence evidence)
    {
        using var reader = new StreamReader(telemetryPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine()?.TrimStart('\uFEFF');
        if (!string.Equals(header, TelemetryHeader, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The telemetry snapshot header does not match contract v2.");
        }

        var expectedPrefix = string.Join(
            ',',
            evidence.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            ValidateRunId(evidence.RunId),
            evidence.SceneId.ToString("D"),
            evidence.Runtime.Tick.ToString(CultureInfo.InvariantCulture),
            evidence.ExportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) + ',';
        var rows = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The telemetry snapshot provenance does not match the manifest evidence.");
            }

            rows++;
        }

        if (rows != evidence.Tags.Count)
        {
            throw new InvalidDataException(
                "The telemetry snapshot row count does not match the manifest evidence.");
        }
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

    private static string FormatEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string ValidateRunId(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var normalized = runId.Trim();
        if (normalized.Length > 128 ||
            normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
        {
            throw new ArgumentException(
                "Run id must contain at most 128 ASCII letters, digits, '.', ':', '_' or '-'.",
                nameof(runId));
        }

        return normalized;
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
