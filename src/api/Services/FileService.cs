using System.Collections.Concurrent;
using System.IO.Compression;
using api.Extensions;
using api.Models;
using CSharpVitamins;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

// Implements user files management logic (uploads, gets, deletes, etc.).
public class FileService(
    IConfiguration configuration,
    FileManagerDbContext db,
    IStoreService storeService,
    IThumbnailService thumbnailService,
    ILogger<FileService> logger) : IFileService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly FileManagerDbContext _db = db;
    private readonly IStoreService _storeService = storeService;
    private readonly IThumbnailService _thumbnailService = thumbnailService;
    private readonly ILogger<FileService> _logger = logger;

    public async Task<IList<UserFile>> UploadFilesAsync(AppUser user, IFormFileCollection files, CancellationToken cancellationToken)
    {
        // A collection of uploaded files to return.
        var userFiles = new List<UserFile>();
        foreach (var file in files)
        {
            // Record new user file to the database.
            var userFile = new UserFile
            {
                Name = file.FileName,
                Type = Path.GetExtension(file.FileName),
                Created = DateTime.UtcNow,
                Owner = user.Id,
                DownloadCount = 0
            };
            var userFileEntry = await _db.UserFiles.AddAsync(userFile, cancellationToken);
            userFiles.Add(userFileEntry.Entity);

            // Save the user file entity to the database to generate its Id.
            await _db.SaveChangesAsync(cancellationToken);
            var id = userFileEntry.Entity.Id.ToString();
            var thumbnailId = userFileEntry.Entity.Thumbnail;

            // Save the file.
            var bytes = await file.ToBytesAsync(cancellationToken);
            await _storeService.AddFileAsync(id, file.FileName, bytes, cancellationToken);

            // Generate a thumbnail.
            var thumbnailResponse = await _thumbnailService.CreateThumbnailAsync(file.FileName, bytes, cancellationToken);

            // Save the thumbnail.
            using var thumbnailStream = await thumbnailResponse.Content.ReadAsStreamAsync(cancellationToken);
            await _storeService.AddFileAsync(thumbnailId, thumbnailId, await thumbnailStream.ToBytesAsync(cancellationToken), cancellationToken);
        }

        return userFiles;
    }

    public async Task<(string Filename, byte[] Bytes)> GetFileAsync(AppUser user, string id, bool preview, CancellationToken cancellationToken)
    {
        var userFile = await _db.UserFiles.FirstAsync(f => f.Owner == user.Id && f.Id == long.Parse(id), cancellationToken: cancellationToken);
        return preview ?
            (userFile.Thumbnail, await _storeService.GetFileAsync(userFile.Thumbnail, cancellationToken)) :
            (userFile.Name, await _storeService.GetFileAsync(userFile.Id.ToString(), cancellationToken));
    }

    private async Task IncrementDownloadCountAsync(long[] ids, CancellationToken cancellationToken)
    {
        var userFiles = await _db.UserFiles
            .Where(f => ids.Contains(f.Id))
            .ToListAsync(cancellationToken);

        foreach (var userFile in userFiles)
        {
            userFile.DownloadCount++;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(string Filename, byte[] Bytes)> GetArchiveAsync(AppUser? user, string[] ids, CancellationToken cancellationToken)
    {
        var longIds = ids.Select(long.Parse).ToArray();
        var userFiles = await _db.UserFiles
            .Where(f => longIds.Contains(f.Id) && (user == null || (user != null && f.Owner == user.Id)))
            .ToListAsync(cancellationToken);

        // Let's handle multiple files in parallel.
        var files = new ConcurrentDictionary<string, byte[]>();
        var tasks = new List<Task>();
        foreach (var userFile in userFiles)
        {
            tasks.Add(Task.Run(() =>
            _storeService.GetFileAsync(userFile.Id.ToString(), cancellationToken))
                .ContinueWith(bytes =>
                {
                    // Files can have same names.
                    var formattedDate = userFile.Created.ToString("yyyy-MM-dd-HH-mm-ss");
                    var derivedFilename = $"{Path.GetFileNameWithoutExtension(userFile.Name)}-{formattedDate}{userFile.Type}";
                    files.TryAdd(derivedFilename, bytes.Result);
                }, cancellationToken));
        }
        Task.WaitAll([.. tasks], cancellationToken);
        await IncrementDownloadCountAsync(longIds, cancellationToken);

        return await CompressAsync(files.ToDictionary(), cancellationToken);
    }

    public async Task<(string Filename, byte[] Bytes)> GetSharedArchiveAsync(string key, CancellationToken cancellationToken)
    {
        var sharedUserFileIds = await _db.SharedUserFiles
            .Include(s => s.UserFile)
            .Where(s => s.Key == key && s.AvailableUntil > DateTime.UtcNow)
            .Select(f => f.UserFile.Id.ToString())
            .ToArrayAsync(cancellationToken);
        return await GetArchiveAsync(null, sharedUserFileIds, cancellationToken);
    }

    public async Task<bool> TestSharedArchiveAsync(string key, CancellationToken cancellationToken)
    {
        return await _db.SharedUserFiles
            .Where(s => s.Key == key && s.AvailableUntil > DateTime.UtcNow)
            .AnyAsync(cancellationToken);
    }

    private async Task<(string Filename, byte[] Bytes)> CompressAsync(IDictionary<string, byte[]> files, CancellationToken cancellationToken)
    {
        using (var compressedFileStream = new MemoryStream())
        {
            using (var zipArchive = new ZipArchive(compressedFileStream, ZipArchiveMode.Create, false))
            {
                foreach (var file in files)
                {
                    var zipEntry = zipArchive.CreateEntry(file.Key);
                    using (var originalFileStream = new MemoryStream(file.Value))
                    using (var zipEntryStream = zipEntry.Open())
                    {
                        await originalFileStream.CopyToAsync(zipEntryStream);
                    }
                }
            }
            return ("archive.zip", compressedFileStream.ToArray());
        }
    }

    public async Task<IList<UserFile>> GetUserFilesAsync(AppUser user, CancellationToken cancellationToken)
    {
        var userFiles = await _db.UserFiles.Where(f => f.Owner == user.Id).ToListAsync(cancellationToken);
        return userFiles;
    }

    public async Task<string> ShareUserFilesAsync(AppUser user, string[] ids, DateTime availableUntil, CancellationToken cancellationToken)
    {
        var longIds = ids.Select(long.Parse);
        var userFiles = await _db.UserFiles
            .Where(f => longIds.Contains(f.Id) && f.Owner == user.Id)
            .ToListAsync(cancellationToken);
        ShortGuid id = Guid.NewGuid();
        foreach (var userFile in userFiles)
        {
            _db.SharedUserFiles.Add(new SharedUserFile
            {
                Key = id.ToString(),
                UserFile = userFile,
                AvailableUntil = availableUntil
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return id;
    }
}
