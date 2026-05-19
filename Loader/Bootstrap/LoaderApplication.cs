using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using Autodesk.Revit.UI;

namespace RevitLoader.Bootstrap
{
    public sealed class LoaderApplication : IExternalApplication
    {
        private IExternalApplication? activePlugin;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // A versao local vem do proprio assembly do loader.
                var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
                var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RevitLoader", localVersion?.ToString() ?? "1.0"));
                var manifestService = new GitHubReleaseManifestService(httpClient);
                var updateService = new UpdatePackageService(httpClient);
                var activationService = new PluginActivationService();
                // O manifesto remoto diz qual pacote deve ser usado.
                var manifest = manifestService.Download(LoaderSettings.ManifestUrl);

                if (manifest == null)
                {
                    TaskDialog.Show("Revit Loader", "Falha ao baixar o manifesto do plugin.");
                    return Result.Failed;
                }

                // Se houver versao nova do plugin, baixa e substitui o pacote em cache.
                var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LoaderSettings.CacheFolderName);
                var pluginFolder = Path.Combine(cacheRoot, LoaderSettings.CurrentPackageFolderName);
                var cachedManifestPath = Path.Combine(cacheRoot, LoaderSettings.CachedManifestFileName);
                var cachedManifest = TryLoadCachedManifest(cachedManifestPath);
                var needsUpdate = cachedManifest == null ||
                    VersionComparer.IsUpdateAvailable(cachedManifest.ParsedVersion, manifest.ParsedVersion) ||
                    !Directory.Exists(pluginFolder);

                // Baixa quando ha update ou quando nao existe nada em cache ainda.
                if (needsUpdate)
                {
                    if (manifest.PackageUrl == null)
                    {
                        TaskDialog.Show("Revit Loader", "Pacote URL não encontrado no manifesto.");
                        return Result.Failed;
                    }
                    pluginFolder = updateService.DownloadAndExtract(manifest.PackageUrl, cacheRoot);
                    SaveManifestToCache(cachedManifestPath, manifest);
                }

                // Depois da validacao, carrega a DLL correta do pacote.
                this.activePlugin = activationService.Load(pluginFolder, manifest);
                return this.activePlugin.OnStartup(application);
            }
            catch (Exception exception)
            {
                TaskDialog.Show("Revit Loader", "Falha ao iniciar o plugin: " + exception.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Encaminha o shutdown para o plugin real, quando ele foi carregado.
            if (this.activePlugin != null)
            {
                return this.activePlugin.OnShutdown(application);
            }

            return Result.Succeeded;
        }

        private static GitHubReleaseManifest? TryLoadCachedManifest(string cachedManifestPath)
        {
            if (string.IsNullOrWhiteSpace(cachedManifestPath) || !File.Exists(cachedManifestPath))
            {
                return null;
            }

            var json = File.ReadAllText(cachedManifestPath, Encoding.UTF8);
            var serializer = new DataContractJsonSerializer(typeof(GitHubReleaseManifest));

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as GitHubReleaseManifest;
            }
        }

        private static void SaveManifestToCache(string cachedManifestPath, GitHubReleaseManifest manifest)
        {
            var serializer = new DataContractJsonSerializer(typeof(GitHubReleaseManifest));

            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, manifest);
                File.WriteAllText(cachedManifestPath, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }
}