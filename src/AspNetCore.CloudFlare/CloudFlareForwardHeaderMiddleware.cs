using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
        public bool VerifyRemoteIP { get; set; } = true;
        public string HeaderName { get; set; } = "CF_CONNECTING_IP";
        public string HttpClientFactoryName { get; set; } = nameof(CloudFlareForwardHeaderOptions);
        public string IPv4ListUrl { get; set; } = "https://www.cloudflare.com/ips-v4";
        public string IPv6ListUrl { get; set; } = "https://www.cloudflare.com/ips-v6";
        public bool UseIPv4List { get; set; } = true;
        public bool UseIPv6List { get; set; } = true;
    }

    public static class CloudFlareForwardHeaderMiddlewareExtensions
    {
        public static IApplicationBuilder UseCloudFlareForwardHeader(this IApplicationBuilder app)
            => app.UseMiddleware<CloudFlareForwardHeaderMiddleware>();
    }

    public class CloudFlareForwardHeaderMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CloudFlareForwardHeaderOptions _options;

        private IList<string>? _cfIPRangeCollection = null;

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
                if (_cfIPRangeCollection?.Any(ipRange => remoteIp.IsInSubnet(ipRange)) == true)
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
                    var data = await client.GetStringArray(_options.IPv4ListUrl, cancellationToken);
                    foreach (var entry in data) ipCollection.Add(entry);
                }

                if (_options.UseIPv6List)
                {
                    var data = await client.GetStringArray(_options.IPv6ListUrl, cancellationToken);
                    foreach (var entry in data) ipCollection.Add(entry);
                }

                _cfIPRangeCollection = ipCollection;

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


    internal static class HttpClientExtensions
    {
        public static async Task<string[]> GetStringArray(this HttpClient client, string url,
            CancellationToken cancellationToken)
        {
            var data = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            using StringReader reader = new StringReader(data);

            IList<string> lines = new List<string>();

            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }
    }

    internal static class IPAddressExtensions
    {
        // https://stackoverflow.com/questions/1499269/how-to-check-if-an-ip-address-is-within-a-particular-subnet
        public static bool IsInSubnet(this IPAddress address, string subnetMask)
        {
            var slashIdx = subnetMask.IndexOf("/", StringComparison.Ordinal);
            if (slashIdx == -1)
            {
                // We only handle netmasks in format "IP/PrefixLength".
                throw new NotSupportedException("Only SubNetMasks with a given prefix length are supported.");
            }

            // First parse the address of the netmask before the prefix length.
            var maskAddress = IPAddress.Parse(subnetMask.Substring(0, slashIdx));

            if (maskAddress.AddressFamily != address.AddressFamily)
            {
                // We got something like an IPV4-Address for an IPv6-Mask. This is not valid.
                return false;
            }

            // Now find out how long the prefix is.
            int maskLength = int.Parse(subnetMask.Substring(slashIdx + 1));

            if (maskAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                // Convert the mask address to an unsigned integer.
                var maskAddressBits = BitConverter.ToUInt32(maskAddress.GetAddressBytes().Reverse().ToArray(), 0);

                // And convert the IpAddress to an unsigned integer.
                var ipAddressBits = BitConverter.ToUInt32(address.GetAddressBytes().Reverse().ToArray(), 0);

                // Get the mask/network address as unsigned integer.
                uint mask = uint.MaxValue << (32 - maskLength);

                // https://stackoverflow.com/a/1499284/3085985
                // Bitwise AND mask and MaskAddress, this should be the same as mask and IpAddress
                // as the end of the mask is 0000 which leads to both addresses to end with 0000
                // and to start with the prefix.
                return (maskAddressBits & mask) == (ipAddressBits & mask);
            }

            if (maskAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Convert the mask address to a BitArray.
                var maskAddressBits = new BitArray(maskAddress.GetAddressBytes());

                // And convert the IpAddress to a BitArray.
                var ipAddressBits = new BitArray(address.GetAddressBytes());

                if (maskAddressBits.Length != ipAddressBits.Length)
                {
                    throw new ArgumentException("Length of IP Address and Subnet Mask do not match.");
                }

                // Compare the prefix bits.
                for (int maskIndex = 0; maskIndex < maskLength; maskIndex++)
                {
                    if (ipAddressBits[maskIndex] != maskAddressBits[maskIndex])
                    {
                        return false;
                    }
                }

                return true;
            }

            throw new NotSupportedException("Only InterNetworkV6 or InterNetwork address families are supported.");
        }
    }
}