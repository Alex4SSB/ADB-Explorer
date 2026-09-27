namespace ADB_Explorer.Models;

public sealed record ShellIdentity(string UserName, int Uid, int Gid, IReadOnlySet<int> Groups)
{
    public bool IsRoot => Uid == 0;
}
