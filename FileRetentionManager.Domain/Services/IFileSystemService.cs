using FileRetentionManager.Domain.Models;

namespace FileRetentionManager.Domain.Services;

public interface IFileSystemService
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    void EnsureDirectory(string path);

    string CombinePath(params string[] paths);

    IAsyncEnumerable<FileMetadata> EnumerateFilesAsync(
        string rootPath,
        bool includeSubdirectories,
        CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);
}
