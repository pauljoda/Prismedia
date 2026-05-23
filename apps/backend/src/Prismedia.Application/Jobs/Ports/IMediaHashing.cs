namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for computing file fingerprints (MD5 and oshash).
/// </summary>
public interface IMediaHashing {
    Task<FileHashData> ComputeHashesAsync(string filePath, CancellationToken cancellationToken);
}

public sealed record FileHashData(string Md5, string Oshash);
