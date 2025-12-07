namespace HealthCoachServer.Infra;

public class OperationResult<T> where T : class
{
    public T Result { get; }
    public string Error { get; } = "Empty error.";
    public bool IsCompleted => Result != null;
    public bool IsCancelled => !IsCompleted;

    private OperationResult(T result, string error)
    {
        Result = result;
        Error = error;
    }

    public static OperationResult<T> CreateCompleted(T result)
    {
        return new OperationResult<T>(result, string.Empty);
    }
    
    public static OperationResult<T> CreateCancelled(string error)
    {
        return new OperationResult<T>(null!, error);
    }
}