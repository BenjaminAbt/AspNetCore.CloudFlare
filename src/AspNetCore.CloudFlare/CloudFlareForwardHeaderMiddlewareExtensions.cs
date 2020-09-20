using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BenjaminAbt.AspNetCore.CloudFlare
{
    public static class CloudFlareForwardHeaderMiddlewareExtensions
    {
        public static IServiceCollection AddCloudFlareForwardHeaderOptions(
            this IServiceCollection services,
            Action<CloudFlareForwardHeaderOptions>? options = null)
        {
            return services.Configure<CloudFlareForwardHeaderOptions>(cfOptions => options?.Invoke(cfOptions));
        }

        public static IApplicationBuilder UseCloudFlareForwardHeader(
            this IApplicationBuilder app,
            Action<CloudFlareForwardHeaderOptions>? options = null)
        {
            return app.UseMiddleware<CloudFlareForwardHeaderMiddleware>();
        }
    }
}