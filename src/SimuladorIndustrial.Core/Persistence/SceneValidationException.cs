namespace SimuladorIndustrial.Core.Persistence;

public sealed class SceneValidationException : Exception
{
    public SceneValidationException(IReadOnlyList<string> errors)
        : base($"A cena é inválida: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
