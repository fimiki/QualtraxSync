namespace QualtraxSync.Contracts.Models;

public record QualtraxItem(
    int Id, 
    string Name,
    QualtraxItem? Folder);

public record QualtraxDocument(
    int Id,
    int LatestRevision,
    string Name, 
    QualtraxItem Folder,
    QualtraxStatus Status,
    DateTimeOffset Created) : QualtraxItem(Id, Name, Folder);

public record QualtraxRevision(
    int Id,
    QualtraxDocument Document,
    string Extension,
    Stream Contents,
    DateTimeOffset Published,
    DateTimeOffset? Archived);