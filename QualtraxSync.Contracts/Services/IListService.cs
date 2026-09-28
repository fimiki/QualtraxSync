namespace QualtraxSync.Contracts.Services;


public interface IListService
{
    /// <summary>
    /// Gets the lookup list Id and primary key property name for the given list type, if it has already been cached. Otherwise returns null.
    /// </summary>
    /// <param name="siteId"></param>
    /// <param name="listType"></param>
    /// <returns></returns>
    ListKey? GetKey(string siteId, Type listType);

    /// <summary>
    /// Ensures that a lookup list of the specified type exists on the site, creating it if necessary
    /// </summary>
    /// <param name="siteId"></param>
    /// <param name="listType"></param>
    /// <returns></returns>
    Task ProvisionAsync(string siteId, Type listType);

    /// <summary>
    /// Gets the list item Id an item (row) in the lookup list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="siteId"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    Task<string> GetIdAsync<T>(string siteId, T value) where T : class;

    /// <summary>
    /// Gets the entire value of an item in the lookup list based on its list item Id.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="siteId"></param>
    /// <param name="lookupId"></param>
    /// <returns></returns>
    T GetValue<T>(string siteId, string lookupId) where T : class;

    /// <summary>
    /// Gets the entire value of an item in the lookup list based on its list item Id.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="siteId"></param>
    /// <param name="lookupId"></param>
    /// <returns></returns>
    object GetValue(string siteId, string lookupId, Type listType);

    /// <summary>
    /// Updates the lookup list with the given values, adding new items and optionally removing items that are not in the provided list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="siteId"></param>
    /// <param name="removeIfMissing"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    Task UpdateAsync<T>(string siteId, bool removeIfMissing, params T[] values) where T : class;
}

public record ListKey(string ListId, string FieldName);