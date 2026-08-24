using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dami.Capabilities.Skills;

internal static partial class AtomicDirectoryExchange
{
    private const int AT_CURRENT_WORKING_DIRECTORY = -100;
    private const uint RENAME_EXCHANGE = 2;

    public static void Exchange(string first, string second)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Atomic skill revision requires Linux renameat2 support.");
        }

        int result = RenameAt2(
            AT_CURRENT_WORKING_DIRECTORY, first,
            AT_CURRENT_WORKING_DIRECTORY, second,
            RENAME_EXCHANGE);
        if (result != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new IOException("Atomic skill directory exchange failed.", new Win32Exception(error));
        }
    }

    [LibraryImport(
        "libc",
        EntryPoint = "renameat2",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameAt2(
        int oldDirectory,
        string oldPath,
        int newDirectory,
        string newPath,
        uint flags);
}
