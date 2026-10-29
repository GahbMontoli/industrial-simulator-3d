using System.Globalization;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Scenarios;
using SimuladorIndustrial.Core.Tags;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    try
    {
        var scenePath = ResolveScenePath(arguments);
        await using var sceneStream = File.OpenRead(scenePath);
        var scene = await SceneSerializer.DeserializeAsync(sceneStream);

        var journal = new EventJournal();
        var tags = new TagRegistry(journal);
        foreach (var tag in scene.Tags)
        {
            tags.Register(tag);
        }

        var containerDetected = FindTag(scene, "recipienteDetectado");
        var gateClosed = FindTag(scene, "bloqueadorFechado");
        var fillValve = FindTag(scene, "valvulaEnchimento");
        var filledVolume = FindTag(scene, "volumePreenchido");
        var flow = ReadFillingFlow(scene);

        var fillingCell = new FillingCellSystem(
            containerDetected.Id,
            gateClosed.Id,
            fillValve.Id,
            filledVolume.Id,
            flow);
        var clock = new FixedStepClock(
            TimeSpan.FromMilliseconds(scene.Simulation.LogicStepMilliseconds),
            scene.Simulation.MaxCatchUpSteps);
        var engine = new SimulationEngine(tags, journal, [fillingCell], clock);

        Console.WriteLine($"Cena carregada: {scene.Name}");
        Console.WriteLine($"Componentes: {scene.Components.Count} | Tags: {scene.Tags.Count}");

        engine.Reset();
        PrintState("Estado inicial", engine, tags, containerDetected.Id, filledVolume.Id);

        tags.WriteFromPlc(gateClosed.Id, TagValue.FromBool(true), engine.Clock.Tick);
        tags.WriteFromPlc(fillValve.Id, TagValue.FromBool(true), engine.Clock.Tick);
        engine.Run();
        AdvanceSteps(engine, 100);
        engine.Pause();
        PrintState("Após 2 segundos enchendo", engine, tags, containerDetected.Id, filledVolume.Id);

        var pausedTicks = engine.Advance(TimeSpan.FromSeconds(1));
        Console.WriteLine($"Avanço durante pausa: {pausedTicks} ticks executados");

        engine.Reset();
        PrintState("Após reset", engine, tags, containerDetected.Id, filledVolume.Id);
        Console.WriteLine($"Eventos auditáveis: {journal.Count}");
        Console.WriteLine($"Arquivo: {scenePath}");

        return 0;
    }
    catch (SceneValidationException exception)
    {
        Console.Error.WriteLine("Falha ao validar a cena:");
        foreach (var error in exception.Errors)
        {
            Console.Error.WriteLine($"- {error}");
        }

        return 2;
    }
    catch (Exception exception) when (
        exception is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        Console.Error.WriteLine($"Falha ao executar a simulação: {exception.Message}");
        return 1;
    }
}

static string ResolveScenePath(string[] arguments)
{
    if (arguments.Length > 1)
    {
        throw new InvalidOperationException("Informe no máximo um caminho de cena.");
    }

    if (arguments.Length == 1)
    {
        return Path.GetFullPath(arguments[0]);
    }

    var fileName = "celula-envase.scene.json";
    var candidates = new[]
    {
        Path.Combine(Environment.CurrentDirectory, "samples", fileName),
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "samples", fileName))
    };

    return candidates.FirstOrDefault(File.Exists)
        ?? throw new FileNotFoundException(
            "A cena padrão não foi encontrada. Informe o caminho do arquivo .scene.json.");
}

static TagDefinition FindTag(SceneDocument scene, string canonicalName)
{
    return scene.Tags.SingleOrDefault(
            tag => string.Equals(tag.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"A cena não contém a tag obrigatória '{canonicalName}'.");
}

static float ReadFillingFlow(SceneDocument scene)
{
    var fillingHead = scene.Components.SingleOrDefault(
        component => string.Equals(
            component.Type,
            "process.filling-head.v1",
            StringComparison.OrdinalIgnoreCase));

    if (fillingHead is null ||
        !fillingHead.Parameters.TryGetValue("flowLitersPerSecond", out var flowElement) ||
        !flowElement.TryGetSingle(out var flow))
    {
        throw new InvalidOperationException(
            "O componente de envase deve definir flowLitersPerSecond.");
    }

    return flow;
}

static void AdvanceSteps(SimulationEngine engine, int numberOfSteps)
{
    for (var index = 0; index < numberOfSteps; index++)
    {
        engine.Advance(engine.Clock.Step);
    }
}

static void PrintState(
    string label,
    SimulationEngine engine,
    TagRegistry tags,
    Guid containerDetectedTagId,
    Guid filledVolumeTagId)
{
    var detected = tags.Get(containerDetectedTagId).EffectiveValue.AsBool();
    var volume = tags.Get(filledVolumeTagId).EffectiveValue.AsFloat32();

    Console.WriteLine(
        $"{label}: estado={engine.State}, tick={engine.Clock.Tick}, " +
        $"recipiente={(detected ? "sim" : "não")}, " +
        $"volume={volume.ToString("F3", CultureInfo.InvariantCulture)} L");
}
