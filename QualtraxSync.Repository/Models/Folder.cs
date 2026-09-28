using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QualtraxSync.Persistence.Models;

public record Folder
{
    [Display(Order = 1), Description("The unique identifier for the folder in Qualtrax")]
    public int Id { get; init; }

    [Display(Order = 2), Description("The aproximate date this folder was created in Qualtrax")]
    public DateTimeOffset Created { get; init; }

    [Display(Order = 3), Description("The date this folder was last modified")]
    public DateTimeOffset Modified { get; init; }

    public static Folder FromDomainEntity(Domain.Entities.Folder folder)
    {
        return new Folder
        {
            Id = folder.Id,
            Created = folder.Created,
            Modified = folder.Modified
        };
    }
}