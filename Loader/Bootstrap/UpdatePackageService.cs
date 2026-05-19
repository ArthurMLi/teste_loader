using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace RevitLoader.Bootstrap
{
    public sealed class UpdatePackageService
    {
        private readonly HttpClient httpClient;

        public UpdatePackageService(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public string DownloadAndExtract(string packageUrl, string cacheRoot)
        {
            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                throw new ArgumentException("Package URL is required.", nameof(packageUrl));
            }

            if (string.IsNullOrWhiteSpace(cacheRoot))
            {
                throw new ArgumentException("Cache root is required.", nameof(cacheRoot));
            }

            Directory.CreateDirectory(cacheRoot);

            // Salva o pacote baixado para permitir reutilizacao e diagnostico.
            var packagePath = Path.Combine(cacheRoot, LoaderSettings.CachePackageName);
            var payload = this.httpClient.GetByteArrayAsync(packageUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(packagePath, payload);

            // Extrai a versao ativa em uma pasta fixa.
            var extractPath = Path.Combine(cacheRoot, LoaderSettings.CurrentPackageFolderName);
            if (Directory.Exists(extractPath))
            {
                Directory.Delete(extractPath, true);
            }

            ZipFile.ExtractToDirectory(packagePath, extractPath);
            return extractPath;
        }
    }
}