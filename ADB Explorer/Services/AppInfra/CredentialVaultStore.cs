namespace ADB_Explorer.Services;

/// <summary>
/// General-purpose access to the Windows Credential Vault (PasswordVault) for this app.
/// </summary>
public static class CredentialVaultStore
{
    public const string Resource = "ADBExplorer";

    public static string? Get(string userName)
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
    }

    public static void Set(string userName, string value)
    {
        try
        {
            Remove(userName);
            new global::Windows.Security.Credentials.PasswordVault()
                .Add(new global::Windows.Security.Credentials.PasswordCredential(Resource, userName, value));
        }
        catch
        { }
    }

    public static void Remove(string userName)
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

    public static void ClearAll()
    {
        try
        {
            var vault = new global::Windows.Security.Credentials.PasswordVault();
            foreach (var cred in vault.FindAllByResource(Resource))
                vault.Remove(cred);
        }
        catch
        { }
    }
}
