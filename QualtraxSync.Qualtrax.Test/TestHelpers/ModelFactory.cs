using QualtraxSync.Qualtrax.Models;

namespace QualtraxSync.Qualtrax.Test.TestHelpers;

/// <summary>
/// Factory helpers for creating Qualtrax API model instances with sensible defaults for tests.
/// </summary>
internal static class ModelFactory
{
    public static Document CreateDocument(
        int id,
        string title,
        string status,
        int revision,
        int parentId,
        string parentType,
        DateTimeOffset created) => new(
            Id: id,
            Title: title,
            Status: status,
            Revision: revision,
            CheckedIn: true,
            IsControlled: true,
            DateCreated: created,
            DateExpires: null,
            DocumentManagerId: 1,
            EditorId: 1,
            FileType: "pdf",
            GeneratePdf: false,
            ParentId: parentId,
            ShowInTree: true,
            StatusId: 1,
            LocationId: 1,
            ParentType: parentType);

    public static Folder CreateFolder(int id, string title, int parentId) => new(
        Id: id,
        Title: title,
        InheritFrom: 0,
        ParentId: parentId,
        ParentUrl: string.Empty,
        AllowControlledDocuments: true,
        AllowUncontrolledDocuments: true,
        AllowVisitorsToEdit: false,
        AllowIndividualsInDocumentList: false,
        RequirePasswordVerification: false,
        RequireStandards: false,
        InheritFromUrl: string.Empty);
}
