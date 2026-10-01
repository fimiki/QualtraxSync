# QualtraxSync

QualtraxSync is a .NET 10 background worker that mirrors published documents from a **Qualtrax** (Ideagen) document management system into a **SharePoint** document library.

## How it works

At a fixed interval, QualtraxSync:

1. **Queries the Qualtrax API** for revisions published or archived since the last run. The first run starts from `Sync:EarliestDocument`. When a new root folder shows up, its documents are synced from that date too.
2. **Loads what's already in SharePoint.** It reads the Qualtrax metadata columns on the files and folders in the target drive and rebuilds an in-memory model of folders, documents and revisions.
3. **Works out the changes** between Qualtrax and SharePoint:
   - new documents and revisions
   - revisions that were archived, retired or unretired
   - documents or folders that were renamed or moved
   - documents that were removed
4. **Applies the changes to SharePoint.** Depending on the change, it:
   - uploads files
   - moves or renames files and folders
   - deletes content that should no longer be mirrored

   Failed saves are retried up to 3 times with back-off. If a whole sync run fails, it is logged and retried on the next interval.

It runs as a generic .NET host, so you can run it from the console, as a Windows Service, or in a container.

## SharePoint layout

### Type folders (root folder mappings)

The `SharePoint:TypeFolders` setting controls where root folders go:

- **No mappings**: each root Qualtrax folder is mirrored directly at the root of the SharePoint drive.
- **With mappings**: each root Qualtrax folder goes under the SharePoint folder it's mapped to. Root folders that aren't in any mapping go under `Other`.

### Lifecycle folders

When `SharePoint:Lifecycle:UseFolders` is `true` (the default), each mirrored root folder gets three sub-folders. Each revision is filed by its status:

| Folder     | Contents                                                        |
|------------|-----------------------------------------------------------------|
| `Current`  | The current, published revision of each document                |
| `Archived` | Revisions that were replaced by a newer revision                |
| `Retired`  | Every revision of a document that has been retired              |

The rest of the Qualtrax folder hierarchy is recreated inside each lifecycle folder. For example:

```
Manuals/Current/Policies/Travel Policy - 005 - 2024-07-01.docx
Manuals/Archived/Policies/Travel Policy - 003 - 2024-03-01 through 2024-07-01.docx
```

When `UseFolders` is `false`, every revision stays in the document's own folder and there are no lifecycle sub-folders.

### Special cases

- **`Forms` root folder**: SharePoint reserves the name `Forms` at the root of a library, so a Qualtrax root folder named `Forms` is mirrored as `Form Templates`.
- **Empty folders**: a folder that is left empty after its documents are moved out is removed.
- **Document folders**: In Qualtrax, documents may have child documents.  For such cases in SharePoint a folder with the name of the parent document is created along with the file.

### Timestamps

- The `Created` timestamp of a Qualtrax file matches the date the file was created in Qualtrax.
- The `Modified` timestamp of a Qualtrax folder matches the latest `Published` or `Archived` date of any revision in that folder or its subfolders and is also updated when a child item is renamed or moved. This makes it easy to see which folders have changed since the last sync.

## Revision naming convention

Each revision is stored as a separate file. The name follows this pattern:

```
{Document Title}[ ({Iteration})] - {Revision:000} - {Published:yyyy-MM-dd}[ through {Archived:yyyy-MM-dd}].{extension}
```

| Part | Description |
|------|-------------|
| `Document Title` | The document's current name in Qualtrax. If the document is renamed, all of its revisions are renamed too. |
| `(Iteration)` | Added only when more than one document in the same folder has the same title. Documents are numbered by the publish date of their first revision, then by Qualtrax ID. The first one gets no suffix; the others get ` (2)`, ` (3)`, and so on. |
| `Revision` | The Qualtrax revision number, padded to three digits (for example `001`, `012`). |
| `Published` | The local date the revision was published. |
| `through Archived` | Added only when the revision is archived or retired. It's the local date the revision stopped being current. It is removed if the revision is reactivated. |
| `extension` | The original file extension of the revision. |

Examples:

```
Quality Manual - 002 - 2024-01-01.pdf
Quality Manual - 001 - 2023-06-01 through 2024-01-01.pdf
Travel Policy (2) - 003 - 2024-03-01.docx
```

## Configuration

Settings are loaded in this order. Later sources override earlier ones:

1. `appsettings.json` (and `appsettings.{Environment}.json`)
2. User secrets (in the Development environment)
3. Environment variables. Replace `:` with `__`, for example `Qualtrax__Token`.
4. Command-line arguments. Use the full key (`--Qualtrax:Token=...`) or the shortcut switch listed below (`--Token ...`).

### Qualtrax

