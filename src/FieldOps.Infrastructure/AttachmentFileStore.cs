// File: AttachmentFileStore.cs
// Purpose: Stream bounded synthetic attachments to durable local storage with integrity metadata.
// Symbols and line locations: see docs/code-index.md.
// Important limits: MaximumAttachmentBytes bounds disk use; allowedContentTypes limits file classes.

using System.Security.Cryptography;
using FieldOps.Core;

namespace FieldOps.Infrastructure;

/// <summary>Stores attachments without buffering the entire file in memory.</summary>
public sealed class AttachmentFileStore(string rootDirectory)
{
    public const long MaximumAttachmentBytes = 5 * 1024 * 1024;
    private const int BufferSize = 81_920;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    /// <summary>
    /// Stream one attachment to a temporary file, calculate SHA-256, then atomically rename it.
    /// </summary>
    /// <exception cref="InvalidDataException">The stream exceeds the configured size limit.</exception>
    public async Task<AttachmentDescriptor> SaveAsync(
        Guid inspectionId,
        string fileName,
        string contentType,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (inspectionId == Guid.Empty)
        {
            throw new ArgumentException("Inspection ID must not be empty.", nameof(inspectionId));
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new ArgumentException("Only synthetic JPEG and PNG fixtures are accepted.", nameof(contentType));
        }

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 120)
        {
            throw new ArgumentException("File name must contain between 1 and 120 characters.", nameof(fileName));
        }

        var attachmentId = Guid.NewGuid();
        var inspectionFolder = Path.Combine(Path.GetFullPath(rootDirectory), inspectionId.ToString("N"));
        Directory.CreateDirectory(inspectionFolder);
        var extension = string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : ".jpg";
        var finalName = attachmentId.ToString("N") + extension;
        var finalPath = Path.Combine(inspectionFolder, finalName);
        var temporaryPath = finalPath + ".partial";
        long totalBytes = 0;

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[BufferSize];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    totalBytes = checked(totalBytes + bytesRead);
                    if (totalBytes > MaximumAttachmentBytes)
                    {
                        throw new InvalidDataException(
                            $"Attachment exceeds the {MaximumAttachmentBytes}-byte limit.");
                    }

                    hash.AppendData(buffer, 0, bytesRead);
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                        .ConfigureAwait(false);
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, finalPath);
            return new AttachmentDescriptor(
                attachmentId,
                inspectionId,
                safeName,
                contentType,
                totalBytes,
                Convert.ToHexString(hash.GetHashAndReset()),
                Path.Combine(inspectionId.ToString("N"), finalName));
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }
}
