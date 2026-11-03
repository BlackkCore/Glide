namespace ServiceLib.Manager;

public class NoticeManager
{
    private static readonly Lazy<NoticeManager> _instance = new(() => new());
    public static NoticeManager Instance => _instance.Value;

    public void Enqueue(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return;
        }
        AppEvents.SendSnackMsgRequested.Publish(content);
    }

    public void SendMessage(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return;
        }
        AppEvents.SendMsgViewRequested.Publish(content);
    }

    public void SendMessageEx(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return;
        }
        content = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} {content}";
        SendMessage(content);
    }

    public void SendMessageAndEnqueue(string? msg)
    {
        Enqueue(msg);
        SendMessage(msg);
    }

    /// <summary>
    /// Sends each error and warning in <paramref name="validatorResult"/> to the message panel
    /// and enqueues a summary snack notification (capped at 10 messages).
    /// Returns <c>true</c> when there were any messages so the caller can decide on early-return
    /// based on <see cref="NodeValidatorResult.Success"/>.
    /// </summary>
    public bool NotifyValidatorResult(NodeValidatorResult validatorResult)
    {
        var msgs = new List<string>([.. validatorResult.Errors, .. validatorResult.Warnings]);
        if (msgs.Count == 0)
        {
            return false;
        }
        // Every message goes to the log pane; the toast only summarizes.
        foreach (var msg in msgs)
        {
            SendMessage(msg);
        }
        Enqueue(SummarizeValidator(validatorResult.Errors, validatorResult.Warnings));
        return true;
    }

    /// <summary>
    /// Glide: the toast text for a validation result. A group such as "Auto (fastest)" validates every
    /// server in the subscription, so listing each warning made a wall of text. Warnings alone become one
    /// line pointing to the log; errors show the first three distinct ones and how many more there are.
    /// </summary>
    public static string SummarizeValidator(IReadOnlyCollection<string> errors, IReadOnlyCollection<string> warnings)
    {
        var distinctErrors = errors.Where(e => e.IsNotEmpty()).Distinct().ToList();
        var warningCount = warnings.Where(w => w.IsNotEmpty()).Distinct().Count();
        if (distinctErrors.Count == 0)
        {
            return warningCount == 0 ? string.Empty : string.Format(ResUI.TipValidatorWarnings, warningCount);
        }
        var lines = distinctErrors.Take(3).ToList();
        var more = distinctErrors.Count - lines.Count + warningCount;
        if (more > 0)
        {
            lines.Add(string.Format(ResUI.TipAndMoreInLog, more));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
