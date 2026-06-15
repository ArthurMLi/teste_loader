using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace RevitLoader.App
{
    // OBSOLETO: Este controlador não é mais utilizado no fluxo principal do programa.
    // Mantido apenas para referência ou uso futuro, conforme solicitado.
    public static class AuthController
    {
        private static readonly HttpClient Client = new HttpClient();
        private static readonly string ApiBasePath = "http://192.168.1.160:8080/api/";
        private static readonly string LoaderControllerName = "RevitLoader";

        // Propriedade para obter o caminho base dinamicamente
        public static string RevitLoaderBasePath
        {
            get
            {
                var appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appDataLocal, "RevitLoader");
            }
        }

        private static readonly string AccessTokenFilePath = Path.Combine(RevitLoaderBasePath, "access.token");
        private static readonly string RefreshTokenFilePath = Path.Combine(RevitLoaderBasePath, "refresh.token");
        private static readonly string AuthLogPath = Path.Combine(RevitLoaderBasePath, "auth.log");

        public static string AccessToken { get; private set; }
        public static string RefreshToken { get; private set; }

        public static bool InitiateLoginFlow()
        {
            Directory.CreateDirectory(RevitLoaderBasePath);
            Log("InitiateLoginFlow: entrada. Usando controller=" + LoaderControllerName);
            if (AuthenticateWithSavedToken(LoaderControllerName))
            {
                Log("InitiateLoginFlow: já autenticado via token salvo.");
                return true;
            }
            while (true)
            {
                var loginWindow = new LoginWindow();
                var result = loginWindow.ShowDialog();
                if (result != true)
                {
                    Log("InitiateLoginFlow: usuário cancelou o login.");
                    return false;
                }

                Log($"InitiateLoginFlow: tentativa de login para email={loginWindow.Email}");
                if (AuthenticateUser(LoaderControllerName, loginWindow.Email, loginWindow.Senha))
                {
                    Log("InitiateLoginFlow: autenticação bem-sucedida para email=" + loginWindow.Email);
                    return true;
                }
                Log("InitiateLoginFlow: autenticação falhou para email=" + loginWindow.Email);
                MessageBox.Show("Falha na autenticação. Verifique suas credenciais e tente novamente.");
            }
        }

        // Garante que, na inicialização do loader, exista um token válido.
        // Fluxo: se há token salvo válido -> retorna true; caso contrário -> abre login e persiste token.
        public static bool EnsureAuthenticatedForApplicationStartup()
        {
            Log("EnsureAuthenticatedForApplicationStartup: verificando token salvo para controller=" + LoaderControllerName);
            if (AuthenticateWithSavedToken(LoaderControllerName))
            {
                Log("EnsureAuthenticatedForApplicationStartup: token válido encontrado");
                return true;
            }

            Log("EnsureAuthenticatedForApplicationStartup: token inexistente ou inválido, abrindo diálogo de login");
            return InitiateLoginFlow();
        }

        public static bool ValidatePluginAccess(string pluginName)
        {
            var controller = GetNormalizedControllerName(pluginName);
            Log("ValidatePluginAccess: plugin=" + pluginName + " controller=" + controller);
            var result = ValidateCurrentAccessTokenOnServer(controller);
            Log("ValidatePluginAccess: resultado=" + result + " for plugin=" + pluginName);
            return result;
        }

        public static bool AuthenticatePluginUser(string pluginName, string email, string password)
        {
            return AuthenticateUser(GetNormalizedControllerName(pluginName), email, password);
        }

        public static bool AuthenticateWithSavedToken()
        {
            return AuthenticateWithSavedToken(LoaderControllerName);
        }

        public static bool AuthenticateWithSavedToken(string controllerName)
        {
            try
            {
                Log("AuthenticateWithSavedToken: entrada para controller=" + controllerName);
                
                var persistedAccess = LoadAccessTokenFromStorage();
                var persistedRefresh = LoadRefreshTokenFromStorage();

                // Se não existir access.token ou refresh.token, retorna false para abrir tela de login
                if (string.IsNullOrWhiteSpace(persistedAccess) || string.IsNullOrWhiteSpace(persistedRefresh))
                {
                    Log("AuthenticateWithSavedToken: tokens não encontrados no disco.");
                    return false;
                }

                AccessToken = persistedAccess;
                RefreshToken = persistedRefresh;

                // Fluxo da N-ésima vez: Tenta dar um refresh direto no token salvo.
                // A validação inicial via /validar foi removida para o loader conforme solicitado.
                Log("AuthenticateWithSavedToken: tentando atualizar token via refresh...");
                var resultado = RefreshAccessAndRefreshTokens(RefreshToken);
                
                if (!string.IsNullOrWhiteSpace(resultado.IdToken))
                {
                    AccessToken = resultado.IdToken;
                    RefreshToken = resultado.NewRefreshToken;

                    SaveAccessTokenToStorage(AccessToken);
                    SaveRefreshTokenToStorage(RefreshToken);
                    
                    Log("AuthenticateWithSavedToken: token atualizado com sucesso via refresh.");
                    return true;
                }

                Log("AuthenticateWithSavedToken: falha ao atualizar token via refresh.");
                return false;
            }
            catch (Exception ex)
            {
                Log("AuthenticateWithSavedToken: exceção inesperada: " + ex.Message);
                return false;
            }
        }

        public static bool AuthenticateUser(string email, string password)
        {
            return AuthenticateUser(LoaderControllerName, email, password);
        }

        private static bool AuthenticateUser(string controllerName, string email, string password)
        {
            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(new { email, password }),
                    Encoding.UTF8,
                    "application/json");

                var loginUrl = BuildApiEndpointUrl(controllerName, "login");
                Log("AuthenticateUser(credentials): POST " + loginUrl + " for email=" + email);
                var response = Client.PostAsync(loginUrl, content).Result;
                if (!response.IsSuccessStatusCode)
                {
                    Log("AuthenticateUser(credentials): resposta não-Success StatusCode=" + (int)response.StatusCode);
                    return false;
                }

                var responseBody = response.Content.ReadAsStringAsync().Result;
                var accessToken = ExtractAccessTokenFromJson(responseBody);

                // Extrai também o refreshToken do corpo da resposta
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                string refreshToken = root.TryGetProperty("refreshToken", out var refEl) ? refEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    Log("AuthenticateUser(credentials): token vazio retornado");
                    return false;
                }

                AccessToken = accessToken;
                RefreshToken = refreshToken;
                Log("AuthenticateUser(credentials): token recebido");

                try
                {
                    SaveAccessTokenToStorage(AccessToken);
                    SaveRefreshTokenToStorage(RefreshToken);
                    Log("AuthenticateUser(credentials): token persistido no disco com sucesso");
                }
                catch
                {
                    Log("AuthenticateUser(credentials): falha ao persistir o token no disco");
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static (string IdToken, string NewRefreshToken) RefreshAccessAndRefreshTokens(string refreshToken)
        {
            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(new { refreshToken }),
                    Encoding.UTF8,
                    "application/json");

                // Usando o helper para garantir que a URL esteja correta e sem barras duplas
                var refreshUrl = BuildApiEndpointUrl(LoaderControllerName, "refresh");
                Log("RefreshAccessAndRefreshTokens: POST " + refreshUrl);

                var response = Client.PostAsync(refreshUrl, content).Result;
                if (!response.IsSuccessStatusCode)
                {
                    Log("RefreshAccessAndRefreshTokens: resposta não-Success StatusCode=" + (int)response.StatusCode);
                    // Adicional: Logar o corpo do erro ajuda a identificar por que o servidor rejeitou
                    var errorBody = response.Content.ReadAsStringAsync().Result;
                    Log("RefreshAccessAndRefreshTokens: erro da API: " + errorBody);
                    return (null, null);
                }

                var responseBody = response.Content.ReadAsStringAsync().Result;
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                string idToken = root.TryGetProperty("idToken", out var idEl) ? idEl.GetString() :
                                 root.TryGetProperty("accessToken", out var accEl) ? accEl.GetString() :
                                 root.TryGetProperty("token", out var tEl) ? tEl.GetString() : null;

                string newRefreshToken = root.TryGetProperty("refreshToken", out var refEl) ? refEl.GetString() : null;

                return (idToken, newRefreshToken);
            }
            catch (Exception ex)
            {
                Log("RefreshAccessAndRefreshTokens: exceção: " + ex.Message);
                return (null, null);
            }
        }

        public static string GetCurrentAccessToken()
        {
            return AccessToken;
        }

        public static bool SignOutUser()
        {
            return ForceSignOutUser();
        }

        public static bool ForceSignOutUser()
        {
            try
            {
                AccessToken = null;
                RefreshToken = null;
                DeleteSavedAccessTokenFromStorage();
                DeleteSavedRefreshTokenFromStorage();
                return true;
            }
            catch (Exception ex)
            {
                Log("ForceSignOutUser: erro ao remover token: " + ex.Message);
                return false;
            }
        }

        private static bool ValidateCurrentAccessTokenOnServer(string controllerName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(AccessToken))
                {
                    return false;
                }

                var validarUrl = BuildApiEndpointUrl(controllerName, "validar");
                using var request = new HttpRequestMessage(HttpMethod.Get, validarUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

                var response = Client.SendAsync(request).Result;
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractAccessTokenFromJson(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (root.TryGetProperty("token", out var tokenElement))
                {
                    return tokenElement.GetString();
                }

                if (root.TryGetProperty("accessToken", out var accessTokenElement))
                {
                    return accessTokenElement.GetString();
                }

                if (root.TryGetProperty("idToken", out var idTokenElement))
                {
                    return idTokenElement.GetString();
                }

                return null;
            }
            catch
            {
                Log("ExtractAccessTokenFromJson: falha ao parsear responseBody");
                return null;
            }
        }

        private static string BuildApiEndpointUrl(string controllerName, string action)
        {
            return BuildApiControllerBasePath(controllerName) + action;
        }

        private static string BuildApiControllerBasePath(string controllerName)
        {
            var normalizedControllerName = GetNormalizedControllerName(controllerName);
            return ApiBasePath + "AuthController" + normalizedControllerName + "/";
        }

        private static string GetNormalizedControllerName(string pluginName)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                return string.Empty;
            }

            var normalized = Path.GetFileNameWithoutExtension(pluginName.Trim());
            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
        }

        private static void SaveAccessTokenToStorage(string token)
        {
            var directory = Path.GetDirectoryName(AccessTokenFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var protectedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(token),
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                File.WriteAllBytes(AccessTokenFilePath, protectedBytes);
                Log("SaveAccessTokenToStorage: token protegido salvo em " + AccessTokenFilePath);
                return;
            }
            catch (Exception ex)
            {
                // DPAPI failed for some reason; fallback to plain-text with warning (best-effort)
                Log("SaveAccessTokenToStorage: falha DPAPI Protect: " + ex.Message + "; tentando fallback em texto simples.");
                try
                {
                    File.WriteAllText(AccessTokenFilePath, token, Encoding.UTF8);
                    Log("SaveAccessTokenToStorage: fallback texto salvo em " + AccessTokenFilePath);
                    return;
                }
                catch (Exception ex2)
                {
                    Log("SaveAccessTokenToStorage: falha ao salvar token em texto simples: " + ex2.Message);
                }
            }
        }

        private static string LoadAccessTokenFromStorage()
        {
            try
            {
                if (!File.Exists(AccessTokenFilePath))
                {
                    Log("LoadAccessTokenFromStorage: arquivo inexistente em " + AccessTokenFilePath);
                    return null;
                }
                // Primeiro, tenta ler como bytes protegidos (DPAPI)
                try
                {
                    var protectedBytes = File.ReadAllBytes(AccessTokenFilePath);
                    var bytes = ProtectedData.Unprotect(
                        protectedBytes,
                        optionalEntropy: null,
                        scope: DataProtectionScope.CurrentUser);

                    var token = Encoding.UTF8.GetString(bytes);
                    Log("LoadAccessTokenFromStorage: token protegido carregado com sucesso");
                    return token;
                }
                catch (Exception ex)
                {
                    Log("LoadAccessTokenFromStorage: falha ao unprotect DPAPI: " + ex.Message + "; tentando leitura texto simples.");
                }

                // Fallback: tenta ler como texto simples
                try
                {
                    var txt = File.ReadAllText(AccessTokenFilePath, Encoding.UTF8);
                    Log("LoadAccessTokenFromStorage: token lido como texto simples com sucesso");
                    return txt;
                }
                catch (Exception ex2)
                {
                    Log("LoadAccessTokenFromStorage: falha ao ler token em texto simples: " + ex2.Message);
                    return null;
                }
            }
            catch
            {
                Log("LoadAccessTokenFromStorage: exceção inesperada ao tentar carregar token");
                return null;
            }
        }

        private static void SaveRefreshTokenToStorage(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            var directory = Path.GetDirectoryName(RefreshTokenFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var protectedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(token),
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                File.WriteAllBytes(RefreshTokenFilePath, protectedBytes);
                Log("SaveRefreshTokenToStorage: refresh token protegido salvo.");
            }
            catch (Exception ex)
            {
                Log("SaveRefreshTokenToStorage: falha DPAPI: " + ex.Message + "; tentando fallback texto simples.");
                try
                {
                    File.WriteAllText(RefreshTokenFilePath, token, Encoding.UTF8);
                }
                catch (Exception ex2)
                {
                    Log("SaveRefreshTokenToStorage: falha fallback: " + ex2.Message);
                }
            }
        }

        private static string LoadRefreshTokenFromStorage()
        {
            try
            {
                if (!File.Exists(RefreshTokenFilePath))
                {
                    Log("LoadRefreshTokenFromStorage: arquivo inexistente em " + RefreshTokenFilePath);
                    return null;
                }

                try
                {
                    var protectedBytes = File.ReadAllBytes(RefreshTokenFilePath);
                    var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    var token = Encoding.UTF8.GetString(bytes);
                    Log("LoadRefreshTokenFromStorage: refresh token protegido carregado com sucesso");
                    return token;
                }
                catch (Exception ex)
                {
                    Log("LoadRefreshTokenFromStorage: falha ao unprotect DPAPI: " + ex.Message + "; tentando leitura texto simples.");
                    // Fallback texto simples
                    try
                    {
                        var txt = File.ReadAllText(RefreshTokenFilePath, Encoding.UTF8);
                        Log("LoadRefreshTokenFromStorage: refresh token lido como texto simples com sucesso");
                        return txt;
                    }
                    catch (Exception ex2)
                    {
                        Log("LoadRefreshTokenFromStorage: falha ao ler refresh token em texto simples: " + ex2.Message);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("LoadRefreshTokenFromStorage: exceção inesperada ao tentar carregar refresh token: " + ex.Message);
                return null;
            }
        }

        private static void DeleteSavedAccessTokenFromStorage()
        {
            try
            {
                if (File.Exists(AccessTokenFilePath))
                {
                    File.Delete(AccessTokenFilePath);
                }
            }
            catch
            {
            }
        }

        private static void DeleteSavedRefreshTokenFromStorage()
        {
            try
            {
                if (File.Exists(RefreshTokenFilePath))
                {
                    File.Delete(RefreshTokenFilePath);
                }
            }
            catch
            {
            }
        }

        private static void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(AuthLogPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(AuthLogPath, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
            catch
            {
                // best-effort logging
            }
        }
    }
}