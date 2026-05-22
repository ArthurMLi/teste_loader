// Início de um arquivo .cs
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Net;
using DotNetEnv;
using System.Reflection; // Essencial para plugins e add-ins
using Microsoft.Extensions.Configuration;

namespace TesteUnitario
{
    class Github
    {
        public static async Task Main()
        {
            Console.WriteLine("[Main] Iniciando aplicação de atualização...");
            var urls = pegarEnvEJson();
            Console.WriteLine($"[Main] URLs carregadas: {urls?.Count ?? 0}");

            string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN"); // Seu token gerado no GitHub

            try
            {
                foreach (string url in urls ?? new List<string>())
                {
                    Console.WriteLine($"[Main] Processando URL: {url}");
                    var pluginName = GetPluginNameFromUrl(url);
                    Console.WriteLine($"[Main] Plugin identificado: {pluginName}");
                    string pathDll = Path.Combine(".\\", pluginName) + "\\";
                    
                    await aplicarAtualizacao(token, url, pathDll);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Main][Erro] Exceção no loop principal: {ex}");
            }

            Console.WriteLine("[Main] Finalizando método Main.");
        }
        private static List<string> pegarEnvEJson()
        {
            try
            {
                // 1. Descobre o caminho completo da SUA DLL (ex: C:\Users\...\Addins\MeuPlugin.dll)
                string caminhoDaSuaDll = Assembly.GetExecutingAssembly().Location;
                // 2. Extrai apenas a pasta onde a DLL está (ex: C:\Users\...\Addins\)
                string diretorioDoAddin = Path.GetDirectoryName(caminhoDaSuaDll);
                string caminhoPastaConfig = diretorioDoAddin;
                //loop para voltar 3 pastas para trás, saindo da pasta do add-in e chegando na pasta do projeto onde o configestá
                //paneas para teste local, tirar
                for (int i = 0; i < 3; i++)
                {
                    var parent = Directory.GetParent(caminhoPastaConfig);
                    if (parent == null) break;
                    caminhoPastaConfig = parent.FullName;
                }

                // 3. Monta o caminho exato do arquivo .env
                string caminhoJson = Path.Combine(caminhoPastaConfig, "config");
                string caminhoEnv = Path.Combine(caminhoPastaConfig, "config", ".env");
                Console.WriteLine($"[pegarEnvEJson] .env esperado em: {caminhoEnv}");

                // Carrega o .env
                Env.Load(caminhoEnv);
                Console.WriteLine($"[pegarEnvEJson] Token carregado do .env: {caminhoEnv}");

                var config = new ConfigurationBuilder()
                    .SetBasePath(caminhoJson) // <--- Diz ao .NET para procurar os arquivos JSON nesta pasta
                    .AddJsonFile("config.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables() // <--- Une as variáveis do .env com o sistema de configuração do .NET
                    .Build();

                var listaDeUrls = config.GetSection("MinhasUrls").Get<List<string>>();
                Console.WriteLine($"[pegarEnvEJson] URLs encontradas no config: {listaDeUrls?.Count ?? 0}");
                return listaDeUrls;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[pegarEnvEJson][Erro] Falha ao carregar config/.env: {ex}");
                return new List<string>();
            }
        }
        private static string? GetPluginNameFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                var u = new Uri(url);

                // Limpa os segmentos (Remove as barras e espaços vazios)
                var segs = u.Segments
                            .Select(s => s.Trim('/'))
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToArray();

                // 1. Lógica para a API do GitHub
                // Se a URL começa com "repos", o índice 0 é "repos", o 1 é o dono, e o 2 é o repositório.
                if (segs.Length >= 3 && segs[0].Equals("repos", StringComparison.OrdinalIgnoreCase))
                {
                    return segs[2]; // Vai retornar "projeto-cad"
                }

                // 2. Lógica Original (Fallback)
                if (segs.Length == 0) return u.Host.Replace('.', '_');
                var last = segs.Last();
                var name = Path.GetFileNameWithoutExtension(last);

                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch
            {
                return null;
            }
        }

        private static bool is_atualizado(string dataLocal, string dataGithub)
        {
            DateTimeOffset dataL = DateTimeOffset.Parse(dataLocal);
            DateTimeOffset dataG = DateTimeOffset.Parse(dataGithub);

            // Comparação direta com operadores
            if (dataL < dataG) return false;
            else return true;
        }
        public async static Task<string> BaixarZipGithub(string token, string url, string pathDownload)
        {
            Console.WriteLine($"[BaixarZipGithub] Iniciando download para URL: {url}");
            string dataVersaoLocal = "2024-06-01T00:00:00Z"; // tem que pegar do arquivo local, mas por enquanto é só um exemplo

            if (string.IsNullOrEmpty(pathDownload))
            {
                pathDownload = "./"; // Caminho onde o arquivo será salvo, aqui é a pasta atual do programa
            }
            using (HttpClient client = new HttpClient())
            {
                //configuracoes do cliente HTTP pedidas pelo github para fazer a requisição
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MeuAplicativo", "1.0"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.raw"));

