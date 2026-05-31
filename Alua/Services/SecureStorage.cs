using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using Windows.Storage;
// SecureStorage is the canonical type here; explicitly disambiguate from Windows.Security.Credentials.

namespace Alua.Services;

/// <summary>
/// Per-platform secure storage for small secrets (tokens, API keys).
/// Falls back to a permissioned local file on platforms without a native secret store.
/// </summary>
public static class SecureStorage
{
    // NOTE: ServiceName does not match the app bundle id (net.rarisma.gravity).
    // Do NOT change this value — changing it would orphan existing Keychain entries for existing users.
    private const string ServiceName = "net.rarisma.gravity";

    // Serializes concurrent Windows DPAPI and Linux/Android file-fallback read-modify-write operations.
    private static readonly SemaphoreSlim _ioLock = new(1, 1);

    public static async Task<string?> GetAsync(string key)
    {
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
                return KeychainGet(key);

            await _ioLock.WaitAsync();
            try
            {
                if (OperatingSystem.IsWindows())
                    return WindowsGet(key);
                return FileFallbackGet(key);
            }
            finally
            {
                _ioLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SecureStorage.Get failed for {Key}", key);
            return null;
        }
    }

    public static async Task SetAsync(string key, string? value)
    {
        try
        {
            if (value is null)
            {
                await DeleteAsync(key);
                return;
            }

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
            {
                KeychainSet(key, value);
                return;
            }

            await _ioLock.WaitAsync();
            try
            {
                if (OperatingSystem.IsWindows())
                    WindowsSet(key, value);
                else
                    FileFallbackSet(key, value);
            }
            finally
            {
                _ioLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SecureStorage.Set failed for {Key}", key);
        }
    }

    public static async Task DeleteAsync(string key)
    {
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
            {
                KeychainDelete(key);
                return;
            }

            await _ioLock.WaitAsync();
            try
            {
                if (OperatingSystem.IsWindows())
                    WindowsDelete(key);
                else
                    FileFallbackDelete(key);
            }
            finally
            {
                _ioLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SecureStorage.Delete failed for {Key}", key);
        }
    }

    #region macOS / iOS Keychain

    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(SecurityFramework)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemDelete(IntPtr query);

    [DllImport(SecurityFramework)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr alloc, byte[] bytes, long length);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr alloc, long capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundation)]
    private static extern void CFDictionaryAddValue(IntPtr dict, IntPtr key, IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern long CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFRetain(IntPtr cf);

    [DllImport(CoreFoundation, EntryPoint = "CFStringGetTypeID")]
    private static extern long CFStringGetTypeID();

    // Constants from Security.framework / CoreFoundation. CFStringRef constants are exported
    // as pointers (kSecClass et al.), so dereference once. Struct constants like the type
    // callbacks are exported as the struct itself — pass the symbol address directly.
    private static readonly Lazy<IntPtr> s_securityHandle = new(() => NativeLibrary.Load(SecurityFramework));
    private static readonly Lazy<IntPtr> s_coreFoundationHandle = new(() => NativeLibrary.Load(CoreFoundation));

    private static IntPtr ReadCFConst(Lazy<IntPtr> handle, string name)
        => Marshal.ReadIntPtr(NativeLibrary.GetExport(handle.Value, name));

    private static IntPtr GetStructAddress(Lazy<IntPtr> handle, string name)
        => NativeLibrary.GetExport(handle.Value, name);

    private static readonly Lazy<IntPtr> s_kSecClass = new(() => ReadCFConst(s_securityHandle, "kSecClass"));
    private static readonly Lazy<IntPtr> s_kSecClassGenericPassword = new(() => ReadCFConst(s_securityHandle, "kSecClassGenericPassword"));
    private static readonly Lazy<IntPtr> s_kSecAttrService = new(() => ReadCFConst(s_securityHandle, "kSecAttrService"));
    private static readonly Lazy<IntPtr> s_kSecAttrAccount = new(() => ReadCFConst(s_securityHandle, "kSecAttrAccount"));
    private static readonly Lazy<IntPtr> s_kSecValueData = new(() => ReadCFConst(s_securityHandle, "kSecValueData"));
    private static readonly Lazy<IntPtr> s_kSecReturnData = new(() => ReadCFConst(s_securityHandle, "kSecReturnData"));
    private static readonly Lazy<IntPtr> s_kSecMatchLimit = new(() => ReadCFConst(s_securityHandle, "kSecMatchLimit"));
    private static readonly Lazy<IntPtr> s_kSecMatchLimitOne = new(() => ReadCFConst(s_securityHandle, "kSecMatchLimitOne"));
    private static readonly Lazy<IntPtr> s_kCFBooleanTrue = new(() => ReadCFConst(s_coreFoundationHandle, "kCFBooleanTrue"));
    private static readonly Lazy<IntPtr> s_kCFTypeDictionaryKeyCallBacks = new(() => GetStructAddress(s_coreFoundationHandle, "kCFTypeDictionaryKeyCallBacks"));
    private static readonly Lazy<IntPtr> s_kCFTypeDictionaryValueCallBacks = new(() => GetStructAddress(s_coreFoundationHandle, "kCFTypeDictionaryValueCallBacks"));

    private static IntPtr KSecClass => s_kSecClass.Value;
    private static IntPtr KSecClassGenericPassword => s_kSecClassGenericPassword.Value;
    private static IntPtr KSecAttrService => s_kSecAttrService.Value;
    private static IntPtr KSecAttrAccount => s_kSecAttrAccount.Value;
    private static IntPtr KSecValueData => s_kSecValueData.Value;
    private static IntPtr KSecReturnData => s_kSecReturnData.Value;
    private static IntPtr KSecMatchLimit => s_kSecMatchLimit.Value;
    private static IntPtr KSecMatchLimitOne => s_kSecMatchLimitOne.Value;
    private static IntPtr KCFBooleanTrue => s_kCFBooleanTrue.Value;

    private static IntPtr MakeCFString(string s) => CFStringCreateWithCString(IntPtr.Zero, s, 0x08000100 /* kCFStringEncodingUTF8 */);

    private static IntPtr CreateCFTypeDictionary()
        => CFDictionaryCreateMutable(IntPtr.Zero, 0, s_kCFTypeDictionaryKeyCallBacks.Value, s_kCFTypeDictionaryValueCallBacks.Value);

    private static IntPtr MakeBaseQuery(string key)
    {
        var dict = CreateCFTypeDictionary();
        var service = MakeCFString(ServiceName);
        var account = MakeCFString(key);
        CFDictionaryAddValue(dict, KSecClass, KSecClassGenericPassword);
        CFDictionaryAddValue(dict, KSecAttrService, service);
        CFDictionaryAddValue(dict, KSecAttrAccount, account);
        CFRelease(service);
        CFRelease(account);
        return dict;
    }

    private static string? KeychainGet(string key)
    {
        var query = MakeBaseQuery(key);
        try
        {
            CFDictionaryAddValue(query, KSecReturnData, KCFBooleanTrue);
            CFDictionaryAddValue(query, KSecMatchLimit, KSecMatchLimitOne);
            var status = SecItemCopyMatching(query, out var result);
            if (status != 0 || result == IntPtr.Zero)
                return null;
            try
            {
                var len = (int)CFDataGetLength(result);
                var ptr = CFDataGetBytePtr(result);
                var bytes = new byte[len];
                Marshal.Copy(ptr, bytes, 0, len);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CFRelease(result);
            }
        }
        finally
        {
            CFRelease(query);
        }
    }

    private static void KeychainSet(string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var dataRef = CFDataCreate(IntPtr.Zero, bytes, bytes.Length);

        // Try update first; if not found, add.
        var query = MakeBaseQuery(key);
        var attrs = CreateCFTypeDictionary();
        try
        {
            CFDictionaryAddValue(attrs, KSecValueData, dataRef);
            var status = SecItemUpdate(query, attrs);
            if (status == -25300 /* errSecItemNotFound */)
            {
                // Add it
                CFRelease(query);
                query = MakeBaseQuery(key);
                CFDictionaryAddValue(query, KSecValueData, dataRef);
                status = SecItemAdd(query, IntPtr.Zero);
                if (status != 0)
                    Log.Warning("Keychain SecItemAdd failed for {Key}: {Status}", key, status);
            }
            else if (status != 0)
            {
                Log.Warning("Keychain SecItemUpdate failed for {Key}: {Status}", key, status);
            }
        }
        finally
        {
            CFRelease(attrs);
            CFRelease(query);
            CFRelease(dataRef);
        }
    }

    private static void KeychainDelete(string key)
    {
        var query = MakeBaseQuery(key);
        try
        {
            SecItemDelete(query);
        }
        finally
        {
            CFRelease(query);
        }
    }

    #endregion

    #region Windows DPAPI (via Win32 P/Invoke)

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    private static byte[] DpapiProtect(byte[] plain)
    {
        var inBlob = default(DATA_BLOB);
        var outBlob = default(DATA_BLOB);
        inBlob.pbData = Marshal.AllocHGlobal(plain.Length);
        try
        {
            Marshal.Copy(plain, 0, inBlob.pbData, plain.Length);
            inBlob.cbData = plain.Length;
            if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                throw new InvalidOperationException("CryptProtectData failed");
            var output = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, output, 0, outBlob.cbData);
            return output;
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero)
                LocalFree(outBlob.pbData);
        }
    }

    private static byte[] DpapiUnprotect(byte[] cipher)
    {
        var inBlob = default(DATA_BLOB);
        var outBlob = default(DATA_BLOB);
        inBlob.pbData = Marshal.AllocHGlobal(cipher.Length);
        try
        {
            Marshal.Copy(cipher, 0, inBlob.pbData, cipher.Length);
            inBlob.cbData = cipher.Length;
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                throw new InvalidOperationException("CryptUnprotectData failed");
            var output = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, output, 0, outBlob.cbData);
            return output;
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero)
                LocalFree(outBlob.pbData);
        }
    }

    private static string WindowsStorePath()
    {
        var folder = ApplicationData.Current.LocalFolder.Path;
        return Path.Combine(folder, "secrets.dat");
    }

    private static Dictionary<string, string> WindowsLoad()
    {
        var path = WindowsStorePath();
        if (!File.Exists(path))
            return new Dictionary<string, string>();
        try
        {
            var enc = File.ReadAllBytes(path);
            if (enc.Length == 0)
                return new Dictionary<string, string>();
            var dec = DpapiUnprotect(enc);
            var json = Encoding.UTF8.GetString(dec);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read Windows secret store; starting fresh");
            return new Dictionary<string, string>();
        }
    }

    private static void WindowsSave(Dictionary<string, string> data)
    {
        var path = WindowsStorePath();
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        var enc = DpapiProtect(bytes);
        File.WriteAllBytes(path, enc);
    }

    private static string? WindowsGet(string key)
    {
        var data = WindowsLoad();
        return data.TryGetValue(key, out var v) ? v : null;
    }

    private static void WindowsSet(string key, string value)
    {
        var data = WindowsLoad();
        data[key] = value;
        WindowsSave(data);
    }

    private static void WindowsDelete(string key)
    {
        var data = WindowsLoad();
        if (data.Remove(key))
            WindowsSave(data);
    }

    #endregion

    #region File fallback (Linux/Android)

    // Linux/Android fallback: AES-256-GCM with a per-install random salt and HKDF key derivation.
    // On-disk format (new): [12-byte nonce][16-byte GCM tag][ciphertext]
    // On-disk format (legacy): [16-byte CBC IV][ciphertext]  — detected by length heuristic and migrated on next write.
    //
    // The key is derived with HKDF(SHA-256, ikm=MachineName|UserName|const, salt=per-install random 32 bytes).
    // The per-install salt is stored in secrets.salt (chmod 0600 on Linux).
    // Not as strong as a real keystore, but far better than plaintext alongside Settings.json.

    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    // Minimum valid GCM blob: nonce + tag (empty plaintext is valid)
    private const int GcmMinSize = GcmNonceSize + GcmTagSize;
    // Legacy CBC minimum: 16-byte IV + at least 1 block (16 bytes) of ciphertext
    private const int CbcMinSize = 32;

    private static string FallbackPath()
    {
        var folder = ApplicationData.Current.LocalFolder.Path;
        return Path.Combine(folder, "secrets.bin");
    }

    private static string FallbackSaltPath()
    {
        var folder = ApplicationData.Current.LocalFolder.Path;
        return Path.Combine(folder, "secrets.salt");
    }

    /// <summary>
    /// Loads or generates the per-install 32-byte random salt stored in secrets.salt.
    /// </summary>
    private static byte[] GetOrCreateSalt()
    {
        var saltPath = FallbackSaltPath();
        if (File.Exists(saltPath))
        {
            try
            {
                var existing = File.ReadAllBytes(saltPath);
                if (existing.Length == 32)
                    return existing;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read secrets.salt; generating a new one (secrets will be re-encrypted)");
            }
        }

        var salt = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(saltPath, salt);
        if (OperatingSystem.IsLinux())
        {
            try { File.SetUnixFileMode(saltPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (Exception ex) { Log.Debug(ex, "Failed to chmod 0600 on {Path}", saltPath); }
        }
        return salt;
    }

    private static byte[] FallbackKey()
    {
        // IKM: stable per-machine/user seed — low entropy on its own, but combined with the
        // per-install random salt the derived key is cryptographically strong.
        var ikm = Encoding.UTF8.GetBytes(Environment.MachineName + "|" + Environment.UserName + "|alua-secure-v1");
        var salt = GetOrCreateSalt();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt);
    }

    /// <summary>
    /// Legacy AES-CBC key derived the old way (for backward-compat migration reads only).
    /// </summary>
    private static byte[] FallbackLegacyKey()
    {
        var seed = Environment.MachineName + "|" + Environment.UserName + "|alua-secure-v1";
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
    }

    private static Dictionary<string, string> FallbackLoad()
    {
        var path = FallbackPath();
        if (!File.Exists(path))
            return new Dictionary<string, string>();
        try
        {
            var raw = File.ReadAllBytes(path);

            // Try GCM first (new format: nonce[12] + tag[16] + ciphertext)
            if (raw.Length >= GcmMinSize)
            {
                try
                {
                    var key = FallbackKey();
                    var nonce = new byte[GcmNonceSize];
                    var tag = new byte[GcmTagSize];
                    var cipherLen = raw.Length - GcmNonceSize - GcmTagSize;
                    var cipher = new byte[cipherLen];
                    var plain = new byte[cipherLen];

                    Array.Copy(raw, 0, nonce, 0, GcmNonceSize);
                    Array.Copy(raw, GcmNonceSize, tag, 0, GcmTagSize);
                    Array.Copy(raw, GcmNonceSize + GcmTagSize, cipher, 0, cipherLen);

                    using var aesGcm = new AesGcm(key, GcmTagSize);
                    aesGcm.Decrypt(nonce, cipher, tag, plain);

                    var json = Encoding.UTF8.GetString(plain);
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                           ?? new Dictionary<string, string>();
                }
                catch (CryptographicException)
                {
                    // GCM auth failed — may be legacy CBC format; fall through to legacy path.
                    Log.Debug("GCM decryption failed; attempting legacy CBC fallback");
                }
            }

            // Legacy AES-CBC read (backward compat): [16-byte IV][ciphertext]
            if (raw.Length >= CbcMinSize)
            {
                try
                {
                    using var aes = Aes.Create();
                    aes.Key = FallbackLegacyKey();
                    var iv = new byte[16];
                    Array.Copy(raw, 0, iv, 0, 16);
                    aes.IV = iv;
                    using var dec = aes.CreateDecryptor();
                    var data = dec.TransformFinalBlock(raw, 16, raw.Length - 16);
                    var json = Encoding.UTF8.GetString(data);
                    var result = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                                 ?? new Dictionary<string, string>();
                    Log.Information("Loaded legacy CBC secrets; they will be re-encrypted as GCM on next write");
                    return result;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Legacy CBC decryption also failed; starting with empty secret store");
                }
            }

            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read fallback secret store; starting fresh");
            return new Dictionary<string, string>();
        }
    }

    private static void FallbackSave(Dictionary<string, string> data)
    {
        var path = FallbackPath();
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var plain = Encoding.UTF8.GetBytes(json);

        var key = FallbackKey();
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var tag = new byte[GcmTagSize];
        var cipher = new byte[plain.Length];

        using var aesGcm = new AesGcm(key, GcmTagSize);
        aesGcm.Encrypt(nonce, plain, cipher, tag);

        // Layout: [nonce (12)][tag (16)][ciphertext]
        var output = new byte[GcmNonceSize + GcmTagSize + cipher.Length];
        Array.Copy(nonce, 0, output, 0, GcmNonceSize);
        Array.Copy(tag, 0, output, GcmNonceSize, GcmTagSize);
        Array.Copy(cipher, 0, output, GcmNonceSize + GcmTagSize, cipher.Length);

        File.WriteAllBytes(path, output);

        if (OperatingSystem.IsLinux())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (Exception ex) { Log.Debug(ex, "Failed to chmod 0600 on {Path}", path); }
        }
    }

    private static string? FileFallbackGet(string key)
    {
        var data = FallbackLoad();
        return data.TryGetValue(key, out var v) ? v : null;
    }

    private static void FileFallbackSet(string key, string value)
    {
        var data = FallbackLoad();
        data[key] = value;
        FallbackSave(data);
    }

    private static void FileFallbackDelete(string key)
    {
        var data = FallbackLoad();
        if (data.Remove(key))
            FallbackSave(data);
    }

    #endregion
}
