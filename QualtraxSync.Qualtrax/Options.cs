namespace QualtraxSync.Qualtrax;

public class Options
{
    public const string Section = "Qualtrax";
    public static Dictionary<string, string> Map => new()
    {
        { "--Url", "Qualtrax:Url" },
        { "--Token", "Qualtrax:Token" },
        { "--UserAgent", "Qualtrax:UserAgent" }
    };

    /// <summary>
    /// The base URL of the Qualtrax API. This is used to connect to your Qualtrax system and retrieve documents and metadata.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The authentication token for accessing the Qualtrax API (https://iqm-ess.help.ideagen.com/hc/en-gb/articles/19192252856850-Getting-started-with-API-development)
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// The User-Agent header value to be sent with requests to the Qualtrax API. This is used to identify the application making the requests.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;
}