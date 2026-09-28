using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Swankers.League.Mfl;

public static class MflServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MflExportClient"/> with <see cref="ExportOnlyHandler"/> always in
    /// its pipeline. Callers bind <see cref="MflOptions"/> from configuration and replace the
    /// default (placeholder-name) <see cref="FranchiseNameMap"/> with one loaded from
    /// data/franchise-names.json.
    /// </summary>
    public static IHttpClientBuilder AddMflExportClient(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(FranchiseNameMap.Empty);
        services.AddTransient<ExportOnlyHandler>();

        return services
            .AddHttpClient<MflExportClient>()
            .AddHttpMessageHandler<ExportOnlyHandler>();
    }
}
