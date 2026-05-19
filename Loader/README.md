# Revit Loader

Loader inicial para plug-ins do Revit que valida a versão publicada no GitHub antes de iniciar o restante do add-in.

## Como funciona

1. O Revit carrega este assembly primeiro via arquivo `.addin`.
2. No `OnStartup`, o loader consulta um manifesto hospedado no GitHub.
3. Se a versão remota for diferente da versão local, ele baixa o pacote atualizado para uma pasta de cache.
4. Depois disso, ele carrega a DLL correta do pacote e executa a classe de entrada do add-in.

## Estrutura esperada no GitHub

Publique um arquivo JSON público com este formato:

```json
{
  "version": "1.0.3",
  "packageUrl": "https://github.com/sua-conta/seu-repo/releases/download/v1.0.3/PluginPackage.zip",
  "entryAssemblyName": "MeuPlugin.dll",
  "entryClassName": "MeuPlugin.App",
  "releaseNotesUrl": "https://github.com/sua-conta/seu-repo/releases/tag/v1.0.3"
}
```

O `packageUrl` deve apontar para um arquivo zip contendo a DLL do plugin e suas dependências.
O loader procura primeiro por `entryAssemblyName`; se não houver, tenta encontrar uma única DLL no pacote. A classe apontada por `entryClassName` deve implementar `Autodesk.Revit.UI.IExternalApplication`.

## Pontos de ajuste

- Atualize `LoaderSettings` com a URL bruta do manifesto no GitHub.
- Ajuste a versão local do assembly quando publicar uma nova release.
- Configure `entryAssemblyName` e `entryClassName` no manifesto para apontar para a DLL e a classe corretas.
- O pacote baixado fica em `%LocalAppData%\RevitLoader\current` e é essa pasta que o loader usa para iniciar o plugin.

## Observações

Este scaffold assume .NET Framework 4.8, que é a base mais comum para Revit.
As referências `RevitAPI` e `RevitAPIUI` precisam apontar para a instalação do Revit usada no desenvolvimento.