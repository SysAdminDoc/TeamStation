using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TeamStation.Launcher;

/// <summary>
/// Thin <c>WinVerifyTrust</c> wrapper used by
/// <see cref="TeamViewerBinaryProvenanceInspector"/> to classify the
/// Authenticode state of a file. Returns one of
/// <see cref="TeamViewerSignatureState.Trusted"/> /
/// <see cref="TeamViewerSignatureState.Untrusted"/> /
/// <see cref="TeamViewerSignatureState.Unsigned"/> /
/// <see cref="TeamViewerSignatureState.UnableToVerify"/> rather than raw
/// HRESULTs so the caller can branch on intent.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WinTrust
{
    public static TeamViewerSignatureState Verify(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return TeamViewerSignatureState.NotApplicable;

        try
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = filePath,
            };

            var fileInfoPtr = Marshal.AllocHGlobal((int)fileInfo.cbStruct);
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    dwStateAction = WTD_STATEACTION_IGNORE,
                    pFile = fileInfoPtr,
                };

                var policyGuid = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                var hr = WinVerifyTrust(IntPtr.Zero, ref policyGuid, ref data);
                return MapHResult(hr);
            }
            finally
            {
                Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfoPtr);
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
        catch (DllNotFoundException)
        {
            return TeamViewerSignatureState.UnableToVerify;
        }
        catch
        {
            return TeamViewerSignatureState.UnableToVerify;
        }
    }

    private static TeamViewerSignatureState MapHResult(uint hr) => hr switch
    {
        0u => TeamViewerSignatureState.Trusted,
        // TRUST_E_NOSIGNATURE — file is not signed at all.
        0x800B0100u => TeamViewerSignatureState.Unsigned,
        // TRUST_E_PROVIDER_UNKNOWN / no providers loaded for this object.
        0x800B0001u => TeamViewerSignatureState.UnableToVerify,
        // TRUST_E_SUBJECT_FORM_UNKNOWN — verifier can't recognise file as signable.
        0x800B0003u => TeamViewerSignatureState.UnableToVerify,
        // TRUST_E_SUBJECT_NOT_TRUSTED, TRUST_E_BAD_DIGEST, CERT_E_REVOKED, etc.
        // Anything non-zero that's not the unsigned / unknown shapes above is
        // treated as a real "untrusted" verdict.
        _ => TeamViewerSignatureState.Untrusted,
    };

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;

    private static Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = false, ExactSpelling = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
