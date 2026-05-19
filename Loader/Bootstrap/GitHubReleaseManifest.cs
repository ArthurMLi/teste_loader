using System;
using System.Runtime.Serialization;

namespace RevitLoader.Bootstrap
{
    [DataContract]
    public sealed class GitHubReleaseManifest
    {
        // Versao publicada no GitHub.
        [DataMember(Name = "version")]
        public string? Version { get; set; }

        // URL do zip com a DLL e dependencias.
        [DataMember(Name = "packageUrl")]
        public string? PackageUrl { get; set; }

        // Nome opcional do assembly principal dentro do pacote.
        [DataMember(Name = "entryAssemblyName")]
        public string? EntryAssemblyName { get; set; }

        // Nome opcional da classe de entrada do plugin.
        [DataMember(Name = "entryClassName")]
        public string? EntryClassName { get; set; }

        // Link para notas de release.
        [DataMember(Name = "releaseNotesUrl")]
        public string? ReleaseNotesUrl { get; set; }

        public Version ParsedVersion
        {
            get
            {
                // Converte a string do manifesto em System.Version para comparacao.
                if (!string.IsNullOrEmpty(Version) && System.Version.TryParse(Version, out var version))
                {
                    return version;
                }

                return new Version(0, 0, 0, 0);
            }
        }
    }
}