                try
                {
                    Console.WriteLine("Buscando arquivo...");

                    // Faz a requisição GET
                    HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
                    Console.WriteLine($"[BaixarZipGithub] GET {url} -> {response.StatusCode}");
                    //Console.WriteLine(response);
                    // Lança uma exceção se o status não for de sucesso (ex: 404, 401)
                    response.EnsureSuccessStatusCode();
                    Console.WriteLine($"[BaixarZipGithub] Requisição OK: {response.StatusCode}");
                   string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // Transforma o texto JSON em um objeto que podemos navegar facilmente
                    JsonNode releaseData = JsonNode.Parse(jsonResponse);
                    Console.WriteLine($"Versão mais recente: {releaseData}");

                    if (is_atualizado(dataVersaoLocal, releaseData["published_at"].ToString()) == false)
                    {

                        string downloadUrl = releaseData["assets"][0]["url"]?.ToString();
                        string fileName = releaseData["assets"][0]["name"]?.ToString();

                        // configuracaoes adicionais para baixar o arquivo binário
                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
    
                        Directory.CreateDirectory(pathDownload); // Garante que a pasta de destino existe
                        byte[] fileBytes = await client.GetByteArrayAsync(downloadUrl);
                        Console.WriteLine($"[BaixarZipGithub] Arquivo baixado: {fileName} (path: {pathDownload} bytes)");
                        // Salva o arquivo na mesma pasta onde o script está rodando
                        await File.WriteAllBytesAsync(pathDownload + fileName, fileBytes);
                        
                        Console.WriteLine($"[BaixarZipGithub] Arquivo salvo: {fileName} (destino: {pathDownload})");

                        return pathDownload + fileName;
                    }
                    else
                    {
                        Console.WriteLine("O arquivo já está atualizado.");
                        return "arquivo atualizado";
                    }
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"[BaixarZipGithub][Erro] Erro na requisição: {e}");
                    return null;
                }

            }
        }


        private async static void moverDllDoZip(string zipPath, string destinationPath)
        {
            try
            {
                // Cria a pasta de destino se ela ainda não existir
                   

                Console.WriteLine($"Abrindo o arquivo: {zipPath}");

                // 2. Abre o arquivo ZIP apenas para leitura
                using ZipArchive archive = ZipFile.OpenRead(zipPath);

                Console.WriteLine("\n--- Arquivos encontrados ---");

                // 3. Passa por cada item (arquivo ou pasta) dentro do ZIP
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".dll") == false) continue;

                    Console.WriteLine($"- {entry.FullName}");

                    // Monta o caminho completo de onde o arquivo vai ser salvo no seu computador
                    destinationPath = Path.GetFullPath(Path.Combine(destinationPath, entry.FullName));

                    // 4. Verifica se o item é apenas uma pasta (pastas terminam com / ou têm o Name vazio)
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        // Apenas cria a pasta e vai para o próximo item
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }

                    // 5. Garante que a pasta onde o arquivo vai ficar existe 
                    // (necessário caso o ZIP tenha arquivos dentro de subpastas)
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                    // 6. Extrai/Move o arquivo de fato. O 'true' significa que ele vai sobrescrever se já existir.
                    entry.ExtractToFile(destinationPath, true);

                }

                Console.WriteLine("\n[Sucesso] Todos os arquivos foram movidos/extraídos!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Erro] Falha ao processar o ZIP: {ex.Message}");
            }
            apagarZip(zipPath);
        }
        public async static Task aplicarAtualizacao(string token, string url, string caminhoArquivo)
        {
            Console.WriteLine($"[aplicarAtualizacao] Iniciando para URL: {url}");
            string caminhoZip = await BaixarZipGithub(token, url, caminhoArquivo);
            Console.WriteLine($"[aplicarAtualizacao] Resultado download: {caminhoArquivo ?? "null"}");
            if (caminhoZip == null)
            {
                Console.WriteLine("[aplicarAtualizacao][Erro] Caminho do arquivo é nulo, pulando mover/extrair.");
                return;
            }
            if (caminhoArquivo != "arquivo atualizado")
            {
                moverDllDoZip(caminhoZip, caminhoArquivo);
            }
            else
            {
                Console.WriteLine("[aplicarAtualizacao] Nada a atualizar.");
            }
            Console.WriteLine("[aplicarAtualizacao] Concluído.");
        }
        private static void apagarZip(string path)
        {
            File.Delete(path);
        }
    }
}