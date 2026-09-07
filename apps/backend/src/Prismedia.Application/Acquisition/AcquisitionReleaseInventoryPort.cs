namespace Prismedia.Application.Acquisition;

/// <summary>Recovers provider-advertised payload names from a persisted opaque release locator, without network access.</summary>
public interface IAcquisitionReleaseInventory {
    /// <summary>Returns known filenames, or an empty list when the provider locator carries no usable inventory.</summary>
    IReadOnlyList<string> ReadFileNames(string? downloadUrl);
}
