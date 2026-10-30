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
        var options = ParseOptions(arguments);
        var scenePath = ResolveScenePath(options.ScenePath);
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

        if (options.RunFaultStudy)
        {
            return await RunFaultStudyAsync(
                scene,
                scenePath,
                containerDetected,
                gateClosed,
                fillValve,
                filledVolume,
                flow,
                options.OutputPath);
        }

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

static CliOptions ParseOptions(string[] arguments)
{
    string? scenePath = null;
    string? outputPath = null;
    var runFaultStudy = false;

    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "--fault-study":
                runFaultStudy = true;
                break;
            case "--scene":
                scenePath = ReadOptionValue(arguments, ref index, "--scene");
                break;
            case "--output":
                outputPath = ReadOptionValue(arguments, ref index, "--output");
                break;
            default:
                if (arguments[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Opção desconhecida: {arguments[index]}");
                }

                if (scenePath is not null)
                {
                    throw new InvalidOperationException("Informe apenas um caminho de cena.");
                }

                scenePath = arguments[index];
                break;
        }
    }

    if (outputPath is not null && !runFaultStudy)
    {
        throw new InvalidOperationException("A opção --output requer --fault-study.");
    }

    return new CliOptions(scenePath, runFaultStudy, outputPath);
}

static string ReadOptionValue(string[] arguments, ref int index, string option)
{
    if (index + 1 >= arguments.Length ||
        arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"A opção {option} requer um valor.");
    }

    index++;
    return arguments[index];
}

static string ResolveScenePath(string? requestedPath)
{
    if (!string.IsNullOrWhiteSpace(requestedPath))
    {
        return Path.GetFullPath(requestedPath);
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

static async Task<int> RunFaultStudyAsync(
    SceneDocument scene,
    string scenePath,
    TagDefinition containerDetected,
    TagDefinition gateClosed,
    TagDefinition fillValve,
    TagDefinition filledVolume,
    float flowLitersPerSecond,
    string? requestedOutputPath)
{
    var results = FillingCellFaultStudy.Run(
        scene.Tags,
        new FillingCellStudyBindings(
            containerDetected.Id,
            gateClosed.Id,
            fillValve.Id,
            filledVolume.Id),
        new FillingCellFaultStudyOptions
        {
            Step = TimeSpan.FromMilliseconds(scene.Simulation.LogicStepMilliseconds),
            FlowLitersPerSecond = flowLitersPerSecond
        });

    Console.WriteLine($"Campanha de falhas: {scene.Name}");
    Console.WriteLine("Cenário | Esperado | Volume físico | Volume indicado | Divergência do comando");
    foreach (var result in results)
    {
        Console.WriteLine(
            $"{result.Scenario} | {(result.ExpectationMet ? "SIM" : "NÃO")} | " +
            $"{result.FinalPhysicalVolumeLiters.ToString("F3", CultureInfo.InvariantCulture)} L | " +
            $"{result.FinalReportedVolumeLiters.ToString("F3", CultureInfo.InvariantCulture)} L | " +
            $"{result.ValveCommandDivergenceSteps} passos");
        Console.WriteLine($"  {result.Observation}");
    }

    Console.WriteLine($"Cena: {scenePath}");
    if (!string.IsNullOrWhiteSpace(requestedOutputPath))
    {
        var outputPath = Path.GetFullPath(requestedOutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await using var output = File.Create(outputPath);
        await FillingCellFaultStudy.WriteCsvAsync(output, results);
        Console.WriteLine($"CSV: {outputPath}");
    }

    return results.All(result => result.ExpectationMet) ? 0 : 3;
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

internal sealed record CliOptions(
    string? ScenePath,
    bool RunFaultStudy,
    string? OutputPath);
