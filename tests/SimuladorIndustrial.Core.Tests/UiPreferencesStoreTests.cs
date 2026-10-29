using System.Text;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.Core.Tests;

public sealed class UiPreferencesStoreTests
{
    [Fact]
    public void RoundTripPreservesValidatedPreferences()
    {
        var original = new UiPreferences
        {
            Theme = InterfaceTheme.HighContrast,
            UiScale = 1.25d,
            StartupSection = ApplicationSection.Diagnostics,
            WelcomeDismissed = true,
            ConfirmDestructiveActions = false
        };

        var restored = UiPreferencesStore.Deserialize(UiPreferencesStore.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void MigratesLegacyAliasesAndDefaultsMissingValues()
    {
        const string legacy = """
            {
              "theme": "dark",
              "scale": 1.1,
              "startSection": "editor",
              "showWelcome": false
            }
            """;

        var preferences = UiPreferencesStore.Deserialize(legacy);

        Assert.Equal(UiPreferences.CurrentSchemaVersion, preferences.SchemaVersion);
        Assert.Equal(1.1d, preferences.UiScale);
        Assert.Equal(ApplicationSection.Editor, preferences.StartupSection);
        Assert.True(preferences.WelcomeDismissed);
        Assert.True(preferences.ConfirmDestructiveActions);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"uiScale\":0.5}")]
    [InlineData("{\"schemaVersion\":1,\"theme\":\"unknown\"}")]
    [InlineData("{\"schemaVersion\":1,\"unexpected\":true}")]
    [InlineData("{\"schemaVersion\":\"one\"}")]
    [InlineData("{\"showWelcome\":\"yes\"}")]
    public void RejectsUnsupportedOrInvalidDocuments(string json)
    {
        Assert.Throws<UiPreferencesException>(() => UiPreferencesStore.Deserialize(json));
    }

    [Fact]
    public void MissingFileReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ui.json");

        var preferences = UiPreferencesStore.Load(path);

        Assert.Same(UiPreferences.Default, preferences);
    }

    [Fact]
    public void SaveCreatesAndAtomicallyReplacesThePreferencesFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"simulador-ui-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "preferences.json");
        try
        {
            UiPreferencesStore.Save(path, new UiPreferences { UiScale = 0.9d });
            UiPreferencesStore.Save(path, new UiPreferences
            {
                Theme = InterfaceTheme.HighContrast,
                UiScale = 1.4d,
                StartupSection = ApplicationSection.Connections,
                WelcomeDismissed = true
            });

            var loaded = UiPreferencesStore.Load(path);

            Assert.Equal(InterfaceTheme.HighContrast, loaded.Theme);
            Assert.Equal(1.4d, loaded.UiScale);
            Assert.Equal(ApplicationSection.Connections, loaded.StartupSection);
            Assert.True(loaded.WelcomeDismissed);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void RejectsOversizedAndInvalidUtf8Files()
    {
        var oversized = new string('x', UiPreferencesStore.MaximumPreferencesBytes + 1);
        Assert.Throws<UiPreferencesException>(() => UiPreferencesStore.Deserialize(oversized));

        var directory = Path.Combine(Path.GetTempPath(), $"simulador-ui-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "preferences.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, [0xC3, 0x28]);

            Assert.Throws<UiPreferencesException>(() => UiPreferencesStore.Load(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
