using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;

namespace QualtraxSync.Persistence.Models.Configurations;

public class FileConfig(IOptions<Options> options) : ISchemaConfiguration<File>
{
    public void Configure(ISiteSchemaBuilder<File> builder)
    {
        builder.AsDocument();

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.QualtraxFileContentTypeName) == false)
        {
            builder.HasName(options.Value.Metadata.QualtraxFileContentTypeName);
        }
        else
        {
            builder.HasName("Qualtrax File");
        }

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.QualtraxIdInternalName) == false)
        {
            builder.MapProperty(q => q.Id, options.Value.Metadata.QualtraxIdInternalName);
        }

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.QualtraxIdInternalName) == false)
        {
            builder.MapProperty(q => q.Id, options.Value.Metadata.QualtraxIdInternalName);
        }

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.RevisionIdInternalName) == false)
        {
            builder.MapProperty(q => q.Revision, options.Value.Metadata.RevisionIdInternalName);
        }

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.PublishedDateInternalName) == false)
        {
            builder.MapProperty(q => q.Published, options.Value.Metadata.PublishedDateInternalName);
        }

        if (string.IsNullOrWhiteSpace(options.Value.Metadata?.ArchivedDateInternalName) == false)
        {
            builder.MapProperty(q => q.Archived, options.Value.Metadata.ArchivedDateInternalName);
        }

        builder.MapProperty(q => q.Created, "Created");
        builder.MapProperty(q => q.Title, "Title");

        builder.DefaultPropertyName(property => "Qualtrax_" + property.Name);
    }
}