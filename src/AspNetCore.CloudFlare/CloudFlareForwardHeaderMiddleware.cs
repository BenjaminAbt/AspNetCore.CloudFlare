using System;
using System.Collections.Generic;
using System.Linq;
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
    public class CloudFlareForwardHeaderMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CloudFlareForwardHeaderOptions _options;

        private IList<string>? _cfIpRangeCollection;

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

            _forwardedHeadersMiddleware = new ForwardedHeadersMiddleware(next, loggerFactory, Options.Create(
                new ForwardedHeadersOptions
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

            if (context.Connection.RemoteIpAddress is not null &&
                context.Request.Headers.ContainsKey(_options.HeaderName))
            {
                var remoteIp = context.Connection.RemoteIpAddress;
                if (_cfIpRangeCollection?.Any(ipRange => remoteIp.IsInSubnet(ipRange)) == true)
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

                List<string> ipCollection = new List<string>();
                if (_options.UseIPv4List)
                {
                    var data = await client.GetStringArray(_options.IPv4ListUrl, cancellationToken).ConfigureAwait(false);
                    ipCollection.AddRange(data);
                }

                if (_options.UseIPv6List)
                {
                    var data = await client.GetStringArray(_options.IPv6ListUrl, cancellationToken).ConfigureAwait(false);
                    ipCollection.AddRange(data);
                }

                _cfIpRangeCollection = ipCollection;

                logger.LogInformation($"Initialization of {nameof(CloudFlareForwardHeaderMiddleware)} completed.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    $"Initialization of {nameof(CloudFlareForwardHeaderMiddleware)} failed with {ex}");
                throw;
            }
        }
    }
}