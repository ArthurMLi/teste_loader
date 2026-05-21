namespace RevitLoader.Bootstrap
{
    public static class LoaderSettings
    {
        // Endereco bruto do manifesto no GitHub.
        public const string ManifestUrl = "https://raw.githubusercontent.com/ArthurMLi/teste_loader/refs/heads/main/Loader/revit/loader-manifest.json";

        // Endpoint da API do GitHub para pegar a release mais recente.
        public const string ReleasesApiUrl = "https://api.github.com/repos/ArthurMLi/teste_loader/releases/latest";

        // Nome esperado do asset publicado na release.
        public const string ReleaseAssetName = "PluginPackage.zip";

        // Pasta local onde o pacote baixado fica em cache.
        public const string CacheFolderName = "RevitLoader";

        // Nome do zip que o loader grava antes de extrair.
        public const string CachePackageName = "PluginPackage.zip";

        // Nome da pasta que representa a versao ativa.
        public const string CurrentPackageFolderName = "current";

        // Nome do manifesto salvo localmente para comparar versoes do plugin.
        public const string CachedManifestFileName = "loader-manifest.json";
    }
}