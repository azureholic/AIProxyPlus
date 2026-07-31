namespace AiProxy.Application.Exceptions;

/// <summary>Thrown when a command would conflict with existing state (e.g. a duplicate unique name).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
