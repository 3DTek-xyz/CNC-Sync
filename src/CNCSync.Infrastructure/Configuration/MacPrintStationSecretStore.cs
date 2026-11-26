using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CNCSync.Core.Configuration;

namespace CNCSync.Infrastructure.Configuration;

[SupportedOSPlatform("macos")]
internal sealed class MacPrintStationSecretStore : ISecretStore
{
    private const string ServiceName = "ProCut Suite Desktop Print Sharing";
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;
    private const int MaxSecretBytes = 64 * 1024;

    public string? GetSecret(string key)
    {
        using var query = CreateQuery(key);
        query.Set(Native.ReturnData, Native.True);
        var status = Native.SecItemCopyMatching(query.Handle, out var data);
        try
        {
            if (status == ItemNotFound)
            {
                return null;
            }

            CheckStatus(status);
            if (data == IntPtr.Zero)
            {
                throw Unavailable();
            }

            var length = Native.CFDataGetLength(data);
            if (length is < 1 or > MaxSecretBytes)
            {
                throw Unavailable();
            }

            var bytes = new byte[(int)length];
            try
            {
                Marshal.Copy(Native.CFDataGetBytePtr(data), bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                Native.CFRelease(data);
            }
        }
    }

    public void SetSecret(string key, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            if (bytes.Length > MaxSecretBytes)
            {
                throw Unavailable();
            }

            using var data = new CfValue(Native.CFDataCreate(IntPtr.Zero, bytes, bytes.Length));
            using var item = CreateQuery(key);
            item.Set(Native.ValueData, data.Handle);
            var status = Native.SecItemAdd(item.Handle, IntPtr.Zero);
            if (status != DuplicateItem)
            {
                CheckStatus(status);
                return;
            }

            using var replacement = new CfDictionary();
            replacement.Set(Native.ValueData, data.Handle);
            using var query = CreateQuery(key);
            CheckStatus(Native.SecItemUpdate(query.Handle, replacement.Handle));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void DeleteSecret(string key)
    {
        using var query = CreateQuery(key);
        var status = Native.SecItemDelete(query.Handle);
        if (status != ItemNotFound)
        {
            CheckStatus(status);
        }
    }

    private static CfDictionary CreateQuery(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        using var account = CfValue.CreateString(key);
        using var service = CfValue.CreateString(ServiceName);

        var query = new CfDictionary();
        query.Set(Native.Class, Native.GenericPassword);
        query.Set(Native.Account, account.Handle);
        query.Set(Native.Service, service.Handle);
        return query;
    }

    private static void CheckStatus(int status)
    {
        if (status != 0)
        {
            throw Unavailable();
        }
    }

    private static InvalidOperationException Unavailable() =>
        new("Could not access the macOS Keychain for the print station certificate.");

    private class CfValue : IDisposable
    {
        public CfValue(IntPtr handle)
        {
            Handle = handle != IntPtr.Zero ? handle : throw Unavailable();
        }

        public IntPtr Handle { get; }

        public static CfValue CreateString(string value)
        {
            const uint utf8Encoding = 0x08000100;
            var bytes = Encoding.UTF8.GetBytes(value);
            return new(Native.CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, utf8Encoding, 0));
        }

        public void Dispose() => Native.CFRelease(Handle);
    }

    private sealed class CfDictionary() : CfValue(Native.CFDictionaryCreateMutable(
        IntPtr.Zero, 0, Native.DictionaryKeyCallbacks, Native.DictionaryValueCallbacks))
    {
        public void Set(IntPtr key, IntPtr value) => Native.CFDictionarySetValue(Handle, key, value);
    }

    private static class Native
    {
        private static readonly IntPtr SecurityLibrary = NativeLibrary.Load(SecurityFramework);
        private static readonly IntPtr FoundationLibrary = NativeLibrary.Load(CoreFoundation);

        internal static readonly IntPtr Class = SecurityConstant("kSecClass");
        internal static readonly IntPtr GenericPassword = SecurityConstant("kSecClassGenericPassword");
        internal static readonly IntPtr Account = SecurityConstant("kSecAttrAccount");
        internal static readonly IntPtr Service = SecurityConstant("kSecAttrService");
        internal static readonly IntPtr ValueData = SecurityConstant("kSecValueData");
        internal static readonly IntPtr ReturnData = SecurityConstant("kSecReturnData");
        internal static readonly IntPtr True = Marshal.ReadIntPtr(NativeLibrary.GetExport(FoundationLibrary, "kCFBooleanTrue"));
        internal static readonly IntPtr DictionaryKeyCallbacks = NativeLibrary.GetExport(FoundationLibrary, "kCFTypeDictionaryKeyCallBacks");
        internal static readonly IntPtr DictionaryValueCallbacks = NativeLibrary.GetExport(FoundationLibrary, "kCFTypeDictionaryValueCallBacks");

        private static IntPtr SecurityConstant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityLibrary, name));

        [DllImport(SecurityFramework)]
        internal static extern int SecItemAdd(IntPtr attributes, IntPtr result);

        [DllImport(SecurityFramework)]
        internal static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

        [DllImport(SecurityFramework)]
        internal static extern int SecItemUpdate(IntPtr query, IntPtr attributes);

        [DllImport(SecurityFramework)]
        internal static extern int SecItemDelete(IntPtr query);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFStringCreateWithBytes(IntPtr allocator, byte[] bytes, nint length, uint encoding, byte isExternalRepresentation);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

        [DllImport(CoreFoundation)]
        internal static extern nint CFDataGetLength(IntPtr data);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(CoreFoundation)]
        internal static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);

        [DllImport(CoreFoundation)]
        internal static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

        [DllImport(CoreFoundation)]
        internal static extern void CFRelease(IntPtr value);
    }
}
