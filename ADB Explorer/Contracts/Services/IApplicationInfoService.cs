using System;

namespace ADB_Explorer.Contracts.Services
{
    public interface IApplicationInfoService
    {
        Version GetVersion();
    }
}
