namespace RevitLoader.Bootstrap
{
    public static class LoaderSettings
    {
        // Endereco bruto do manifesto no GitHub.
        public const string ManifestUrl = "https://raw.githubusercontent.com/sua-conta/seu-repo/main/revit/loader-manifest.json";

        // Pasta local onde o pacote baixado fica em cache.
        public const string CacheFolderName = "RevitLoader";

        // Nome do zip que o loader grava antes de extrair.
        public const string CachePackageName = "PluginPackage.zip";

        // Nome da pasta que representa a versao ativa.
        public const string CurrentPackageFolderName = "current";
    }
}