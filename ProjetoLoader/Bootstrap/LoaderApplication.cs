using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitLoader.App;
using RevitLoader.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;


namespace RevitLoader.Bootstrap
{
    public sealed class LoaderApplication : IExternalApplication
    {
        private readonly List<String> activePlugins = new();
        private readonly Queue<(string Folder, Type Type)> pendingApplications = new();
        private UIControlledApplication? uiApplication;
        private string? logPath;
        private bool idlingRegistered;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                logPath = Path.Combine(AuthController.RevitLoaderBasePath, "loader.log");
                Directory.CreateDirectory(AuthController.RevitLoaderBasePath);
                uiApplication = application;

                /*
                // Login flow disabled as per requirements
                if (!AuthController.InitiateLoginFlow())
                {
                    LogError("Startup interrompido por autenticação não concluída.");
                    return Result.Failed;
                }
                */

                List<string> updatedFolders = GithubService.aplicarAtualizacao();

                if (updatedFolders.Count == 0)
                {
                    return Result.Succeeded;
                }

                List<string> selectedPlugins = GetSelectedPlugins();
                bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

                if (isShiftDown || selectedPlugins.Count == 0)
                {
                    var availablePluginNames = updatedFolders.Select(f => Path.GetFileName(f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))).ToList();
                    var selectionWindow = new PluginSelectionWindow(availablePluginNames, selectedPlugins);
                    if (selectionWindow.ShowDialog() == true)
                    {
                        selectedPlugins = selectionWindow.GetSelectedPlugins();
                        SaveSelectedPlugins(selectedPlugins);
                    }
                    else if (selectedPlugins.Count == 0)
                    {
                        // First time and cancelled, load nothing
                        return Result.Succeeded;
                    }
                }

                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
                var manifestListPath = Path.Combine(assemblyDir, "manifest-list.txt");
                var cacheRoot = Path.Combine(assemblyDir, "cache");
                try
                {
                    _ = manifestListPath;
                    _ = cacheRoot;
                }
                catch { }


