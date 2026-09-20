namespace RTSPView.Controller;

// Acknowledgement confirms command handling, not decoded video or priority ownership.
public sealed record AutomationDeliveryStatus(DateTimeOffset AttemptedAt, bool Success, string Message);

public static class AutomationRevision
{
    public static string For<T>(T settings) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(settings)));
    public static void Check(string? expected, string current)
    {
        if (expected is not null && expected != current) throw new InvalidDataException("Saved settings changed in another page or in Automation Priority. Your draft was kept. Discard and reload before saving again.");
    }
}
