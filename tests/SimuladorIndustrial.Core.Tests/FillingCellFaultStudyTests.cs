using System.Text;
using SimuladorIndustrial.Core.Scenarios;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Tests;

public sealed class FillingCellFaultStudyTests
{
    [Fact]
    public void DistinguishesNominalCycleFromSensorAndActuatorFailures()
    {
        var fixture = CreateFixture();

        var results = FillingCellFaultStudy.Run(fixture.Tags, fixture.Bindings);

        Assert.Equal(5, results.Count);
        Assert.All(results, result => Assert.True(result.ExpectationMet, result.Observation));

        var nominal = Find(results, FillingCellFaultScenario.Nominal);
        Assert.Equal(1f, nominal.FinalPhysicalVolumeLiters, precision: 3);
        Assert.Equal(1f, nominal.FinalReportedVolumeLiters, precision: 3);
        Assert.False(nominal.FinalValveEffectiveState);
        Assert.Equal(0, nominal.ValveCommandDivergenceSteps);

        var missingContainer = Find(results, FillingCellFaultScenario.ContainerSensorAlwaysOff);
        Assert.Equal(0f, missingContainer.FinalPhysicalVolumeLiters);

        var valveClosed = Find(results, FillingCellFaultScenario.FillValveAlwaysOff);
        Assert.True(valveClosed.FinalValveCommand);
        Assert.False(valveClosed.FinalValveEffectiveState);
        Assert.True(valveClosed.ValveCommandDivergenceSteps > 0);

        var stuckSensor = Find(results, FillingCellFaultScenario.VolumeSensorStuck);
        Assert.Equal(1f, stuckSensor.FinalPhysicalVolumeLiters, precision: 3);
        Assert.Equal(0f, stuckSensor.FinalReportedVolumeLiters);
        Assert.True(stuckSensor.MaximumSensorDeviationLiters > 0.99f);
        Assert.True(stuckSensor.ValveOpenAtCapacitySteps > nominal.ValveOpenAtCapacitySteps);

        var valveOpen = Find(results, FillingCellFaultScenario.FillValveAlwaysOn);
        Assert.False(valveOpen.FinalValveCommand);
        Assert.True(valveOpen.FinalValveEffectiveState);
        Assert.True(valveOpen.ValveCommandDivergenceSteps > 0);
    }

    [Fact]
    public async Task WritesPortableCsvWithoutClosingDestination()
    {
        var fixture = CreateFixture();
        var results = FillingCellFaultStudy.Run(fixture.Tags, fixture.Bindings);
        await using var output = new MemoryStream();

        await FillingCellFaultStudy.WriteCsvAsync(output, results);

        Assert.True(output.CanWrite);
        var csv = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("scenario,expectationMet,simulatedSeconds", csv, StringComparison.Ordinal);
        Assert.Contains("Nominal,True,8.000", csv, StringComparison.Ordinal);
        Assert.Contains("VolumeSensorStuck,True,8.000", csv, StringComparison.Ordinal);
        Assert.Equal(6, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void RejectsTargetAboveProcessCapacity()
    {
        var fixture = CreateFixture();

        Assert.Throws<ArgumentOutOfRangeException>(() => FillingCellFaultStudy.Run(
            fixture.Tags,
            fixture.Bindings,
            new FillingCellFaultStudyOptions
            {
                CapacityLiters = 1f,
                TargetVolumeLiters = 1.1f
            }));
    }

    private static FillingCellFaultStudyResult Find(
        IReadOnlyList<FillingCellFaultStudyResult> results,
        FillingCellFaultScenario scenario) =>
        results.Single(result => result.Scenario == scenario);

    private static StudyFixture CreateFixture()
    {
        var container = CreateTag("recipienteDetectado", TagDataType.Bool, TagDirection.PlcInput, 0);
        var gate = CreateTag("bloqueadorFechado", TagDataType.Bool, TagDirection.PlcOutput, 0);
        var valve = CreateTag("valvulaEnchimento", TagDataType.Bool, TagDirection.PlcOutput, 1);
        var volume = CreateTag("volumePreenchido", TagDataType.Float32, TagDirection.PlcInput, 0);
        return new StudyFixture(
            [container, gate, valve, volume],
            new FillingCellStudyBindings(container.Id, gate.Id, valve.Id, volume.Id));
    }

    private static TagDefinition CreateTag(
        string canonicalName,
        TagDataType dataType,
        TagDirection direction,
        int address) =>
        new(
            Guid.NewGuid(),
            canonicalName,
            canonicalName,
            dataType,
            direction,
            address,
            $"study.{canonicalName}");

    private sealed record StudyFixture(
        IReadOnlyList<TagDefinition> Tags,
        FillingCellStudyBindings Bindings);
}