                foreach (string caminhoPasta in updatedFolders)
                {
                    try
                    {
                        if (!Directory.Exists(caminhoPasta))
                        {
                            LogError("Pasta inexistente=" + caminhoPasta);
                            continue;
                        }

                        string nomeDaPasta = Path.GetFileName(caminhoPasta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        string caminhoCompletoDll = Path.Combine(caminhoPasta, $"{nomeDaPasta}.dll");

                        if (!File.Exists(caminhoCompletoDll))
                        {
                            LogError("DLL não encontrada=" + caminhoCompletoDll);
                            continue;
                        }

                        string pluginName = Path.GetFileNameWithoutExtension(caminhoCompletoDll);
                        if (!selectedPlugins.Contains(pluginName))
                        {
                            continue;
                        }

                        Assembly assemblyCarregado = Assembly.LoadFrom(caminhoCompletoDll);

                        // IMPORTANTE: Aqui procuramos por IExternalApplication (para criar a interface)
                        Type[] tipos;
                        try
                        {
                            tipos = assemblyCarregado.GetTypes();
                        }
                        catch (ReflectionTypeLoadException typeLoadEx)
                        {
                            tipos = typeLoadEx.Types.Where(t => t != null).ToArray();
                            LogError("Falha ao carregar tipos: " + typeLoadEx);
                            foreach (var loaderEx in typeLoadEx.LoaderExceptions.Where(e => e != null))
                            {
                                LogError("LoaderException: " + loaderEx.Message);
                            }
                        }

                        var classesApplication = tipos
                            .Where(t => typeof(IExternalApplication).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                            .ToList();

                        if (classesApplication.Count == 0)
                        {
                            LogError("Tipos no assembly=" + string.Join(", ", tipos.Select(t => t.FullName)));
                        }

                        foreach (var tipoClasse in classesApplication)
                        {
                            pendingApplications.Enqueue((caminhoPasta, tipoClasse));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("Erro ao processar pasta=" + caminhoPasta + " - " + ex);
                    }
                }

                if (pendingApplications.Count > 0 && !idlingRegistered)
                {
                    application.Idling += OnIdling;
                    idlingRegistered = true;
                }

                // Return immediately so Revit startup is not blocked. Background task will log results.
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                try 
                { 
                    var fallbackLog = Path.Combine(AuthController.RevitLoaderBasePath, "loader.log");
                    Directory.CreateDirectory(AuthController.RevitLoaderBasePath);
                    File.AppendAllText(fallbackLog, DateTime.UtcNow.ToString("o") + " Startup exception: " + exception + Environment.NewLine); 
                } catch { }
                TaskDialog.Show("Revit Loader", "Falha ao iniciar o plugin: " + exception.Message);
                return Result.Failed;
            }
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            if (pendingApplications.Count == 0)
            {
                if (uiApplication != null && idlingRegistered)
                {
                    uiApplication.Idling -= OnIdling;
                    idlingRegistered = false;
                    if (!string.IsNullOrWhiteSpace(logPath))
                    {
                        // sem log informativo
                    }
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(logPath))
            {
                logPath = Path.Combine(AuthController.RevitLoaderBasePath, "loader.log");
            }

            var (folder, type) = pendingApplications.Dequeue();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                IExternalApplication appInstanciada = (IExternalApplication)Activator.CreateInstance(type);
                var result = appInstanciada.OnStartup(uiApplication ?? (UIControlledApplication)sender);
                stopwatch.Stop();
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogError("Erro no OnStartup (Idling) de=" + type.FullName + " tempoMs=" + stopwatch.ElapsedMilliseconds + " - " + ex);
            }

            activePlugins.Add(folder);
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Desliga os aplicativos no fechamento do Revit
            DesligarAppsPorPasta(activePlugins, application);
            return Result.Succeeded;
        }

        private void DesligarAppsPorPasta(List<string> caminhosDasPastas, UIControlledApplication application)
        {
            foreach (string caminhoPasta in caminhosDasPastas)
            {
                try
                {
                    string caminhoCompletoDll = ObterCaminhoDll(caminhoPasta);
                    if (caminhoCompletoDll == null) continue;

                    // Como a DLL já está na memória, o LoadFrom apenas recupera a referência existente
                    Assembly assemblyCarregado = Assembly.LoadFrom(caminhoCompletoDll);
                    var classesApplication = ObterInstanciasApplication(assemblyCarregado);

                    foreach (var app in classesApplication)
                    {
                        // Executa o método de desligamento de cada plugin filho
                        app.OnShutdown(application);
                    }
                }
                catch (Exception ex)
                {
                    LogError("Erro no OnShutdown para pasta=" + caminhoPasta + " - " + ex);
                }
            }
        }

        // Métodos auxiliares para evitar repetição de código (Clean Code)
        private string ObterCaminhoDll(string caminhoPasta)
        {
            if (!Directory.Exists(caminhoPasta)) return null;
            string nomeDaPasta = Path.GetFileName(caminhoPasta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string caminhoDll = Path.Combine(caminhoPasta, $"{nomeDaPasta}.dll");
            return File.Exists(caminhoDll) ? caminhoDll : null;
        }

        private IEnumerable<IExternalApplication> ObterInstanciasApplication(Assembly assembly)
        {
            var tipos = assembly.GetTypes()
                .Where(t => typeof(IExternalApplication).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var tipo in tipos)
            {
                yield return (IExternalApplication)Activator.CreateInstance(tipo);
            }
        }

        private List<string> GetSelectedPlugins()
        {
            string path = Path.Combine(AuthController.RevitLoaderBasePath, "selected_plugins.json");
            if (!File.Exists(path)) return new List<string>();
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SaveSelectedPlugins(List<string> selected)
        {
            string path = Path.Combine(AuthController.RevitLoaderBasePath, "selected_plugins.json");
            try
            {
                string json = JsonSerializer.Serialize(selected);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private void LogError(string message)
        {
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return;
            }

            try
            {
                File.AppendAllText(logPath, DateTime.UtcNow.ToString("o") + " [Erro] " + message + Environment.NewLine);
            }
            catch
            {
                // best-effort logging
            }
        }
    }
}