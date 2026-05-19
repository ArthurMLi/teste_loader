using System;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RevitLoader.Bootstrap
{
    public sealed class GitHubReleaseManifestService
    {
        private readonly HttpClient httpClient;

        public GitHubReleaseManifestService(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public GitHubReleaseManifest? Download(string manifestUrl)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                throw new ArgumentException("Manifest URL is required.", nameof(manifestUrl));
            }

            // Busca o JSON remoto e transforma em objeto tipado.
            var json = this.httpClient.GetStringAsync(manifestUrl).GetAwaiter().GetResult();
            var serializer = new DataContractJsonSerializer(typeof(GitHubReleaseManifest));

            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as GitHubReleaseManifest;
            }
        }
    }
}