namespace ADB_Explorer.Services;

/// <summary>
/// General-purpose access to the Windows Credential Vault (PasswordVault) for this app.
/// </summary>
/// <remarks>
/// Every operation is executed on a background thread with a hard timeout. The WinRT
/// <see cref="global::Windows.Security.Credentials.PasswordVault"/> can block for a long time (or
/// effectively hang) on some machines - e.g. when credentials roam via a Microsoft account or the
/// credential store is unhealthy. Running it inline on the UI thread would freeze the whole app
/// (issue #329), so callers are always shielded by <see cref="OperationTimeout"/>.
/// </remarks>
public static class CredentialVaultStore
{
    public const string Resource = "ADBExplorer";

    /// <summary>
    /// Maximum time to wait for a single Credential Vault operation before giving up.
    /// </summary>
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs a vault operation on a background thread, returning <paramref name="timeoutValue"/> when it
    /// does not finish within <see cref="OperationTimeout"/>. <paramref name="completed"/> reports whether
    /// the operation ran to completion (as opposed to timing out on an unresponsive vault).
    /// </summary>
    private static T Run<T>(Func<T> func, T timeoutValue, out bool completed)
    {
        try
        {
            var task = Task.Run(func);
            if (task.Wait(OperationTimeout))
            {
                completed = true;
                return task.Result;
            }
        }
        catch
        {
            // The operation ran but threw; the func's own try/catch normally prevents reaching here.
            completed = true;
            return timeoutValue;
        }

        // Timed out: the background task may still be blocked in WinRT, but the caller is not.
        completed = false;
        return timeoutValue;
    }

    /// <summary>
    /// Retrieves a stored value. <paramref name="value"/> is null when the credential is absent.
    /// Returns <see langword="false"/> only when the vault could not be reached within the timeout,
    /// which lets callers distinguish "no value" from "vault unavailable".
    /// </summary>
    public static bool TryGet(string userName, out string? value)
    {
        value = Run(() =>
        {
            try
            {
                var cred = new global::Windows.Security.Credentials.PasswordVault().Retrieve(Resource, userName);
                cred.RetrievePassword();
                return string.IsNullOrEmpty(cred.Password) ? null : cred.Password;
            }
            catch
            {
                return null;
            }
        }, null, out bool completed);

        return completed;
    }

    public static string? Get(string userName)
    {
        TryGet(userName, out var value);
        return value;
    }

    public static void Set(string userName, string value) => Run<object?>(() =>
    {
        try
        {
            RemoveCore(userName);
            new global::Windows.Security.Credentials.PasswordVault()
                .Add(new global::Windows.Security.Credentials.PasswordCredential(Resource, userName, value));
        }
        catch
        { }

        return null;
    }, null, out _);

    public static void Remove(string userName) => Run<object?>(() =>
    {
        RemoveCore(userName);
        return null;
    }, null, out _);

    private static void RemoveCore(string userName)
    {
        try
        {
            var vault = new global::Windows.Security.Credentials.PasswordVault();
            vault.Remove(vault.Retrieve(Resource, userName));
        }
        catch
        { }
    }

    public static bool Exists(string userName) => Get(userName) is not null;

    public static void ClearAll() => Run<object?>(() =>
    {
        try
        {
            var vault = new global::Windows.Security.Credentials.PasswordVault();
            foreach (var cred in vault.FindAllByResource(Resource))
                vault.Remove(cred);
        }
        catch
        { }

        return null;
    }, null, out _);
}
