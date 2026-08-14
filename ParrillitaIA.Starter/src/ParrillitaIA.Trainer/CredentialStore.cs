using System.Runtime.InteropServices;

namespace ParrillitaIA.Trainer;

internal static class CredentialStore
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref CREDENTIAL credential,
        uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    public static void Save(
        string target,
        string username,
        string password)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Target vacío.", nameof(target));

        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Usuario vacío.", nameof(username));

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Contraseña vacía.", nameof(password));

        var passwordBytes =
            System.Text.Encoding.Unicode.GetBytes(password);

        var blob = Marshal.AllocCoTaskMem(passwordBytes.Length);

        try
        {
            Marshal.Copy(
                passwordBytes,
                0,
                blob,
                passwordBytes.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = username,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "No se pudo guardar la credencial en Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static bool TryRead(
        string target,
        out string username,
        out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (!CredRead(
                target,
                CRED_TYPE_GENERIC,
                0,
                out var ptr))
        {
            return false;
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<CREDENTIAL>(ptr);

            username =
                credential.UserName ?? string.Empty;

            if (credential.CredentialBlob != IntPtr.Zero &&
                credential.CredentialBlobSize > 0)
            {
                password =
                    Marshal.PtrToStringUni(
                        credential.CredentialBlob,
                        (int)credential.CredentialBlobSize / 2)
                    ?? string.Empty;
            }

            return !string.IsNullOrWhiteSpace(username) &&
                   !string.IsNullOrEmpty(password);
        }
        finally
        {
            CredFree(ptr);
        }
    }
}
