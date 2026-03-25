namespace MediaDownloader.Shared.Enums;

public enum JobStatus
{
    Pending,
    Searching,
    Found,
    AddingToRd,
    WaitingForRd,
    Downloading,
    Organizing,
    Complete,
    Failed,
    Cancelled
}

public static class JobStatusExtensions
{
    private static readonly HashSet<JobStatus> ActiveStatuses = new()
    {
        JobStatus.Pending,
        JobStatus.Searching,
        JobStatus.Found,
        JobStatus.AddingToRd,
        JobStatus.WaitingForRd,
        JobStatus.Downloading,
        JobStatus.Organizing
    };

    private static readonly HashSet<JobStatus> TerminalStatuses = new()
    {
        JobStatus.Complete,
        JobStatus.Failed,
        JobStatus.Cancelled
    };

    public static bool IsActive(this JobStatus status) => ActiveStatuses.Contains(status);
    public static bool IsTerminal(this JobStatus status) => TerminalStatuses.Contains(status);
    public static bool CanRetry(this JobStatus status) => status is JobStatus.Failed or JobStatus.Cancelled;
    public static bool CanCancel(this JobStatus status) => status.IsActive();

    public static string ToApiString(this JobStatus status) => status switch
    {
        JobStatus.Pending => "pending",
        JobStatus.Searching => "searching",
        JobStatus.Found => "found",
        JobStatus.AddingToRd => "adding_to_rd",
        JobStatus.WaitingForRd => "waiting_for_rd",
        JobStatus.Downloading => "downloading",
        JobStatus.Organizing => "organizing",
        JobStatus.Complete => "complete",
        JobStatus.Failed => "failed",
        JobStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    public static JobStatus FromApiString(string value) => value switch
    {
        "pending" => JobStatus.Pending,
        "searching" => JobStatus.Searching,
        "found" => JobStatus.Found,
        "adding_to_rd" => JobStatus.AddingToRd,
        "waiting_for_rd" => JobStatus.WaitingForRd,
        "downloading" => JobStatus.Downloading,
        "organizing" => JobStatus.Organizing,
        "complete" => JobStatus.Complete,
        "failed" => JobStatus.Failed,
        "cancelled" => JobStatus.Cancelled,
        _ => throw new ArgumentException($"Unknown job status: {value}")
    };
}
