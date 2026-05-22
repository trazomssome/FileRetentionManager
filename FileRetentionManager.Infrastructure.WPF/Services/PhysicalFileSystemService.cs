using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
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

    public async IAsyncEnumerable<FileMetadata> EnumerateFilesAsync(
        string rootPath,
        bool includeSubdirectories,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Scanning target path {RootPath}", rootPath);
        var channel = Channel.CreateBounded<FileMetadata>(
            new BoundedChannelOptions(128)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        _ = Task.Run(
            () => ProduceFilesAsync(channel.Writer, rootPath, includeSubdirectories, cancellationToken),
            CancellationToken.None);

        await foreach (var file in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return file;
        }
    }

    private static async Task ProduceFilesAsync(
        ChannelWriter<FileMetadata> writer,
        string rootPath,
        bool includeSubdirectories,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(rootPath))
            {
                await writer.WriteAsync(CreateMetadata(new FileInfo(rootPath)), cancellationToken);
                writer.TryComplete();
                return;
            }

            var searchOption = includeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", searchOption))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(CreateMetadata(new FileInfo(filePath)), cancellationToken);
            }

            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
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
