namespace RevitLoader.Bootstrap
{
    public static class LoaderSettings
    {
        // Caminho local do arquivo com a lista de manifests (raw URLs).
        public const string ManifestListPath = "versions\\plugins.json";

        // Arquivo local com o token do GitHub (privado).
        public const string TokenFilePath = "versions\\github.token";

        // Pasta local onde o pacote baixado fica em cache.
        public const string CacheFolderName = "RevitLoader";

        // Nome do zip que o loader grava antes de extrair.
        public const string CachePackageName = "PluginPackage.zip";

        // Nome da pasta que representa a versao ativa.
        public const string CurrentPackageFolderName = "current";
    }
}