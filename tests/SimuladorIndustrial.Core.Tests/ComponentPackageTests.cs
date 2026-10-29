using System.IO.Compression;
using SimuladorIndustrial.Core.Components;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.Core.Tests;

public sealed class ComponentPackageTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        $"simulador-industrial-components-{Guid.NewGuid():N}");

    [Fact]
    public void DefaultCatalogContainsBuiltInComponents()
    {
        var result = ComponentCatalog.LoadDefault(Path.Combine(_temporaryRoot, "missing"));

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Catalog.TryGet("conveyor.straight.v1", out var conveyor));
        Assert.True(conveyor.IsBuiltIn);
        Assert.Equal("Esteira reta", conveyor.Definition.DisplayName);
    }

    [Fact]
    public void RepositorySamplePackageCanBeInstalled()
    {
        var packagePath = Path.Combine(AppContext.BaseDirectory, "samples", "motor-basico.silabcomponent");
        var installation = ComponentPackageInstaller.Install(
            packagePath,
            Path.Combine(_temporaryRoot, "sample-store"));

        Assert.Equal("org.simuladorindustrial.samples.motor", installation.Manifest.PackageId);
        Assert.Contains(
            installation.Manifest.Components,
            component => component.Type == "sample.motor-induction.v1");
    }

    [Fact]
    public void InstallsAssetOnlyPackageAndLoadsItIntoCatalog()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var packagePath = Path.Combine(_temporaryRoot, "motor.silabcomponent");
        CreatePackage(packagePath, CreateManifest());

        var store = Path.Combine(_temporaryRoot, "store");
        var installation = ComponentPackageInstaller.Install(packagePath, store);
        var catalog = ComponentCatalog.LoadDefault(store);

        Assert.Equal("org.example.motor", installation.Manifest.PackageId);
        Assert.Equal(64, installation.PackageSha256.Length);
        Assert.True(File.Exists(Path.Combine(
            installation.InstallationDirectory,
            ComponentPackageSerializer.ManifestFileName)));
        Assert.True(catalog.Catalog.TryGet("actuator.motor.v1", out var motor));
        Assert.False(motor.IsBuiltIn);
        Assert.Equal("Motor trifásico", motor.Definition.DisplayName);
        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    public void RejectsArchiveTraversal()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var packagePath = Path.Combine(_temporaryRoot, "unsafe.silabcomponent");
        CreatePackage(packagePath, CreateManifest(), ("../outside.txt", "unsafe"));

        var exception = Assert.Throws<ComponentPackageException>(() =>
            ComponentPackageInstaller.Install(packagePath, Path.Combine(_temporaryRoot, "store")));

        Assert.Contains("Caminho inseguro", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_temporaryRoot, "outside.txt")));
    }

    [Fact]
    public void RejectsExecutableContent()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var packagePath = Path.Combine(_temporaryRoot, "executable.silabcomponent");
        CreatePackage(packagePath, CreateManifest(), ("runtime/plugin.dll", "not-a-real-dll"));

        var exception = Assert.Throws<ComponentPackageException>(() =>
            ComponentPackageInstaller.Install(packagePath, Path.Combine(_temporaryRoot, "store")));

        Assert.Contains("não permitido", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsSecondInstallationOfSameVersion()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var packagePath = Path.Combine(_temporaryRoot, "motor.silabcomponent");
        CreatePackage(packagePath, CreateManifest());
        var store = Path.Combine(_temporaryRoot, "store");
        ComponentPackageInstaller.Install(packagePath, store);

        var exception = Assert.Throws<ComponentPackageException>(() =>
            ComponentPackageInstaller.Install(packagePath, store));

        Assert.Contains("já está instalado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsModelReferenceMissingFromPackage()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var packagePath = Path.Combine(_temporaryRoot, "missing-model.silabcomponent");
        var manifest = CreateManifest() with
        {
            Components =
            [
                CreateManifest().Components[0] with
                {
                    Visual = new ComponentVisualDefinition(
                        ComponentVisualKind.Model,
                        new Vector3Document(0.8, 0.8, 0.8),
                        "#336699",
                        "assets/motor.glb")
                }
            ]
        };
        CreatePackage(packagePath, manifest);

        var exception = Assert.Throws<ComponentPackageException>(() =>
            ComponentPackageInstaller.Install(packagePath, Path.Combine(_temporaryRoot, "store")));

        Assert.Contains("não existe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPackageThatRequiresNewerHost()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var packagePath = Path.Combine(_temporaryRoot, "future.silabcomponent");
        CreatePackage(packagePath, CreateManifest() with { MinimumHostVersion = "99.0.0" });

        var exception = Assert.Throws<ComponentPackageException>(() =>
            ComponentPackageInstaller.Install(packagePath, Path.Combine(_temporaryRoot, "store")));

        Assert.Contains("requer o Simulador Industrial", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }

    private static ComponentPackageManifest CreateManifest()
    {
        return new ComponentPackageManifest(
            ComponentPackageValidator.CurrentSchemaVersion,
            "org.example.motor",
            "1.0.0",
            "Pacote de motores",
            "Laboratório exemplo",
            "Componente demonstrativo criado do zero.",
            "MIT",
            "0.2.0",
            [
                new ComponentDefinition(
                    "actuator.motor.v1",
                    "Motor trifásico",
                    "Atuadores",
                    "Motor genérico para laboratórios.",
                    new ComponentVisualDefinition(
                        ComponentVisualKind.Cylinder,
                        new Vector3Document(0.8, 0.6, 0.6),
                        "#336699"),
                    [
                        new ComponentPortDefinition(
                            "running",
                            "Em movimento",
                            ComponentPortDirection.Input,
                            ComponentPortDataType.Bool)
                    ],
                    [])
            ]);
    }

    private static void CreatePackage(
        string packagePath,
        ComponentPackageManifest manifest,
        params (string Name, string Content)[] additionalEntries)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry(ComponentPackageSerializer.ManifestFileName);
        using (var writer = new StreamWriter(manifestEntry.Open()))
        {
            writer.Write(ComponentPackageSerializer.Serialize(manifest));
        }

        foreach (var (name, content) in additionalEntries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
