using System.Globalization;
using System.Text;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Scenarios;

public enum FillingCellFaultScenario
{
    Nominal,
    ContainerSensorAlwaysOff,
    FillValveAlwaysOff,
    VolumeSensorStuck,
    FillValveAlwaysOn
}

public sealed record FillingCellStudyBindings(
    Guid ContainerDetectedTagId,
    Guid GateClosedTagId,
    Guid FillValveTagId,
    Guid FilledVolumeTagId);

public sealed record FillingCellFaultStudyOptions
{
    public TimeSpan Step { get; init; } = TimeSpan.FromMilliseconds(20);

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(8);

    public float FlowLitersPerSecond { get; init; } = 0.25f;

    public float CapacityLiters { get; init; } = 1f;

    public float TargetVolumeLiters { get; init; } = 1f;
}

public sealed record FillingCellFaultStudyResult(
    FillingCellFaultScenario Scenario,
    bool ExpectationMet,
    string Observation,
    int ExecutedSteps,
    double SimulatedSeconds,
    double? TargetReachedAtSeconds,
    float FinalPhysicalVolumeLiters,
    float FinalReportedVolumeLiters,
    float MaximumSensorDeviationLiters,
    int ValveOpenAtCapacitySteps,
    int ValveCommandDivergenceSteps,
    bool FinalValveCommand,
    bool FinalValveEffectiveState);

public static class FillingCellFaultStudy
{
    private const float VolumeTolerance = 0.0001f;

    private static readonly FillingCellFaultScenario[] Scenarios =
    [
        FillingCellFaultScenario.Nominal,
        FillingCellFaultScenario.ContainerSensorAlwaysOff,
        FillingCellFaultScenario.FillValveAlwaysOff,
        FillingCellFaultScenario.VolumeSensorStuck,
        FillingCellFaultScenario.FillValveAlwaysOn
    ];

    public static IReadOnlyList<FillingCellFaultStudyResult> Run(
        IEnumerable<TagDefinition> tagDefinitions,
        FillingCellStudyBindings bindings,
        FillingCellFaultStudyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tagDefinitions);
        ArgumentNullException.ThrowIfNull(bindings);
        options ??= new FillingCellFaultStudyOptions();
        ValidateOptions(options);

        var definitions = tagDefinitions.ToArray();
        if (definitions.Length == 0)
        {
            throw new ArgumentException("At least one tag definition is required.", nameof(tagDefinitions));
        }

