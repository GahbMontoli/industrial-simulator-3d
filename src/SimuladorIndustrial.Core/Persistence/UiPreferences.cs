namespace SimuladorIndustrial.Core.Persistence;

public enum InterfaceTheme
{
    Dark,
    HighContrast
}

public enum ApplicationSection
{
    Overview,
    Simulation,
    Editor,
    Diagnostics,
    Connections,
    Help
}

public sealed record UiPreferences
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public InterfaceTheme Theme { get; init; } = InterfaceTheme.Dark;

    public double UiScale { get; init; } = 1d;

    public ApplicationSection StartupSection { get; init; } = ApplicationSection.Overview;

    public bool WelcomeDismissed { get; init; }

    public bool ConfirmDestructiveActions { get; init; } = true;

    public static UiPreferences Default { get; } = new();
}
