using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RevitLoader.Bootstrap
{
    public sealed class GitHubReleaseApiService
    {
        private readonly HttpClient httpClient;

        public GitHubReleaseApiService(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public GitHubLatestRelease? DownloadLatestRelease(string releasesApiUrl)
        {
            if (string.IsNullOrWhiteSpace(releasesApiUrl))
            {
                throw new ArgumentException("Releases API URL is required.", nameof(releasesApiUrl));
            }

            var json = this.httpClient.GetStringAsync(releasesApiUrl).GetAwaiter().GetResult();
            var serializer = new DataContractJsonSerializer(typeof(GitHubLatestRelease));

            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as GitHubLatestRelease;
            }
        }

        public GitHubReleaseAsset? ResolveAsset(GitHubLatestRelease release, string assetName)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            if (string.IsNullOrWhiteSpace(assetName))
            {
                throw new ArgumentException("Asset name is required.", nameof(assetName));
            }

            return release.Assets?.FirstOrDefault(asset =>
                string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
        }
    }

    [DataContract]
    public sealed class GitHubLatestRelease
    {
        [DataMember(Name = "id")]
        public long Id { get; set; }

        [DataMember(Name = "html_url")]
        public string? HtmlUrl { get; set; }

        [DataMember(Name = "published_at")]
        public string? PublishedAt { get; set; }

        [DataMember(Name = "assets")]
        public GitHubReleaseAsset[]? Assets { get; set; }

        public DateTimeOffset ParsedPublishedAt
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(PublishedAt) && DateTimeOffset.TryParse(PublishedAt, out var parsed))
                {
                    return parsed;
                }

                return DateTimeOffset.MinValue;
            }
        }
    }

    [DataContract]
    public sealed class GitHubReleaseAsset
    {
        [DataMember(Name = "name")]
        public string? Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
