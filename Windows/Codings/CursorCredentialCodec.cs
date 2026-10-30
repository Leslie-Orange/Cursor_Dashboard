using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// Matches Cursor's cursorAuthTokenStorageCodec: plaintext or Electron safeStorage v1.
// Reads only this user's Cursor key; credentials and decoded keys stay in memory.
internal static class CursorCredentialCodec
{
    internal const string Prefix = "cursor-safe-storage:v1:";

    public static string Decode(string value)
    {
        if (value == null) return null;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return value.StartsWith("cursor-safe-storage:", StringComparison.Ordinal) ? null : value;
        byte[] payload = null;
        byte[] key = null;
        byte[] plain = null;
        try
        {
            payload = Convert.FromBase64String(value.Substring(Prefix.Length));
            if (payload.Length < 31 || payload[0] != (byte)'v' || payload[1] != (byte)'1' || payload[2] != (byte)'0') return null;
            key = LoadKey();
            if (key == null) return null;
            plain = DecryptPayload(payload, key);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        catch (Exception) { return null; }
        finally
        {
            Clear(payload);
            Clear(key);
            Clear(plain);
        }
    }

    private static byte[] LoadKey()
    {
        string[] roots = new string[] {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        foreach (string root in roots)
        {
            byte[] wrapped = null;
            byte[] protectedKey = null;
            try
            {
                string path = Path.Combine(root, "Cursor", "Local State");
                if (!File.Exists(path)) continue;
                Dictionary<string, object> state = JsonValue.Map(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)));
                string encoded = JsonValue.Text(JsonValue.Get(JsonValue.Nested(state, "os_crypt"), "encrypted_key"));
                if (encoded == null) continue;
                wrapped = Convert.FromBase64String(encoded);
                if (wrapped.Length <= 5 || Encoding.ASCII.GetString(wrapped, 0, 5) != "DPAPI") continue;
                protectedKey = new byte[wrapped.Length - 5];
                Buffer.BlockCopy(wrapped, 5, protectedKey, 0, protectedKey.Length);
                return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
            }
            catch (Exception) { }
            finally { Clear(wrapped); Clear(protectedKey); }
        }
        return null;
    }

    internal static byte[] DecryptPayload(byte[] payload, byte[] key)
    {
        if (payload == null || payload.Length < 31 || payload[0] != (byte)'v' || payload[1] != (byte)'1' || payload[2] != (byte)'0')
            throw new CryptographicException("不支持的 Cursor 凭证格式");
        byte[] nonce = new byte[12];
        byte[] tag = new byte[16];
        byte[] cipher = new byte[payload.Length - 31];
        byte[] plain = new byte[cipher.Length];
        IntPtr algorithm = IntPtr.Zero;
        IntPtr symmetricKey = IntPtr.Zero;
        GCHandle nonceHandle = new GCHandle();
        GCHandle tagHandle = new GCHandle();
        bool success = false;
        try
        {
            Buffer.BlockCopy(payload, 3, nonce, 0, nonce.Length);
            Buffer.BlockCopy(payload, 15, cipher, 0, cipher.Length);
            Buffer.BlockCopy(payload, payload.Length - tag.Length, tag, 0, tag.Length);
            Check(BCryptOpenAlgorithmProvider(out algorithm, "AES", null, 0));
            byte[] mode = Encoding.Unicode.GetBytes("ChainingModeGCM\0");
            Check(BCryptSetProperty(algorithm, "ChainingMode", mode, mode.Length, 0));
            Check(BCryptGenerateSymmetricKey(algorithm, out symmetricKey, IntPtr.Zero, 0, key, key.Length, 0));
            nonceHandle = GCHandle.Alloc(nonce, GCHandleType.Pinned);
            tagHandle = GCHandle.Alloc(tag, GCHandleType.Pinned);
            AuthInfo auth = new AuthInfo();
            auth.Size = Marshal.SizeOf(typeof(AuthInfo));
            auth.Version = 1;
            auth.Nonce = nonceHandle.AddrOfPinnedObject();
            auth.NonceLength = nonce.Length;
            auth.Tag = tagHandle.AddrOfPinnedObject();
            auth.TagLength = tag.Length;
            int written;
            Check(BCryptDecrypt(symmetricKey, cipher, cipher.Length, ref auth, IntPtr.Zero, 0, plain, plain.Length, out written, 0));
            if (written != plain.Length) throw new CryptographicException("Cursor 凭证长度无效");
            success = true;
            return plain;
        }
        finally
        {
            if (nonceHandle.IsAllocated) nonceHandle.Free();
            if (tagHandle.IsAllocated) tagHandle.Free();
            if (symmetricKey != IntPtr.Zero) BCryptDestroyKey(symmetricKey);
            if (algorithm != IntPtr.Zero) BCryptCloseAlgorithmProvider(algorithm, 0);
            Clear(nonce); Clear(tag); Clear(cipher);
            if (!success) Clear(plain);
        }
    }

    private static void Clear(byte[] value) { if (value != null) Array.Clear(value, 0, value.Length); }
    private static void Check(int status) { if (status != 0) throw new CryptographicException("Windows 凭证解密失败"); }

    [StructLayout(LayoutKind.Sequential)]
    private struct AuthInfo
    {
        public int Size;
        public int Version;
        public IntPtr Nonce;
        public int NonceLength;
        public IntPtr AuthData;
        public int AuthDataLength;
        public IntPtr Tag;
        public int TagLength;
        public IntPtr MacContext;
        public int MacContextLength;
        public int AadLength;
        public long DataLength;
        public int Flags;
    }

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm, string algorithmId, string implementation, int flags);
    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptSetProperty(IntPtr handle, string property, byte[] value, int length, int flags);
    [DllImport("bcrypt.dll")]
    private static extern int BCryptGenerateSymmetricKey(IntPtr algorithm, out IntPtr key, IntPtr keyObject, int keyObjectLength, byte[] secret, int secretLength, int flags);
    [DllImport("bcrypt.dll")]
    private static extern int BCryptDecrypt(IntPtr key, byte[] input, int inputLength, ref AuthInfo authInfo, IntPtr iv, int ivLength, [Out] byte[] output, int outputLength, out int resultLength, int flags);
    [DllImport("bcrypt.dll")]
    private static extern int BCryptDestroyKey(IntPtr key);
    [DllImport("bcrypt.dll")]
    private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, int flags);
}
