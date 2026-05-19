using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitLoader.Bootstrap
{
    public sealed class PluginActivationService
    {
        public IExternalApplication Load(string pluginFolder, GitHubReleaseManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(pluginFolder))
            {
                throw new ArgumentException("Plugin folder is required.", nameof(pluginFolder));
            }

            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            // Carrega a DLL do plugin da pasta ja validada pelo loader.
            var assemblyPath = ResolveAssemblyPath(pluginFolder, manifest.EntryAssemblyName);
            var assembly = Assembly.LoadFrom(assemblyPath);
            var entryType = ResolveEntryType(assembly, manifest.EntryClassName);

            if (!typeof(IExternalApplication).IsAssignableFrom(entryType))
            {
                throw new InvalidOperationException("The target entry class must implement Autodesk.Revit.UI.IExternalApplication.");
            }

            var instance = Activator.CreateInstance(entryType);
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to create instance of {entryType.FullName}.");
            }

            return (IExternalApplication)instance;
        }

        private static string ResolveAssemblyPath(string pluginFolder, string? entryAssemblyName)
        {
            // Primeiro tenta o nome exato informado no manifesto.
            if (!string.IsNullOrWhiteSpace(entryAssemblyName))
            {
                var expectedPath = Path.Combine(pluginFolder, entryAssemblyName);
                if (File.Exists(expectedPath))
                {
                    return expectedPath;
                }
            }

            // Se nao houver nome explicito, tenta descobrir uma unica DLL valida.
            var dllFiles = Directory.GetFiles(pluginFolder, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFileName(path), "RevitLoader.dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (dllFiles.Length == 1)
            {
                return dllFiles[0];
            }

            throw new FileNotFoundException("Unable to determine the target plugin assembly.");
        }

        private static Type ResolveEntryType(Assembly assembly, string? entryClassName)
        {
            // Se a classe foi informada no manifesto, usa ela diretamente.
            if (!string.IsNullOrWhiteSpace(entryClassName))
            {
                var explicitType = assembly.GetType(entryClassName, throwOnError: false, ignoreCase: false);
                if (explicitType != null)
                {
                    return explicitType;
                }
            }

            // Caso contrario, procura a primeira classe concreta que implemente IExternalApplication.
            var applicationType = assembly.GetTypes()
                .FirstOrDefault(type => typeof(IExternalApplication).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract);

            if (applicationType != null)
            {
                return applicationType;
            }

            throw new InvalidOperationException("No class implementing IExternalApplication was found in the target assembly.");
        }
    }
}