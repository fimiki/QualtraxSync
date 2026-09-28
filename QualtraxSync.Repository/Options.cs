namespace QualtraxSync.Persistence;

public class Options
{
    public const string Section = "SharePoint";
    public static Dictionary<string, string> Map => new()
    {
        { "--DriveId", "SharePoint:Site:DriveId" },
        { "--QualtraxFileContentTypeName", "SharePoint:Metadata:QualtraxFileContentTypeName" },
        { "--QualtraxFolderContentTypeName", "SharePoint:Metadata:QualtraxFolderContentTypeName" },
        { "--PublishedDateInternalName", "SharePoint:Metadata:PublishedDateInternalName" },
        { "--ArchivedDateInternalName", "SharePoint:Metadata:ArchivedDateInternalName" },
        { "--QualtraxIdInternalName", "SharePoint:Metadata:QualtraxIdInternalName" },
        { "--RevisionIdInternalName", "SharePoint:Metadata:RevisionIdInternalName" },
        { "--UseFolders", "SharePoint:Lifecycle:UseFolders" },
        { "--IncludeRetired", "SharePoint:Lifecycle:IncludeRetired" },
        { "--IncludeArchived", "SharePoint:Lifecycle:IncludeArchived" }
    };

    /// <summary>
    /// The SharePoint identifiers for the site and drive that will host the Qualtrax documents.
    /// </summary>
    public SiteOptions Site { get; set; } = new SiteOptions();

    /// <summary>
    /// The metadata identifiers for the Qualtrax documents that will be stored in SharePoint.  These will be provisioned if not provided in settings.
    /// </summary>
    public MetadataOptions? Metadata { get; set; }

    /// <summary>
    /// Controls how archived and retired versions of Qualtrax documents are mirrored in SharePoint
    /// </summary>
    public LifecycleOptions Lifecycle { get; set; } = new LifecycleOptions();

    /// <summary>
    /// A list of root folders to be created in SharePoint and the Qualtrax root folders that should be mapped to each.
    /// This may be used to assign document types in the CLR Connect portal. If no mappings other than 'Other' is provided, 
    /// all root Qualtrax documents will be mirrored directly to the root of the SharePoint drive. If mappings are provided, 
    /// all root Qualtrax documents will be mirrored to the specified SharePoint folder based on their Qualtrax root folder, 
    /// and any Qualtrax root folders not specified in the mappings will be mirrored to the 'Other' folder.
    /// </summary>
    public Dictionary<string, string[]> FolderMappings { get; set; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { {"Other", [] } };

    /// <summary>
    /// True if one or more <see cref="FolderMappings"/> other than 'Other' have been configured, meaning root Qualtrax
    /// folders should be mirrored under a SharePoint-only parent folder rather than directly at the drive root.
    /// </summary>
    public bool UsesFolderMappings => FolderMappings.Keys.Any(key => key.Equals("Other", StringComparison.OrdinalIgnoreCase) == false);

    /// <summary>
    /// Resolves the SharePoint-only parent folder that a root Qualtrax folder should be mirrored under, based on <see cref="FolderMappings"/>.
    /// Returns null if no mappings other than 'Other' have been configured, in which case the root Qualtrax folder should be mirrored
    /// directly to the root of the SharePoint drive. If mappings have been configured but the given root folder is not found in any of
    /// them, it is mirrored under the 'Other' folder.
    /// </summary>
    public string? ResolveRootFolder(string qualtraxRootFolderName)
    {
        if (UsesFolderMappings == false) return null;

        foreach (var mapping in FolderMappings)
        {
            if (mapping.Key.Equals("Other", StringComparison.OrdinalIgnoreCase)) continue;

            if (mapping.Value.Contains(qualtraxRootFolderName, StringComparer.OrdinalIgnoreCase))
            {
                return mapping.Key;
            }
        }

        return "Other";
    }
}

public class SiteOptions
{
    /// <summary>                                                                                      
    /// The drive ID for the SharePoint site library that will host the Qualtrax documents. (Example: "b!1234567890abcdef89jkl012mno345pqr678stu901vwx234yz567890ab12cd34e")
    /// </summary>
    public string DriveId { get; set; } = string.Empty;
}

public class MetadataOptions
{
    /// <summary>
    /// The content type name for Qualtrax files in SharePoint.
    /// </summary>
    public string QualtraxFileContentTypeName { get; set; } = string.Empty;

    /// <summary>
    /// The content type name for Qualtrax folders in SharePoint.
    /// </summary>
    public string QualtraxFolderContentTypeName { get; set; } = string.Empty;

    /// <summary>
    /// The internal name for the Qualtrax ID field in SharePoint.
    /// </summary>
    public string QualtraxIdInternalName { get; set; } = string.Empty;

    /// <summary>
    /// The internal name for the revision ID field in SharePoint.
    /// </summary>
    public string RevisionIdInternalName { get; set; } = string.Empty;

    /// <summary>
    /// The internal name for the published date field in SharePoint.
    /// </summary>
    public string PublishedDateInternalName { get; set; } = string.Empty;

    /// <summary>
    /// The internal name for the archived date field in SharePoint.
    /// </summary>
    public string ArchivedDateInternalName { get; set; } = string.Empty;
}

public class LifecycleOptions
{
    /// <summary>
    /// If true, three folders (Current, Archived, Retired) will be created under each root Qualtrax folder mirrored in SharePoint and all revisions sorted into these folders based on their status
    /// </summary>
    public bool UseFolders { get; set; } = true;

    /// <summary>
    /// If true, archived documents will be mirrored in SharePoint rather than having all their revisions deleted upon archiving
    /// </summary>
    public bool IncludeArchived { get; set; } = true;

    /// <summary>
    /// If true, retired documents will be mirrored in SharePoint rather than having all their revisions deleted upon retirement
    /// </summary>
    public bool IncludeRetired { get; set; } = true;
}