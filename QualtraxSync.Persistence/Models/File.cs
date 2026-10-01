using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QualtraxSync.Persistence.Models;

public record File
{
    [Display(Order = 1), Description("The title of this document in Qualtrax")]
    public string Title { get; init; } = null!;

    [Display(Order = 2), Description("The unique identifier for the document in Qualtrax")]
    public int Id { get; init; }

    [Display(Order = 3), Description("The number that applies to this version of the document")]
    public int Revision { get; init; }

    [Display(Order = 4), Description("The date this revision of the document was published")]
    public DateTimeOffset Published { get; init; }

    [Display(Order = 5), Description("The date this revision of the document was archived")]
    public DateTimeOffset? Archived { get; init; }

    [Display(Order = 6), Description("The date the document for this revision was created in Qualtrax"), ReadOnly(true)]
    public DateTimeOffset Created { get; init; }

    public static File FromDomainEntity(Domain.Entities.Revision revision)
    {
        return new File
        {
            Title = revision.Document.Name,
            Id = revision.Document.Id,
            Revision = revision.Id.Revision,
            Published = revision.Published,
            Archived = revision.Archived,
            Created = revision.Document.Created
        };
    }
}
