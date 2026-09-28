using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ExternalConnectors;
using Microsoft.Kiota.Abstractions.Serialization;

namespace QualtraxSync.SharePoint.Client.Middleware;


/// <summary>
/// https://blog.mastykarz.nl/easily-handle-long-running-operations-middleware-microsoft-graph-net-sdk/
/// </summary>
/// <param name="delayMs"></param>
public class CompleteJobWithDelayHandler(ILogger<CompleteJobWithDelayHandler> logger) : DelegatingHandler
{
    private int _delayMs = 10000;

    public void SetDelay(double delayMs)
    {
        _delayMs = delayMs > 0 ? (int)delayMs : _delayMs;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await SendAsync(request, 0, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, int tries, CancellationToken cancellationToken)
    {
        tries += 1;

        var delay = (int)Math.Pow(2, tries) * _delayMs;
        var response = await base.SendAsync(request, cancellationToken);

        var location = response.Headers.FirstOrDefault(h => h.Key == "Location").Value?.FirstOrDefault();
        if (location is not null)
        {
            if (location.IndexOf("/operations/") < 0)
            {
                // not a job URL we should follow
                return response;
            }

            logger.LogDebug("Waiting {Delay} ms before following location...", delay);

            await Task.Delay(delay);

            request.RequestUri = new Uri(location);
            request.Method = HttpMethod.Get;
            request.Content = null;

            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(25));
            return await SendAsync(request, tries, cts.Token);
        }

        if (!response.IsSuccessStatusCode)
        {
            // let the caller handle the error response
            return response;
        }

        if (request.RequestUri?.AbsolutePath.IndexOf("/operations/") < 0)
        {
            // not a job
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // deserialize the response
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        var parseNode = await ParseNodeFactoryRegistry.DefaultInstance.GetRootParseNodeAsync("application/json", ms, cancellationToken);
        var operation = parseNode.GetObjectValue(ConnectionOperation.CreateFromDiscriminatorValue);

        if (operation?.Status == ConnectionOperationStatus.Inprogress)
        {
            logger.LogDebug("Waiting {Delay} ms before retrying...", delay);

            await Task.Delay(delay);
            return await SendAsync(request, tries, cancellationToken);
        }
        else
        {
            return response;
        }
    }
}