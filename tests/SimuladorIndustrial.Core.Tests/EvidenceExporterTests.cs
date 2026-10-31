using System.Text;
using System.Text.Json;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class EvidenceExporterTests
{
    [Fact]
    public async Task CapturesRuntimeTagsAndEventsAsVersionedJson()
    {
        var scene = LoadScene();
        var journal = new EventJournal();
        var tags = RegisterSceneTags(scene, journal);
        var engine = new SimulationEngine(tags, journal);
        var firstInput = scene.Tags.First(tag => tag.Direction == TagDirection.PlcInput);
        tags.PublishFromSimulation(firstInput.Id, TagValue.FromBool(true), 1);
        engine.Run();

        var evidence = EvidenceExporter.Capture(
            scene,
            engine,
            tags,
            journal,
            DateTimeOffset.Parse("2026-10-27T12:00:00Z"),
            "simulation-test-001");
        await using var output = new MemoryStream();
        await EvidenceExporter.WriteJsonAsync(output, evidence);

        using var document = JsonDocument.Parse(output.ToArray());
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("simulation-test-001", root.GetProperty("runId").GetString());
        Assert.Equal(scene.SceneId, root.GetProperty("sceneId").GetGuid());
        Assert.Equal("running", root.GetProperty("runtime").GetProperty("state").GetString());
        Assert.True(root.GetProperty("runtime").TryGetProperty("averageLogicStepMilliseconds", out _));
        Assert.Equal(scene.Tags.Count, root.GetProperty("tags").GetArrayLength());
        Assert.True(root.GetProperty("events").GetArrayLength() >= scene.Tags.Count + 2);
    }

    [Fact]
    public async Task WritesVersionedTagTelemetryForOneTemporalSnapshot()
    {
        var scene = LoadScene();
        var journal = new EventJournal();
        var tags = RegisterSceneTags(scene, journal);
        var engine = new SimulationEngine(tags, journal);
        var firstInput = scene.Tags.First(tag => tag.Direction == TagDirection.PlcInput);
        tags.PublishFromSimulation(firstInput.Id, TagValue.FromBool(false), 1);
        tags.Force(firstInput.Id, TagValue.FromBool(true), 2);
        tags.SetQuality(firstInput.Id, TagQuality.Stale, 3);
        tags.SetFault(firstInput.Id, TagFault.AlwaysOn, 4);
        var capturedAt = DateTimeOffset.Parse("2026-10-31T13:45:00Z");
        var evidence = EvidenceExporter.Capture(
            scene,
            engine,
            tags,
            journal,
            capturedAt,
            "simulation-test-telemetry");
        await using var output = new MemoryStream();

        await EvidenceExporter.WriteTagTelemetryCsvAsync(output, evidence);

        Assert.True(output.CanWrite);
        var lines = Encoding.UTF8.GetString(output.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(scene.Tags.Count + 1, lines.Length);
        Assert.Equal(
            "schema_version,run_id,scene_id,tick,observed_at_utc,tag_id,canonical_name,data_type,direction,raw_value,effective_value,quality,fault,is_forced,last_changed_tick,last_changed_at_utc",
            lines[0].TrimStart('\uFEFF'));
        Assert.All(lines.Skip(1), line =>
        {
            Assert.Equal(16, line.Split(',').Length);
            Assert.Contains(",simulation-test-telemetry,", line, StringComparison.Ordinal);
            Assert.Contains(",0,2026-10-31T13:45:00.0000000+00:00,", line, StringComparison.Ordinal);
        });
        var changedTag = lines.Single(line =>
            line.Contains($",{firstInput.CanonicalName},", StringComparison.Ordinal));
        var changedFields = changedTag.Split(',');
        Assert.Equal("False", changedFields[9]);
        Assert.Equal("True", changedFields[10]);
        Assert.Equal("stale", changedFields[11]);
        Assert.Equal("alwaysOn", changedFields[12]);
        Assert.Equal("true", changedFields[13]);
        Assert.Equal("4", changedFields[14]);
    }

    [Fact]
    public void RejectsUnsafeRunIdentifier()
    {
        var scene = LoadScene();
        var journal = new EventJournal();
        var tags = RegisterSceneTags(scene, journal);
        var engine = new SimulationEngine(tags, journal);

        Assert.Throws<ArgumentException>(() => EvidenceExporter.Capture(
            scene,
            engine,
            tags,
            journal,
            runId: "not,portable"));
    }

    [Fact]
    public async Task CsvEscapesMessagesAndKeepsDestinationOpen()
    {
        var scene = LoadScene();
        var journal = new EventJournal();
        var tags = RegisterSceneTags(scene, journal);
        var engine = new SimulationEngine(tags, journal);
        journal.Record(0, "teste", "valor, com \"aspas\"\ne linha");
        var evidence = EvidenceExporter.Capture(scene, engine, tags, journal);
        await using var output = new MemoryStream();

        await EvidenceExporter.WriteEventsCsvAsync(output, evidence);
        Assert.True(output.CanWrite);

        var csv = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("sequence,tick,occurredAtUtc,category,message,tagId", csv, StringComparison.Ordinal);
        Assert.Contains("\"valor, com \"\"aspas\"\"", csv, StringComparison.Ordinal);
    }

    private static SceneDocument LoadScene() => SceneSerializer.Deserialize(
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "samples",
            "celula-envase.scene.json")));

    private static TagRegistry RegisterSceneTags(SceneDocument scene, EventJournal journal)
    {
        var tags = new TagRegistry(journal);
        foreach (var tag in scene.Tags)
        {
            tags.Register(tag);
        }

        return tags;
    }
}