| Key | Switch | Default | Description |
|-----|--------|---------|-------------|
| `Qualtrax:Url` | `--Url` | *(empty)* | Base URL of the Qualtrax API. |
| `Qualtrax:Token` | `--Token` | *(empty)* | API authentication token. See [Getting started with API development](https://iqm-ess.help.ideagen.com/hc/en-gb/articles/19192252856850-Getting-started-with-API-development). |
| `Qualtrax:UserAgent` | `--UserAgent` | `QualtraxSync` in `appsettings.json` | `User-Agent` header sent with every request to Qualtrax. |

### Azure authentication

QualtraxSync signs in to Microsoft Graph with an Entra ID (Azure AD) app registration that uses a client secret. The app needs permission to read and write to the target SharePoint site and drive. (Sites.ReadWrite.All and Sites.Manage.All or Sites.Selected with appropriate roles)

| Key | Switch | Default | Description |
|-----|--------|---------|-------------|
| `Azure:Auth:TenantId` | `--TenantId` | *(empty)* | Entra ID tenant ID. |
| `Azure:Auth:ClientId` | `--ClientId` | *(empty)* | App registration (client) ID. |
| `Azure:Auth:ClientSecret` | `--ClientSecret` | *(empty)* | App registration client secret. |

### SharePoint

| Key | Switch | Default | Description |
|-----|--------|---------|-------------|
| `SharePoint:Site:DriveId` | `--DriveId` | *(empty)* | Graph drive ID of the document library that holds the mirrored documents (for example `b!1234...cd34e`). |
| `SharePoint:Lifecycle:UseFolders` | `--UseFolders` | `true` | Creates `Current` / `Archived` / `Retired` folders under each root folder and files revisions into them by status. |
| `SharePoint:Lifecycle:IncludeArchived` | `--IncludeArchived` | `true` | Keeps archived (superseded) revisions in SharePoint. When `false`, a revision is deleted as soon as it's archived, and archived revisions aren't synced from Qualtrax. |
| `SharePoint:Lifecycle:IncludeRetired` | `--IncludeRetired` | `true` | Keeps retired documents in SharePoint. When `false`, all revisions of a document are deleted when it's retired, and retired documents aren't synced from Qualtrax. |
| `SharePoint:TypeFolders` | `--TypeFolders` | `{ "Other": [] }` | Maps SharePoint folder names to arrays of Qualtrax root folder names. See [Type folders](#type-folders-root-folder-mappings). |

Example `TypeFolders`:

```json
"TypeFolders": {
  "Policies": [ "HR Policies", "Finance Policies" ],
  "Procedures": [ "SOPs" ],
  "Other": []
}
```

#### Metadata

These are the names of the site-level SharePoint content types and columns that hold Qualtrax metadata used by the application to track mirrored files and folders.  Any that are not set here will be **provisioned automatically** at startup.  
For example, you may specify the name of a pre-configured content type in SharePoint to be used for mirrored Qualtrax files, and the application will add the additional columns to that content type if they don't already exist.

| Key | Switch | Description |
|-----|--------|-------------|
| `SharePoint:Metadata:QualtraxFileContentTypeName` | `--QualtraxFileContentTypeName` | Content type used for mirrored Qualtrax files. |
| `SharePoint:Metadata:QualtraxFolderContentTypeName` | `--QualtraxFolderContentTypeName` | Content type used for mirrored Qualtrax folders. |
| `SharePoint:Metadata:QualtraxIdInternalName` | `--QualtraxIdInternalName` | Internal name of the Qualtrax ID column (non-unique Number). |
| `SharePoint:Metadata:RevisionIdInternalName` | `--RevisionIdInternalName` | Internal name of the revision ID column (non-unique Number). |
| `SharePoint:Metadata:PublishedDateInternalName` | `--PublishedDateInternalName` | Internal name of the published date column (non-unique Date and Time). |
| `SharePoint:Metadata:ArchivedDateInternalName` | `--ArchivedDateInternalName` | Internal name of the archived date column (non-unique Date and Time). |

### Sync

| Key | Switch | Default | Description |
|-----|--------|---------|-------------|
| `Sync:EarliestDocument` | `--EarliestDocument` | `1990-01-01T00:00:00+00:00` | Publish date of the earliest Qualtrax document. Syncing starts here on the first run and whenever a new root folder is found. |
| `Sync:IntervalSeconds` | `--IntervalSeconds` | `300` | Seconds between sync runs. Must be at least 1. |

### Application Insights

| Key | Default | Description |
|-----|---------|-------------|
| `ApplicationInsights:ConnectionString` | *(empty)* | Optional. When set, all log events are also sent to Application Insights as traces. When empty, the sink is not added. |

### Logging

Logging uses [Serilog](https://serilog.net/) and is configured through the `Serilog` section (see [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration)). The default `appsettings.json` sets up:

- **Minimum levels**: `Error` by default, `Information` for the `QualtraxSync` namespace.
- **Sinks**: `File`, `EventLog`, and `Console` by default.

## Running

Below are the minimum required arguments to run the application.

Console:
```powershell
dotnet run -- --Url https://qualtrax.example.com/api --Token <token> --UserAgent <user-agent> --TenantId <tenant> --ClientId <client> --ClientSecret <secret> --DriveId <drive-id>
```

Executable:
```powershell
QualtraxSync.exe --Url https://qualtrax.example.com/api --Token <token> --UserAgent <user-agent> --TenantId <tenant> --ClientId <client> --ClientSecret <secret> --DriveId <drive-id>
```

## Solution structure

| Project | Purpose |
|---------|---------|
| `QualtraxSync` | Host, `Program`, background `Worker` and logging setup. |
| `QualtraxSync.Qualtrax` | Qualtrax API client. |
| `QualtraxSync.SharePoint` | Microsoft Graph / SharePoint file and metadata services. |
| `QualtraxSync.Domain` | Domain entities (`Folder`, `Document`, `Revision`), domain events and the notification dispatcher. |
| `QualtraxSync.Persistence` | Loads SharePoint content into the domain model, builds paths and names, and runs the handlers that write changes back to SharePoint. |
| `QualtraxSync.Services` | Sync logic that compares Qualtrax with the domain model. |
| `*.Test` | Unit and integration tests (xUnit v3). |
