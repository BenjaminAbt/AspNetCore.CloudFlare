using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BenjaminAbt.AspNetCore.CloudFlare
{
    public class CloudFlareForwardHeaderOptions
    {
        public string HeaderName { get; set; } = "CF_CONNECTING_IP";
        public string HttpClientFactoryName { get; set; } = nameof(CloudFlareForwardHeaderOptions);
        public string IPv4ListUrl { get; set; } = "https://www.cloudflare.com/ips-v4";
        public string IPv6ListUrl { get; set; } = "https://www.cloudflare.com/ips-v6";
    }

    public class CloudFlareForwardHeaderMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CloudFlareForwardHeaderOptions _options;

        private IList<IPAddress>? _cfIPAddressCollection = null;

        private Task? _initializationTask;
        private readonly ForwardedHeadersMiddleware _forwardedHeadersMiddleware;

        public CloudFlareForwardHeaderMiddleware(
            RequestDelegate next,
            ILoggerFactory loggerFactory,
            IOptions<CloudFlareForwardHeaderOptions> optionsAccessor,
            IHttpClientFactory httpClientFactory,
            IHostApplicationLifetime lifetime)
        {
            _options = optionsAccessor.Value;
            _next = next;
            _loggerFactory = loggerFactory;
            _httpClientFactory = httpClientFactory;

            _forwardedHeadersMiddleware = new ForwardedHeadersMiddleware(next, loggerFactory, Options.Create(new ForwardedHeadersOptions
            {
                ForwardedForHeaderName = _options.HeaderName,
                ForwardedHeaders = ForwardedHeaders.XForwardedFor
            }));

            // Start initialization when the app starts
            var registrationCancellationToken = default(CancellationTokenRegistration);
            registrationCancellationToken = lifetime.ApplicationStarted.Register(() =>
            {
                _initializationTask = InitializeAsync(lifetime.ApplicationStopping);
                registrationCancellationToken.Dispose();
            });
        }

        public async Task Invoke(HttpContext context)
        {
            var initializationTask = _initializationTask; // use copy to avoid race condition
            if (initializationTask is not null)
            {
                await initializationTask.ConfigureAwait(false);
                _initializationTask = null;
            }

            if (context.Connection.RemoteIpAddress is not null && context.Request.Headers.ContainsKey(_options.HeaderName))
            {
                if (_cfIPAddressCollection?.Any(ip => ip.Equals(context.Connection.RemoteIpAddress)) == true)
                {
                    await _forwardedHeadersMiddleware.Invoke(context).ConfigureAwait(false);
                }
            }

            await _next(context).ConfigureAwait(false);
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var logger = _loggerFactory.CreateLogger<CloudFlareForwardHeaderMiddleware>();

            try
            {
                logger.LogInformation($"Initialization {nameof(CloudFlareForwardHeaderMiddleware)}...");

                var client = _httpClientFactory.CreateClient(_options.HttpClientFactoryName);

                List<IPAddress> ipCollection = new List<IPAddress>();

                var ip4Data = await client.GetStringAsync(_options.IPv4ListUrl, cancellationToken).ConfigureAwait(false);
                var ip6Data = await client.GetStringAsync(_options.IPv6ListUrl, cancellationToken).ConfigureAwait(false);

                ipCollection.AddRange(ParseCloudFlareIPAddressData(ip4Data));
                ipCollection.AddRange(ParseCloudFlareIPAddressData(ip6Data));

                _cfIPAddressCollection = ipCollection;

                logger.LogInformation($"Initialization of {nameof(CloudFlareForwardHeaderMiddleware)} completed.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Initialization of {nameof(CloudFlareForwardHeaderMiddleware)} failed with {ex}");
                throw;
            }

            static IEnumerable<IPAddress> ParseCloudFlareIPAddressData(string data)
            {
                foreach (var entry in data.Split(Environment.NewLine))
                {
                    if (IPAddress.TryParse(entry, out var ip2Add))
                    {
                        yield return ip2Add;
                    }
                }
            }
        }
    }
}