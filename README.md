# Revit Loader

Carregador de plugins para o Autodesk Revit que baixa releases de plugins no GitHub, extrai as DLLs e inicializa automaticamente os `IExternalApplication` encontrados.

## Como funciona

1. Na inicialização do Revit, o loader chama `GithubService.aplicarAtualizacao()`.
2. O serviço lê `config/config.json` e `config/.env` (variável `GITHUB_TOKEN`) para obter as URLs de releases.
3. Para cada URL, baixa o ZIP do release, extrai as DLLs e grava a versão local.
4. As DLLs extraídas são carregadas e todas as classes que implementam `IExternalApplication` são inicializadas no evento `Idling`.

## Estrutura principal

- `Bootstrap/LoaderApplication.cs`: ponto de entrada do add-in do Revit e orquestração do carregamento.
- `Services/GithubService.cs`: download, cache e extração de ZIPs do GitHub.
- `Bootstrap/LoaderSettings.cs`: constantes de configuração.

## Configuração

Crie a pasta `config` ao lado da DLL do loader com:

- `config.json` contendo a lista de URLs:

```json
{
  "MinhasUrls": [
	"https://api.github.com/repos/ORG/REPO/releases/latest"
  ]
}
```

- `.env` com o token do GitHub:

```
GITHUB_TOKEN=seu_token_aqui
```

## Saída e logs

- Os plugins são baixados para `%LOCALAPPDATA%\RevitLoader\plugins\{NomeDoPlugin}\`.
- Logs e tokens são gravados em `%LOCALAPPDATA%\RevitLoader\`.

## Requisitos

- .NET 8
- Autodesk Revit (API disponível para execução do add-in)

## Build

Abra a solução no Visual Studio e compile o projeto `Loader.csproj`.

## Observações

O ZIP do release deve conter as DLLs do plugin. O loader extrai todas as DLLs do ZIP para a pasta do plugin.