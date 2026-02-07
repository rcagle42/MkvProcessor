using Microsoft.Extensions.Configuration;

namespace MkvProcessor.Services;

public static class AppConfiguration
{
    private static IConfiguration? _configuration;

    public static IConfiguration Configuration =>
        _configuration ?? throw new InvalidOperationException(
            "AppConfiguration has not been initialized. Call Initialize() in App.OnStartup.");

    public static void Initialize()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.user.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "MKVPROCESSOR_");

        _configuration = builder.Build();
    }

    public static string? TvdbApiKey => Configuration["Tvdb:ApiKey"];
    public static string? TvdbPin => Configuration["Tvdb:Pin"];
}
