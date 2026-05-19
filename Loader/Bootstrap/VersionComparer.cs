using System;

namespace RevitLoader.Bootstrap
{
    public static class VersionComparer
    {
        public static bool IsUpdateAvailable(Version localVersion, Version remoteVersion)
        {
            // Sem versao remota nao existe update para aplicar.
            if (remoteVersion == null)
            {
                return false;
            }

            // Se o loader local nao tiver versao, qualquer versao remota e mais nova.
            if (localVersion == null)
            {
                return true;
            }

            // Atualiza apenas quando a remota for maior que a local.
            return remoteVersion > localVersion;
        }
    }
}