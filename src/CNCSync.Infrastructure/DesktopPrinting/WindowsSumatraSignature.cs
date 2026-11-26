using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace CNCSync.Infrastructure.DesktopPrinting;

[SupportedOSPlatform("windows")]
internal static class WindowsSumatraSignature
{
    private const uint VerifyState = 1;
    private const uint CloseState = 2;
    private const uint NoDialog = 2;
    private const uint VerifyFile = 1;
    private const string Publisher = "Krzysztof Kowalczyk";
    private const string CommonNameOid = "2.5.4.3";
    private const string OrganizationOid = "2.5.4.10";

    internal static bool IsTrusted(FileStream executable)
    {
        var file = new TrustFile
        {
            Size = (uint)Marshal.SizeOf<TrustFile>(),
            Path = Marshal.StringToCoTaskMemUni(executable.Name),
            Handle = executable.SafeFileHandle.DangerousGetHandle()
        };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            return Verify(filePointer);
        }
        finally
        {
            GC.KeepAlive(executable);
            Marshal.FreeHGlobal(filePointer);
            Marshal.FreeCoTaskMem(file.Path);
        }
    }

    private static bool Verify(IntPtr filePointer)
    {
        var policy = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(),
            UiChoice = NoDialog,
            UnionChoice = VerifyFile,
            File = filePointer,
            StateAction = VerifyState
        };
        try
        {
            return WinVerifyTrust(new IntPtr(-1), ref policy, ref data) == 0 &&
                HasExpectedPublisher(data.StateData);
        }
        finally
        {
            data.StateAction = CloseState;
            WinVerifyTrust(new IntPtr(-1), ref policy, ref data);
        }
    }

    private static bool HasExpectedPublisher(IntPtr trustState)
    {
        var providerData = WTHelperProvDataFromStateData(trustState);
        if (providerData == IntPtr.Zero)
        {
            return false;
        }

        var signer = WTHelperGetProvSignerFromChain(providerData, 0, false, 0);
        if (signer == IntPtr.Zero)
        {
            return false;
        }

        // Use the publisher of the signature Windows just verified, rather than a
        // certificate independently extracted from the executable.
        var providerCertificate = WTHelperGetProvCertFromChain(signer, 0);
        if (providerCertificate == IntPtr.Zero)
        {
            return false;
        }

        var certificateContext = Marshal.PtrToStructure<TrustCertificate>(providerCertificate).Context;
        if (certificateContext == IntPtr.Zero)
        {
            return false;
        }

        using var certificate = new X509Certificate2(certificateContext);
        var attributes = certificate.SubjectName.EnumerateRelativeDistinguishedNames()
            .Where(attribute => !attribute.HasMultipleElements)
            .ToLookup(attribute => attribute.GetSingleElementType().Value, attribute => attribute.GetSingleElementValue());
        return attributes[CommonNameOid].SequenceEqual([Publisher], StringComparer.Ordinal) &&
            attributes[OrganizationOid].SequenceEqual([Publisher], StringComparer.Ordinal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustFile
    {
        internal uint Size;
        internal IntPtr Path;
        internal IntPtr Handle;
        internal IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal uint Size;
        internal IntPtr PolicyCallbackData;
        internal IntPtr SipClientData;
        internal uint UiChoice;
        internal uint RevocationChecks;
        internal uint UnionChoice;
        internal IntPtr File;
        internal uint StateAction;
        internal IntPtr StateData;
        internal IntPtr UrlReference;
        internal uint ProviderFlags;
        internal uint UiContext;
        internal IntPtr SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustCertificate
    {
        internal uint Size;
        internal IntPtr Context;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr providerData, uint signerIndex, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);
}
