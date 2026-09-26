using System.Net.Http;
using System.Text;
using System.Text.Json;
using UsageBar.Core.Models;

namespace UsageBar.Core.Providers;

public sealed class AntigravityUsageProvider : IUsageProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    public string ProviderId => "antigravity";
    public string DisplayName => "Antigravity";

    public async Task<ProviderUsage> RefreshAsync(
        IReadOnlyList<AntigravityLanguageServer> servers,
        CancellationToken cancellationToken)
    {
        if (servers.Count == 0)
        {
            throw new UsageProviderException("Antigravity's local quota service is unavailable.", ProviderStatus.Unavailable);
        }

        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = RequestTimeout };
        UsageProviderException? lastError = null;

        foreach (var server in servers.OrderBy(server => server.ClientKind == "standalone" ? 0 : 1))
        {
            if (string.IsNullOrWhiteSpace(server.CsrfToken)) continue;
            foreach (var port in server.ListeningPorts)
            {
                var address = $"http://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
                using var request = new HttpRequestMessage(HttpMethod.Post, address);
                request.Headers.TryAddWithoutValidation("X-Codeium-Csrf-Token", server.CsrfToken);
                request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                try
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    {
                        lastError = new UsageProviderException("Antigravity's local session needs to be refreshed.", ProviderStatus.AuthenticationRequired);
                        continue;
                    }
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        lastError = new UsageProviderException("Antigravity temporarily limited usage refreshes.", ProviderStatus.RateLimited);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode) continue;

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    return AntigravityUsageParser.Parse(document.RootElement, DateTimeOffset.UtcNow);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = new UsageProviderException("Antigravity's local quota service timed out.");
                }
                catch (UsageProviderException exception)
                {
                    lastError = exception;
                }
                catch
                {
                    // Do not include local request details or server response text in diagnostics.
                    lastError = new UsageProviderException("Antigravity usage could not be refreshed.");
                }
            }
        }

        throw lastError ?? new UsageProviderException("Antigravity's local quota summary is not available.");
    }
}
