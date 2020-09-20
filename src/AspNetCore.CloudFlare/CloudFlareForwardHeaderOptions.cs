namespace BenjaminAbt.AspNetCore.CloudFlare
{
    public class CloudFlareForwardHeaderOptions
    {
        public string HeaderName { get; set; } = "CF_CONNECTING_IP";
        public string HttpClientFactoryName { get; set; } = nameof(CloudFlareForwardHeaderOptions);
        public string IPv4ListUrl { get; set; } = "https://www.cloudflare.com/ips-v4";
        public string IPv6ListUrl { get; set; } = "https://www.cloudflare.com/ips-v6";
        public bool UseIPv4List { get; set; } = true;
        public bool UseIPv6List { get; set; } = true;
    }
}