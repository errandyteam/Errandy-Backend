namespace Errandy.Application.Common.Utils;

public static class ErrandTimings
{
    /// <summary>
    /// How long a customer has to confirm after the runner submits proof. After this the
    /// errand is auto-released to the runner. PRD says 2-6 hours; 6 is used.
    /// </summary>
    public static readonly TimeSpan CustomerConfirmationWindow = TimeSpan.FromHours(6);
}