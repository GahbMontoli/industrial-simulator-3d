using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task WritesTraceableSyntheticPackageForPowerBi()
    {
        var fixture = CreateFixture();
        var results = FillingCellFaultStudy.Run(fixture.Tags, fixture.Bindings);
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"simulador-fault-study-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var sourcePath = Path.Combine(directory, "scene.json");
            var csvPath = Path.Combine(directory, "fault-study.csv");
            await File.WriteAllTextAsync(sourcePath, "{\"schemaVersion\":1}");
            var exportedAtUtc = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

            var package = await FillingCellFaultStudyPackageExporter.WriteAsync(
                csvPath,
                sourcePath,
                results,
                runId: "fault-study-test",
                exportedAtUtc);

            Assert.True(File.Exists(package.CsvPath));
            Assert.True(File.Exists(package.ManifestPath));
            Assert.Equal("fault-study-test", package.Manifest.RunId);
            Assert.Equal("synthetic", package.Manifest.EvidenceClass);
            Assert.Equal("valid", package.Manifest.QualityStatus);
            Assert.Equal(5, package.Manifest.RowCount);
            Assert.True(package.Manifest.AllExpectationsMet);
            Assert.Equal(ComputeSha256(sourcePath), package.Manifest.SourceSha256);
            Assert.Equal(ComputeSha256(csvPath), package.Manifest.CsvSha256);

            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(package.ManifestPath));
            Assert.Equal(
                "industrial-simulator-3d",
                manifest.RootElement.GetProperty("projectId").GetString());
            Assert.Contains(
                "não representa evidência de campo",
                manifest.RootElement.GetProperty("evidenceLimit").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsManifestCollisionWithoutChangingSource()
    {
        var fixture = CreateFixture();
        var results = FillingCellFaultStudy.Run(fixture.Tags, fixture.Bindings);
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"simulador-fault-study-collision-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var sourcePath = Path.Combine(directory, "fault-study.manifest.json");
            var csvPath = Path.Combine(directory, "fault-study.csv");
            const string source = "{\"source\":true}";
            await File.WriteAllTextAsync(sourcePath, source);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                FillingCellFaultStudyPackageExporter.WriteAsync(csvPath, sourcePath, results));

            Assert.Equal(source, await File.ReadAllTextAsync(sourcePath));
            Assert.False(File.Exists(csvPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoresPreviousCsvWhenManifestPromotionFails()
    {
        var fixture = CreateFixture();
        var results = FillingCellFaultStudy.Run(fixture.Tags, fixture.Bindings);
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"simulador-fault-study-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var sourcePath = Path.Combine(directory, "scene.json");
            var csvPath = Path.Combine(directory, "fault-study.csv");
            var manifestPath = Path.ChangeExtension(csvPath, ".manifest.json");
            await File.WriteAllTextAsync(sourcePath, "{\"schemaVersion\":1}");
            await File.WriteAllTextAsync(csvPath, "previous-csv");
            Directory.CreateDirectory(manifestPath);

            await Assert.ThrowsAsync<IOException>(() =>
                FillingCellFaultStudyPackageExporter.WriteAsync(csvPath, sourcePath, results));

            Assert.Equal("previous-csv", await File.ReadAllTextAsync(csvPath));
            Assert.True(Directory.Exists(manifestPath));
            Assert.Empty(Directory.GetFiles(directory, ".*.tmp"));
            Assert.Empty(Directory.GetFiles(directory, ".*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

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
