using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;

namespace QualtraxSync.Persistence.Models.Configurations;

public class FolderConfig(IOptions<Options> options) : ISchemaConfiguration<Folder>
{
    public void Configure(ISiteSchemaBuilder<Folder> builder)
    {
        builder.AsFolder();

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.QualtraxFolderContentTypeName) == false)
        {
            builder.HasName(options.Value.Metadata.QualtraxFolderContentTypeName);
        }
        else
        {
            builder.HasName("Qualtrax Folder");
        }

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.QualtraxIdInternalName) == false)
        {
            builder.MapProperty(q => q.Id, options.Value.Metadata.QualtraxIdInternalName);
        }

        builder.MapProperty(q => q.Created, "Created");
        builder.MapProperty(q => q.Modified, "Modified");

        builder.DefaultPropertyName(property => "Qualtrax_" + property.Name);
    }
}