using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using DotNetEnv;
using RevitLoader.App;
using Microsoft.Extensions.Configuration;

namespace RevitLoader.Services
{
    class GithubService
    {
            private static readonly string LogPath = Path.Combine(AuthController.RevitLoaderBasePath, "github.log");

            //public static async Task Main()
            //{
            //    Console.WriteLine("[Main] Iniciando aplicação de atualização...");
            //    var urls = pegarEnvEJson();
            //    Console.WriteLine($"[Main] URLs carregadas: {urls?.Count ?? 0}");
            //
            //    string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN"); // Seu token gerado no GitHub
            //
            //    try
            //    {
            //        foreach (string url in urls ?? new List<string>())
            //        {
            //            Console.WriteLine($"[Main] Processando URL: {url}");
            //            var pluginName = GetPluginNameFromUrl(url);
            //            Console.WriteLine($"[Main] Plugin identificado: {pluginName}");
            //            string pathDll = Path.Combine(".\\", pluginName) + "\\";
            //
            //            await aplicarAtualizacao(token, url, pathDll);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine($"[Main][Erro] Exceção no loop principal: {ex}");
            //    }
            //
            //    Console.WriteLine("[Main] Finalizando método Main.");
            //}
            public GithubService() { 
        
             }
            private static List<string> pegarEnvEJson()
            {
                try
                {
                string caminhoDaSuaDll = Assembly.GetExecutingAssembly().Location;
                string diretorioDoAddin = Path.GetDirectoryName(caminhoDaSuaDll) ?? string.Empty;
                string caminhoPastaConfig = diretorioDoAddin;

                string caminhoJson = Path.Combine(caminhoPastaConfig, "config");
                string caminhoEnv = Path.Combine(caminhoPastaConfig, "config", ".env");

               

                Env.Load(caminhoEnv);
                
                string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

                var config = new ConfigurationBuilder()
                    .SetBasePath(caminhoJson)
                    .AddJsonFile("config.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                var urlsConfiguradas = config.GetSection("MinhasUrls").Get<List<string>>() ?? new List<string>();
                var listaDeUrls = CarregarUrlsDoLoaderPublico(urlsConfiguradas, token);
                return listaDeUrls;
            }
            catch (Exception ex)
            {
                LogError(nameof(pegarEnvEJson), $"Falha ao carregar config/.env: {ex.Message}");
                return new List<string>();
            }
        }
            private static string? GetPluginNameFromUrl(string url)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    return null;
                }

                try
                {
                    var u = new Uri(url);
                    var segs = u.Segments
                                .Select(s => s.Trim('/'))
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToArray();

                    if (segs.Length >= 3 && segs[0].Equals("repos", StringComparison.OrdinalIgnoreCase))
                    {
                        return segs[2];
                    }

                    if (segs.Length == 0)
                    {
                        var fallbackHost = u.Host.Replace('.', '_');
                        return fallbackHost;
                    }

                    var last = segs.Last();
                    var name = Path.GetFileNameWithoutExtension(last);
                    return string.IsNullOrEmpty(name) ? null : name;
                }
                catch (Exception ex)
                {
                    LogError(nameof(GetPluginNameFromUrl), $"Falha ao identificar plugin: {ex.Message}");
                    return null;
                }
            }

            private static bool is_atualizado(string dataLocal, string dataGithub)
            {
                DateTimeOffset dataL = DateTimeOffset.Parse(dataLocal);
                DateTimeOffset dataG = DateTimeOffset.Parse(dataGithub);

                var atualizado = dataL >= dataG;
                return atualizado;
            }

            private static List<string> CarregarUrlsDoLoaderPublico(List<string> urlsConfiguradas, string? token)
            {
                if (urlsConfiguradas == null || urlsConfiguradas.Count == 0)
                {
                    return new List<string>();
                }

                if (urlsConfiguradas.Count > 1)
                {
                    return urlsConfiguradas;
                }

                var urlReleaseLoader = urlsConfiguradas[0];
                if (string.IsNullOrWhiteSpace(urlReleaseLoader))
                {
                    return new List<string>();
                }

                try
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RevitLoader", "1.0"));
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }

                    var releaseResponse = client.GetAsync(urlReleaseLoader).GetAwaiter().GetResult();
                    releaseResponse.EnsureSuccessStatusCode();

                    var releaseJson = releaseResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var releaseNode = JsonNode.Parse(releaseJson) as JsonObject;
                    if (releaseNode == null)
                    {
                        return urlsConfiguradas;
                    }

