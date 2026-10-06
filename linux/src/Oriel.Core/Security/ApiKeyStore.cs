using System.Reflection;

namespace Oriel.Core.Security;

public interface IApiKeyStore
{
    string? Get();
    bool HasKey { get; }
}

/// <summary>
/// The Unsplash access key that ships with the app, embedded at build time from
/// UNSPLASH_ACCESS_KEY (see Oriel.Core.csproj). Users never need their own.
/// </summary>
public sealed class BuiltInApiKeyStore : IApiKeyStore
{
    static readonly string? Key = typeof(BuiltInApiKeyStore).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UnsplashAccessKey")?.Value;

    public bool HasKey => !string.IsNullOrWhiteSpace(Key);

    public string? Get() => HasKey ? Key : null;
}
