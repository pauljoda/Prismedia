using System.Runtime.InteropServices;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Creates filesystem hard links — the primitive behind hardlink imports (the library file and the
/// still-seeding download share one inode, so the "copy" is instant and costs no space). The BCL has
/// no hard-link API, so this wraps <c>link(2)</c> on Unix and <c>CreateHardLinkW</c> on Windows.
/// </summary>
public static class HardLink {
    /// <summary>
    /// Attempts to hard-link <paramref name="source"/> at <paramref name="target"/>. Returns false when
    /// the platform call fails (most commonly EXDEV — different filesystems), letting the caller fall
    /// back to a copy.
    /// </summary>
    public static bool TryCreate(string source, string target) {
        try {
            return OperatingSystem.IsWindows()
                ? CreateHardLinkW(target, source, IntPtr.Zero)
                : link(source, target) == 0;
        } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
            return false;
        }
    }

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int link(string oldpath, string newpath);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
