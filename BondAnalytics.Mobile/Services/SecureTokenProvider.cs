using Domain;

namespace BondAnalytics.Mobile.Services;

public sealed class SecureTokenProvider : ITokenProvider
{
    private const string TokenKey = "bondanalytics_token";
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private string? _cachedToken;
    private bool _isLoaded;

    public async Task<string?> GetTokenAsync()
    {
        if (Volatile.Read(ref _isLoaded))
            return Volatile.Read(ref _cachedToken);

        await _cacheLock.WaitAsync();
        try
        {
            if (!Volatile.Read(ref _isLoaded))
            {
                var token = await SecureStorage.Default.GetAsync(TokenKey);
                Volatile.Write(ref _cachedToken, token);
                Volatile.Write(ref _isLoaded, true);
            }

            return Volatile.Read(ref _cachedToken);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task SaveTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be empty.", nameof(token));

        var normalizedToken = token.Trim();
        await _cacheLock.WaitAsync();
        try
        {
            await SecureStorage.Default.SetAsync(TokenKey, normalizedToken);
            Volatile.Write(ref _cachedToken, normalizedToken);
            Volatile.Write(ref _isLoaded, true);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task ClearTokenAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            SecureStorage.Default.Remove(TokenKey);
            Volatile.Write(ref _cachedToken, null);
            Volatile.Write(ref _isLoaded, true);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public string GetCachedToken()
    {
        if (!Volatile.Read(ref _isLoaded))
            throw new InvalidOperationException("The saved T-Invest token must be loaded asynchronously before creating the API client.");

        return Volatile.Read(ref _cachedToken)
            ?? throw new InvalidOperationException("Не задан токен API T‑Invest.");
    }
}
