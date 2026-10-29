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
            DateTimeOffset.Parse("2026-10-27T12:00:00Z"));
        await using var output = new MemoryStream();
        await EvidenceExporter.WriteJsonAsync(output, evidence);

        using var document = JsonDocument.Parse(output.ToArray());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(scene.SceneId, root.GetProperty("sceneId").GetGuid());
        Assert.Equal("running", root.GetProperty("runtime").GetProperty("state").GetString());
        Assert.True(root.GetProperty("runtime").TryGetProperty("averageLogicStepMilliseconds", out _));
        Assert.Equal(scene.Tags.Count, root.GetProperty("tags").GetArrayLength());
        Assert.True(root.GetProperty("events").GetArrayLength() >= scene.Tags.Count + 2);
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
