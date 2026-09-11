using System.Security.Cryptography;
using System.Text.Json;

namespace SpotMonitor.Infrastructure;

// Uses the existing .NET PBKDF2 implementation; legacy hashes remain readable.
public sealed class WebSecurity
{
    private const int CurrentIterations = 600_000;
    private readonly string _path;
    private readonly string _legacyPasswordPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile SecurityFile _state;
    private WebSecurity(string path, string legacyPasswordPath, SecurityFile state)
        => (_path, _legacyPasswordPath, _state) = (path, legacyPasswordPath, state);
    public bool PasswordChangeRequired => _state.PasswordChangeRequired;
    public string SessionVersion => _state.SessionVersion;
    public bool Verify(string password)
    {
        var state = _state;
        if (password.Length > 1024) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(state.Hash),
            Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(state.Salt), state.Iterations, HashAlgorithmName.SHA256, 32));
    }
    public async Task<bool> ChangeAsync(string currentPassword, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new PasswordValidationException("Enter a new password; it cannot be blank.");
        if (password.Length > 1024)
            throw new PasswordValidationException("The new password must be 1024 characters or fewer.");
        if (password == "admin")
            throw new PasswordValidationException("The new password cannot be the initial password admin.");
        if (password == currentPassword)
            throw new PasswordValidationException("The new password must differ from your current password.");
        await _gate.WaitAsync();
        try
        {
            if (!Verify(currentPassword)) return false;
            var state = CreateState(password, false);
            await SaveAsync(_path, state);
            _state = state;
            if (File.Exists(_legacyPasswordPath)) File.Delete(_legacyPasswordPath);
            return true;
        }
        finally { _gate.Release(); }
    }
    public static async Task<WebSecurity> LoadOrCreateAsync(string path, string legacyPasswordPath)
    {
        SecurityFile state;
        if (File.Exists(path))
        {
            state = JsonSerializer.Deserialize<SecurityFile>(await File.ReadAllTextAsync(path))
                ?? throw new InvalidDataException("Invalid administrator security state.");
            if (state.Iterations is < 210_000 or > 2_000_000 || Convert.FromBase64String(state.Salt).Length != 16 || Convert.FromBase64String(state.Hash).Length != 32)
                throw new InvalidDataException("Invalid administrator security state.");
            // Legacy accounts must change their password, without resetting their password to admin.
            if (string.IsNullOrEmpty(state.SessionVersion))
            {
                state = state with { PasswordChangeRequired = true, SessionVersion = Guid.NewGuid().ToString("N") };
                await SaveAsync(path, state);
            }
        }
        else
        {
            state = CreateState("admin", true);
            await SaveAsync(path, state);
        }
        if (File.Exists(legacyPasswordPath)) File.Delete(legacyPasswordPath);
        return new(path, legacyPasswordPath, state);
    }
    private static SecurityFile CreateState(string password, bool required)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, CurrentIterations, HashAlgorithmName.SHA256, 32);
        return new(Convert.ToBase64String(salt), Convert.ToBase64String(hash), CurrentIterations, required, Guid.NewGuid().ToString("N"));
    }
    private static async Task SaveAsync(string path, SecurityFile state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, path, true);
    }
    private sealed record SecurityFile(string Salt, string Hash, int Iterations = 210_000,
        bool PasswordChangeRequired = true, string SessionVersion = "");
}

public sealed class PasswordValidationException(string message) : Exception(message);