        var stepCount = checked((int)Math.Ceiling(options.Duration.Ticks / (double)options.Step.Ticks));
        return Scenarios
            .Select(scenario => RunScenario(definitions, bindings, options, scenario, stepCount))
            .ToArray();
    }

    public static async ValueTask WriteCsvAsync(
        Stream destination,
        IEnumerable<FillingCellFaultStudyResult> results,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(results);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1_024,
            leaveOpen: true);
        await writer.WriteLineAsync(
            "scenario,expectationMet,simulatedSeconds,targetReachedAtSeconds," +
            "finalPhysicalVolumeLiters,finalReportedVolumeLiters,maximumSensorDeviationLiters," +
            "valveOpenAtCapacitySteps,valveCommandDivergenceSteps,finalValveCommand," +
            "finalValveEffectiveState,observation").ConfigureAwait(false);

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = string.Join(
                ',',
                result.Scenario,
                result.ExpectationMet.ToString(CultureInfo.InvariantCulture),
                result.SimulatedSeconds.ToString("F3", CultureInfo.InvariantCulture),
                result.TargetReachedAtSeconds?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                result.FinalPhysicalVolumeLiters.ToString("F4", CultureInfo.InvariantCulture),
                result.FinalReportedVolumeLiters.ToString("F4", CultureInfo.InvariantCulture),
                result.MaximumSensorDeviationLiters.ToString("F4", CultureInfo.InvariantCulture),
                result.ValveOpenAtCapacitySteps.ToString(CultureInfo.InvariantCulture),
                result.ValveCommandDivergenceSteps.ToString(CultureInfo.InvariantCulture),
                result.FinalValveCommand.ToString(CultureInfo.InvariantCulture),
                result.FinalValveEffectiveState.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(result.Observation));
            await writer.WriteLineAsync(row).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FillingCellFaultStudyResult RunScenario(
        IReadOnlyList<TagDefinition> definitions,
        FillingCellStudyBindings bindings,
        FillingCellFaultStudyOptions options,
        FillingCellFaultScenario scenario,
        int stepCount)
    {
        var journal = new EventJournal();
        var tags = new TagRegistry(journal);
        foreach (var definition in definitions)
        {
            tags.Register(definition);
        }

        var controller = new DemoPlcControllerSystem(
            bindings.ContainerDetectedTagId,
            bindings.FilledVolumeTagId,
            bindings.GateClosedTagId,
            bindings.FillValveTagId,
            options.TargetVolumeLiters);
        var process = new FillingCellSystem(
            bindings.ContainerDetectedTagId,
            bindings.GateClosedTagId,
            bindings.FillValveTagId,
            bindings.FilledVolumeTagId,
            options.FlowLitersPerSecond,
            options.CapacityLiters);
        var engine = new SimulationEngine(
            tags,
            journal,
            [controller, process],
            new FixedStepClock(options.Step, maxStepsPerAdvance: 1));

        engine.Reset();
        ApplyFault(tags, bindings, scenario);
        engine.Run();

        float maximumSensorDeviation = 0f;
        int valveOpenAtCapacitySteps = 0;
        int valveCommandDivergenceSteps = 0;
        double? targetReachedAtSeconds = null;

        for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
        {
            engine.Advance(options.Step);
            var reportedVolume = tags.Get(bindings.FilledVolumeTagId).EffectiveValue.AsFloat32();
            var valve = tags.Get(bindings.FillValveTagId);
            var deviation = MathF.Abs(process.VolumeLiters - reportedVolume);
            maximumSensorDeviation = MathF.Max(maximumSensorDeviation, deviation);

            if (process.VolumeLiters + VolumeTolerance >= options.TargetVolumeLiters)
            {
                targetReachedAtSeconds ??= engine.Clock.Tick * options.Step.TotalSeconds;
                if (valve.EffectiveValue.AsBool())
                {
                    valveOpenAtCapacitySteps++;
                }
            }

            if (valve.RawValue.AsBool() != valve.EffectiveValue.AsBool())
            {
                valveCommandDivergenceSteps++;
            }
        }

        var finalReportedVolume = tags.Get(bindings.FilledVolumeTagId).EffectiveValue.AsFloat32();
        var finalValve = tags.Get(bindings.FillValveTagId);
        var assessment = Assess(
            scenario,
            process.VolumeLiters,
            finalReportedVolume,
            maximumSensorDeviation,
            finalValve.RawValue.AsBool(),
            finalValve.EffectiveValue.AsBool(),
            options);

        return new FillingCellFaultStudyResult(
            scenario,
            assessment.ExpectationMet,
            assessment.Observation,
            checked((int)engine.Metrics.ExecutedSteps),
            engine.Clock.Tick * options.Step.TotalSeconds,
            targetReachedAtSeconds,
            process.VolumeLiters,
            finalReportedVolume,
            maximumSensorDeviation,
            valveOpenAtCapacitySteps,
            valveCommandDivergenceSteps,
            finalValve.RawValue.AsBool(),
            finalValve.EffectiveValue.AsBool());
    }

    private static void ApplyFault(
        TagRegistry tags,
        FillingCellStudyBindings bindings,
        FillingCellFaultScenario scenario)
    {
        var fault = scenario switch
        {
            FillingCellFaultScenario.Nominal => ((Guid?)null, TagFault.None),
            FillingCellFaultScenario.ContainerSensorAlwaysOff =>
                ((Guid?)bindings.ContainerDetectedTagId, TagFault.AlwaysOff),
            FillingCellFaultScenario.FillValveAlwaysOff =>
                ((Guid?)bindings.FillValveTagId, TagFault.AlwaysOff),
            FillingCellFaultScenario.VolumeSensorStuck =>
                ((Guid?)bindings.FilledVolumeTagId, TagFault.Stuck),
            FillingCellFaultScenario.FillValveAlwaysOn =>
                ((Guid?)bindings.FillValveTagId, TagFault.AlwaysOn),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        if (fault.Item1 is Guid tagId)
        {
            tags.SetFault(tagId, fault.Item2, tick: 0);
        }
    }

    private static (bool ExpectationMet, string Observation) Assess(
        FillingCellFaultScenario scenario,
        float physicalVolume,
        float reportedVolume,
        float maximumSensorDeviation,
        bool valveCommand,
        bool valveEffectiveState,
        FillingCellFaultStudyOptions options)
    {
        var targetReached = physicalVolume + VolumeTolerance >= options.TargetVolumeLiters;
        return scenario switch
        {
            FillingCellFaultScenario.Nominal =>
                Evaluate(
                    targetReached && !valveCommand && !valveEffectiveState &&
                    maximumSensorDeviation <= VolumeTolerance,
                    "Ciclo nominal alcançou o volume-alvo e encerrou o comando de enchimento."),
            FillingCellFaultScenario.ContainerSensorAlwaysOff =>
                Evaluate(
                    physicalVolume <= VolumeTolerance && !valveEffectiveState,
                    "A ausência do sinal de presença bloqueou a produção."),
            FillingCellFaultScenario.FillValveAlwaysOff =>
                Evaluate(
                    physicalVolume <= VolumeTolerance && valveCommand && !valveEffectiveState,
                    "O controlador solicitou enchimento, mas a válvula efetiva permaneceu fechada."),
            FillingCellFaultScenario.VolumeSensorStuck =>
                Evaluate(
                    targetReached && reportedVolume <= VolumeTolerance && valveCommand && valveEffectiveState,
                    "O processo atingiu a capacidade, mas o sensor travado manteve o comando de enchimento ativo."),
            FillingCellFaultScenario.FillValveAlwaysOn =>
                Evaluate(
                    targetReached && !valveCommand && valveEffectiveState,
                    "O controlador solicitou fechamento, mas a válvula efetiva permaneceu aberta."),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        (bool ExpectationMet, string Observation) Evaluate(bool expectationMet, string successObservation) =>
            expectationMet
                ? (true, successObservation)
                : (false,
                    $"Resposta diferente da esperada: volume físico={physicalVolume:F4} L, " +
                    $"volume indicado={reportedVolume:F4} L, comando={valveCommand}, " +
                    $"estado efetivo={valveEffectiveState}.");
    }

    private static void ValidateOptions(FillingCellFaultStudyOptions options)
    {
        if (options.Step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The simulation step must be positive.");
        }

        if (options.Duration <= TimeSpan.Zero || options.Duration < options.Step)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The study duration must be at least one simulation step.");
        }

        if (!float.IsFinite(options.FlowLitersPerSecond) || options.FlowLitersPerSecond <= 0f ||
            !float.IsFinite(options.CapacityLiters) || options.CapacityLiters <= 0f ||
            !float.IsFinite(options.TargetVolumeLiters) || options.TargetVolumeLiters <= 0f ||
            options.TargetVolumeLiters > options.CapacityLiters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Flow, capacity and target must be finite and positive; target cannot exceed capacity.");
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
}
