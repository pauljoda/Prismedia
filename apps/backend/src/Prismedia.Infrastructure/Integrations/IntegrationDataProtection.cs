using Microsoft.AspNetCore.DataProtection;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Persistent key ring shared by connection credentials and purpose-isolated integration tokens.</summary>
internal static class IntegrationDataProtection {
    #region Static Variables

    private const string ApplicationName = "Prismedia.Connections";

    #endregion

    #region Actions - Creation

    internal static IDataProtectionProvider Create(string dataDirectory) {
        var directory = Directory.CreateDirectory(Path.Combine(dataDirectory, "keys", "connections"));
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return DataProtectionProvider.Create(directory, builder => builder.SetApplicationName(ApplicationName));
    }

    #endregion
}
