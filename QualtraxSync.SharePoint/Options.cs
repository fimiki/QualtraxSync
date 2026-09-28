namespace QualtraxSync.SharePoint;

public class Options
{
    public const string Section = "Azure";
    public static Dictionary<string, string> Map => new()
    {
        { "--TenantId", "Azure:Auth:TenantId" },
        { "--ClientId", "Azure:Auth:ClientId" },
        { "--ClientSecret", "Azure:Auth:ClientSecret" }
    };

    public AuthOptions Auth { get; set; } = new AuthOptions();  
}

public class AuthOptions
{
    /// <summary>
    /// The Azure Active Directory tenant ID for the application. This is used to authenticate the application with Azure services.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The Azure Active Directory client ID for the application. This is used to authenticate the application with Azure services.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The Azure Active Directory client secret for the application. This is used to authenticate the application with Azure services.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
}