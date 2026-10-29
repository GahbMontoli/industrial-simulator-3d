namespace SimuladorIndustrial.Core.Components;

public sealed class ComponentPackageException : Exception
{
    public ComponentPackageException(string message)
        : base(message)
    {
    }

    public ComponentPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
