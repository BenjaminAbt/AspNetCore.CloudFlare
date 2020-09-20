using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BenjaminAbt.AspNetCore.CloudFlare
{
    internal static class InternalHttpClientExtensions
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
}