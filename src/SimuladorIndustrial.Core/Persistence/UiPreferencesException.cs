namespace SimuladorIndustrial.Core.Persistence;

public sealed class UiPreferencesException : Exception
{
    public UiPreferencesException(string message)
        : base(message)
    {
    }

    public UiPreferencesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
