using UsageBar.Core.Models;

namespace UsageBar.Core.Providers;

public interface IUsageProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
}

public sealed class UsageProviderException(string message, ProviderStatus status = ProviderStatus.Unavailable)
    : Exception(message)
{
    public ProviderStatus Status { get; } = status;
}
