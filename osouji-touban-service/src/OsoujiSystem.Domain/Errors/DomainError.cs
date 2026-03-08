namespace OsoujiSystem.Domain.Errors;

public abstract record DomainError
{
    protected DomainError(
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? args = null)
    {
        Code = code;
        Message = message;
        Args = args ?? new Dictionary<string, object?>();
    }

    public string Code { get; }
    public string Message { get; }
    public IReadOnlyDictionary<string, object?> Args { get; }
}
