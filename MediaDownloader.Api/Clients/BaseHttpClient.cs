using Polly;
using Polly.Extensions.Http;

namespace MediaDownloader.Api.Clients;

public static class HttpClientRegistration
{
    public static IServiceCollection AddApiHttpClients(this IServiceCollection services)
    {
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        // General-purpose client with retry
        services.AddHttpClient("default")
            .AddPolicyHandler(retryPolicy)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        // GitHub API client
        services.AddHttpClient("github")
            .AddPolicyHandler(retryPolicy)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaDownloader/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
            });

        // TMDB client
        services.AddHttpClient("tmdb")
            .AddPolicyHandler(retryPolicy)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });

        // Torrentio client
        services.AddHttpClient("torrentio")
            .AddPolicyHandler(retryPolicy)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://torrentio.strem.fun/");
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            });

        // Real-Debrid client
        services.AddHttpClient("realdebrid")
            .AddPolicyHandler(retryPolicy)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.real-debrid.com/rest/1.0/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        // MPC-BE client (no retry — local, fast-fail)
        services.AddHttpClient("mpc")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(3);
            });

        // Download client (long timeout, no retry)
        services.AddHttpClient("download")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromHours(4);
            });

        return services;
    }
}
