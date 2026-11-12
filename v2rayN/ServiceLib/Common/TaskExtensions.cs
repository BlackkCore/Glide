namespace ServiceLib.Common;

public static class TaskExtensions
{
    /// <summary>
    /// Lets a task run without awaiting it, but logs its failure instead of losing it (a discarded
    /// "_ = SomethingAsync()" hides every exception). Cancellation is not a failure.
    /// </summary>
    public static async void Forget(this Task task, string tag, Action<Exception>? onError = null)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(tag, ex);
            onError?.Invoke(ex);
        }
    }
}
