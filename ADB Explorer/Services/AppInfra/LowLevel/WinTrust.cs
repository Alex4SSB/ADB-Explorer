namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    public sealed partial class WinTrust
    {
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(
            HANDLE hwnd,
            ref Guid pgActionID,
            ref WINTRUST_DATA pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcwszFilePath;
            public nint hFile;
            public nint pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public nint pPolicyCallbackData;
            public nint pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public nint pFile;
            public uint dwStateAction;
            public nint hWVTStateData;
            public nint pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public nint pSignatureSettings;
        }

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;

        /// <summary>
        /// Verifies the Authenticode signature of a file using WinVerifyTrust.
        /// No revocation check is performed (fully offline).
        /// </summary>
        /// <returns><see langword="true"/> if the signature is valid and the certificate chain is trusted.</returns>
        public static bool VerifyEmbeddedSignature(string filePath)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = filePath,
            };

            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                };

                var guid = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                return WinVerifyTrust(nint.Zero, ref guid, ref data) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(pFile);
            }
        }
    }
}
