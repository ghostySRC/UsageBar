using System.Net;
using System.Net.Sockets;
using System.Text;
using UsageBar.Core.Models;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class AntigravityUsageProviderTests
{
    [Fact]
    public async Task MapsLocalSessionExpirationToAuthenticationRequired()
    {
        var status = await RequestFailureStatusAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(ProviderStatus.AuthenticationRequired, status);
    }

    [Fact]
    public async Task MapsQuotaServiceRateLimitToRateLimited()
    {
        var status = await RequestFailureStatusAsync(HttpStatusCode.TooManyRequests);
        Assert.Equal(ProviderStatus.RateLimited, status);
    }

    private static async Task<ProviderStatus> RequestFailureStatusAsync(HttpStatusCode responseStatus)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 }) { }
            var reason = responseStatus == HttpStatusCode.Unauthorized ? "Unauthorized" : "Too Many Requests";
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {(int)responseStatus} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        try
        {
            var languageServer = new AntigravityLanguageServer(
                "language_server.exe", 1, "standalone", [port], "UnitTestCsrfValue");
            var exception = await Assert.ThrowsAsync<UsageProviderException>(() =>
                new AntigravityUsageProvider().RefreshAsync([languageServer], CancellationToken.None));
            await server.ConfigureAwait(false);
            return exception.Status;
        }
        finally
        {
            listener.Stop();
        }
    }
}