                    var assetJson = EncontrarAssetJsonDoRelease(releaseNode, client);
                    if (string.IsNullOrWhiteSpace(assetJson))
                    {
                        return urlsConfiguradas;
                    }

                    var urls = ExtrairUrlsDoJson(assetJson);
                    if (urls.Count > 0)
                    {
                        return urls;
                    }
                    return urlsConfiguradas;
                }
                catch (Exception ex)
                {
                    LogError(nameof(CarregarUrlsDoLoaderPublico), $"Falha ao ler release do loader: {ex.Message}");
                    return urlsConfiguradas;
                }
            }

            private static string? EncontrarAssetJsonDoRelease(JsonObject releaseNode, HttpClient client)
            {
                var assets = releaseNode["assets"]?.AsArray();
                if (assets == null || assets.Count == 0)
                {
                    return null;
                }

                foreach (var assetNode in assets)
                {
                    var asset = assetNode as JsonObject;
                    if (asset == null)
                    {
                        continue;
                    }

                    var nome = asset["name"]?.ToString();
                    var contentType = asset["content_type"]?.ToString();
                    var assetUrl = asset["url"]?.ToString();
                    var browserDownloadUrl = asset["browser_download_url"]?.ToString();

                    if (string.IsNullOrWhiteSpace(assetUrl) && string.IsNullOrWhiteSpace(browserDownloadUrl))
                    {
                        continue;
                    }

                    var ehJson = (!string.IsNullOrWhiteSpace(nome) && nome.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                 || string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase);

                    if (!ehJson)
                    {
                        continue;
                    }

                    var urlDownload = !string.IsNullOrWhiteSpace(assetUrl) ? assetUrl : browserDownloadUrl;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(assetUrl))
                        {
                            using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl);
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                            using var response = client.SendAsync(request).GetAwaiter().GetResult();
                            response.EnsureSuccessStatusCode();
                            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        }

                        return client.GetStringAsync(browserDownloadUrl).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        LogError(nameof(EncontrarAssetJsonDoRelease), $"Falha ao baixar asset JSON {nome}: {ex.Message}");
                    }
                }
                return null;
            }

            private static List<string> ExtrairUrlsDoJson(string json)
            {
                var urls = new List<string>();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return urls;
                }

                try
                {
                    var node = JsonNode.Parse(json);
                    if (node is JsonArray array)
                    {
                        foreach (var item in array)
                        {
                            if (item is JsonValue value)
                            {
                                var url = value.ToString();
                                if (!string.IsNullOrWhiteSpace(url))
                                {
                                    urls.Add(url);
                                }
                                continue;
                            }

                            if (item is JsonObject obj)
                            {
                                var url = obj["manifestUrl"]?.ToString() ?? obj["url"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(url))
                                {
                                    urls.Add(url);
                                }
                            }
                        }
                    }
                    else if (node is JsonObject obj)
                    {
                        var section = obj["MinhasUrls"] as JsonArray
                                      ?? obj["urls"] as JsonArray
                                      ?? obj["manifestUrls"] as JsonArray
                                      ?? obj["manifests"] as JsonArray;

                        if (section != null)
                        {
                            foreach (var item in section)
                            {
                                if (item is JsonValue value)
                                {
                                    var url = value.ToString();
                                    if (!string.IsNullOrWhiteSpace(url))
                                    {
                                        urls.Add(url);
                                    }
                                    continue;
                                }

                                if (item is JsonObject entry)
                                {
                                    var url = entry["manifestUrl"]?.ToString() ?? entry["url"]?.ToString();
                                    if (!string.IsNullOrWhiteSpace(url))
                                    {
                                        urls.Add(url);
                                    }
                                }
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    LogError(nameof(ExtrairUrlsDoJson), $"Falha ao parsear JSON: {ex.Message}");
                }

                return urls;
            }

        public static string BaixarZipGithub(string token, string url, string pathDownload)
        {
            if (string.IsNullOrEmpty(pathDownload))
            {
                pathDownload = "./";
            }

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MeuAplicativo", "1.0"));
                if (!string.IsNullOrWhiteSpace(token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.raw"));

                try
                {
                    string dataVersaoLocal = "1900-01-01T00:00:00Z";
                    var versionFile = Path.Combine(pathDownload, "version.txt");
                    if (File.Exists(versionFile))
                    {
                        dataVersaoLocal = File.ReadAllText(versionFile);
                    }

                    HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    string jsonResponse = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    JsonNode releaseData = JsonNode.Parse(jsonResponse);
                    var tagName = releaseData["tag_name"]?.ToString();
                    var publishedAt = releaseData["published_at"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(publishedAt) && is_atualizado(dataVersaoLocal, publishedAt) == false)
                    {
                        var firstAsset = releaseData["assets"]?[0];
                        string downloadUrl = firstAsset?["url"]?.ToString();
                        string fileName = firstAsset?["name"]?.ToString();
                        dataVersaoLocal = releaseData["published_at"].ToString();

                        if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(fileName))
                        {
                            LogError(nameof(BaixarZipGithub), "Nenhum asset válido encontrado no release.");
                            return null;
                        }

                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                        Directory.CreateDirectory(pathDownload);
                        byte[] fileBytes = client.GetByteArrayAsync(downloadUrl).GetAwaiter().GetResult();

                        File.WriteAllBytes(pathDownload + fileName, fileBytes);
                        File.WriteAllText(versionFile, dataVersaoLocal);
                        return pathDownload + fileName;
                    }

                    var zipExistente = Directory.Exists(pathDownload)
                        ? Directory.GetFiles(pathDownload, "*.zip").FirstOrDefault()
                        : null;
                    var possuiDll = Directory.Exists(pathDownload)
                        && Directory.GetFiles(pathDownload, "*.dll", SearchOption.AllDirectories).Any();

                    if (!string.IsNullOrWhiteSpace(zipExistente) && !possuiDll)
                    {
                        moverDllDoZip(zipExistente);
                    }

                    return null;
                }
                catch (HttpRequestException e)
                {
                    LogError(nameof(BaixarZipGithub), $"Erro na requisição: {e.Message}");
                    return null;
                }
                catch (Exception ex)
                {
                    LogError(nameof(BaixarZipGithub), $"Falha inesperada: {ex.Message}");
                    return null;
                }

            }
        }


        private static void moverDllDoZip(string zipPath)
            {
                try
                {
                    var destinoRaiz = Path.GetDirectoryName(zipPath);
                    if (string.IsNullOrWhiteSpace(destinoRaiz))
                    {
                        LogError(nameof(moverDllDoZip), $"Destino inválido para ZIP: {zipPath}");
                        return;
                    }

                    using ZipArchive archive = ZipFile.OpenRead(zipPath);

                    Directory.CreateDirectory(destinoRaiz);

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == false)
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            continue;
                        }

                        var destinoArquivo = Path.GetFullPath(Path.Combine(destinoRaiz, entry.Name));
                        entry.ExtractToFile(destinoArquivo, true);
                    }
                }
                catch (Exception ex)
                {
                    LogError(nameof(moverDllDoZip), $"Falha ao processar o ZIP: {ex.Message}");
                }
                apagarZip(zipPath);
            }

        public static List<string> aplicarAtualizacao()
        {
            List<string> urls = pegarEnvEJson();
            string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                LogError(nameof(aplicarAtualizacao), "GITHUB_TOKEN não configurado.");
                return new List<string>();
            }

            List<string> pastas = new List<string>();
            foreach (string url in urls)
            {
                var pluginName = GetPluginNameFromUrl(url);
                if (string.IsNullOrWhiteSpace(pluginName)) continue;

                var basePath = Path.Combine(AuthController.RevitLoaderBasePath, "plugins");
                string pathDll = Path.Combine(basePath, pluginName) + "\\";
                pastas.Add(pathDll);

                try
                {
                    // Mantendo síncrono para garantir que as DLLs estejam prontas antes do loader tentar carregar
                    aplicarAtualizacao(token, url, pathDll);
                }
                catch (Exception ex)
                {
                    LogError(nameof(aplicarAtualizacao), $"Erro ao atualizar {pluginName}: {ex.Message}");
                }
            }
            return pastas.Distinct().ToList();
        }
        private static void aplicarAtualizacao(string token, string url, string caminhoArquivo)
        {
            string caminhoZip = BaixarZipGithub(token, url, caminhoArquivo);

            if (caminhoZip != null)
            {
                moverDllDoZip(caminhoZip);
            }
            else
            {
                return;
            }
        }
        private static void apagarZip(string path)
            {
                File.Delete(path);
            }

            private static void Log(string message)
            {
                try
                {
                    Directory.CreateDirectory(AuthController.RevitLoaderBasePath);
                    File.AppendAllText(LogPath, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
                }
                catch
                {
                    // best-effort logging; ignore failures
                }
            }

            

            private static void LogError(string method, string message)
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                var text = $"[{method}] {message}";
                Log(text);
            }

        }
    }
