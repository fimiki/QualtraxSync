namespace QualtraxSync.Qualtrax.Models;

public record Folder(
    int Id,
    string Title,
    int InheritFrom,
    int ParentId,
    string ParentUrl,
    bool AllowControlledDocuments,
    bool AllowUncontrolledDocuments,
    bool AllowVisitorsToEdit,
    bool AllowIndividualsInDocumentList,
    bool RequirePasswordVerification,
    bool RequireStandards,
    string InheritFromUrl
) : Item(Id, ParentId, Title)
{ 
    public static Folder GetUnknownForId(int id) => new(
        Id: id,
        Title: $"Unknown Folder {id:D10}",
        InheritFrom: 0,
        ParentId: int.MinValue,
        ParentUrl: string.Empty,
        AllowControlledDocuments: false,
        AllowUncontrolledDocuments: false,
        AllowVisitorsToEdit: false,
        AllowIndividualsInDocumentList: false,
        RequirePasswordVerification: false,
        RequireStandards: false,
        InheritFromUrl: string.Empty);
}