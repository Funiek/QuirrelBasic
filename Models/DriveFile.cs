namespace QuirrelBasic.Models;
public sealed record DriveFile(string Id, string Name, bool IsFolder, DateTimeOffset ModifiedTime, string? Md5, long? Size, bool IsSupported = true);
