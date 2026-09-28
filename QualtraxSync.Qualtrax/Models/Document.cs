namespace QualtraxSync.Qualtrax.Models;

using System;

public record Document(
    int Id,
    string Title,
    string Status,
    int Revision,
    bool CheckedIn,
    bool IsControlled,
    DateTimeOffset DateCreated,
    DateTimeOffset? DateExpires,
    int DocumentManagerId,
    int EditorId,
    string FileType,
    bool GeneratePdf,
    int ParentId,
    bool ShowInTree,
    int StatusId,
    int LocationId,
    string ParentType
) : Item(Id, ParentId, Title);