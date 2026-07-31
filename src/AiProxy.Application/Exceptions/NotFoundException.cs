namespace AiProxy.Application.Exceptions;

/// <summary>Thrown when a requested entity (by name or id) does not exist.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
