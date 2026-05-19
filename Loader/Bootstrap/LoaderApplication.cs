using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
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

                // Se houver versao nova, baixa e substitui o pacote em cache.
                var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LoaderSettings.CacheFolderName);
                var pluginFolder = Path.Combine(cacheRoot, LoaderSettings.CurrentPackageFolderName);

                // Baixa quando ha update ou quando nao existe nada em cache ainda.
                if (localVersion == null || VersionComparer.IsUpdateAvailable(localVersion, manifest.ParsedVersion) || !Directory.Exists(pluginFolder))
                {
                    if (manifest.PackageUrl == null)
                    {
                        TaskDialog.Show("Revit Loader", "Pacote URL não encontrado no manifesto.");
                        return Result.Failed;
                    }
                    pluginFolder = updateService.DownloadAndExtract(manifest.PackageUrl, cacheRoot);
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
    }
}