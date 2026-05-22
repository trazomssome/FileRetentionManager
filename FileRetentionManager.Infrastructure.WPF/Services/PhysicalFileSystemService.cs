using System.IO;
using System.Text;
using FileRetentionManager.Domain.Models;
using FileRetentionManager.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileRetentionManager.Infrastructure.WPF.Services;

public sealed class PhysicalFileSystemService : IFileSystemService
{
    private readonly ILogger<PhysicalFileSystemService> logger;

    public PhysicalFileSystemService(ILogger<PhysicalFileSystemService>? logger = null)
    {
        this.logger = logger ?? NullLogger<PhysicalFileSystemService>.Instance;
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public string CombinePath(params string[] paths)
    {
        return Path.Combine(paths);
    }

    public Task<IReadOnlyList<FileMetadata>> EnumerateFilesAsync(
        string rootPath,
        bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Scanning target path {RootPath}", rootPath);

        return Task.Run<IReadOnlyList<FileMetadata>>(
            () =>
            {
                if (File.Exists(rootPath))
                {
                    return [CreateMetadata(new FileInfo(rootPath))];
                }

                var searchOption = includeSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                var files = new List<FileMetadata>();

                foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", searchOption))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    files.Add(CreateMetadata(new FileInfo(filePath)));
                }

                return files;
            },
            cancellationToken);
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting file {Path}", path);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(path);
            },
            cancellationToken);
    }

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        logger.LogInformation("Writing file {Path}", path);

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            EnsureDirectory(directory);
        }

        return File.WriteAllTextAsync(path, contents, Encoding.UTF8, cancellationToken);
    }

    private static FileMetadata CreateMetadata(FileInfo fileInfo)
    {
        return new FileMetadata(
            fileInfo.FullName,
            fileInfo.Name,
            fileInfo.Length,
            new DateTimeOffset(fileInfo.CreationTimeUtc),
            new DateTimeOffset(fileInfo.LastWriteTimeUtc));
    }
}
