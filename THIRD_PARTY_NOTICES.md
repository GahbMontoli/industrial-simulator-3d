# Avisos de terceiros

Inventário revisado em 27 de outubro de 2026. Este projeto é independente e não é afiliado, patrocinado nem certificado pela Real Games ou pelos mantenedores das ferramentas abaixo.

## Dependências distribuídas no aplicativo Windows

### Godot Engine 4.7.2 e Godot.NET.Sdk 4.7.2

- Projeto: <https://godotengine.org/>
- Código-fonte: <https://github.com/godotengine/godot>
- Licença: MIT
- Copyright (c) 2014-present Godot Engine contributors.
- Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur.

O pacote é exportado com templates oficiais do Godot. A rotina de entrega inclui `GODOT_LICENSE.txt` e o inventário oficial completo `GODOT_COPYRIGHT.txt`, ambos obtidos do tag `4.7.2-stable` e verificados por SHA-256.

Texto da licença MIT do Godot:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Microsoft .NET 8

- Projeto: <https://github.com/dotnet/runtime>
- Licença: MIT
- Copyright (c) .NET Foundation and Contributors.

O runtime .NET incorporado pelo exportador é regido pelos avisos e licenças presentes na distribuição oficial. A licença MIT concede os mesmos direitos e impõe as mesmas condições reproduzidas acima, com o copyright da .NET Foundation and Contributors.

## Dependências usadas somente para build e testes

Esses pacotes não são necessários para executar o aplicativo exportado:

| Componente | Versão | Uso | Licença declarada pelo projeto |
|---|---:|---|---|
| Microsoft.NET.Test.Sdk | 17.8.0 | host de testes | MIT |
| xUnit.net | 2.5.3 | testes unitários | Apache-2.0 |
| xunit.runner.visualstudio | 2.5.3 | adaptador de testes | Apache-2.0 |
| coverlet.collector | 6.0.0 | cobertura de testes | MIT |

Fontes oficiais: <https://github.com/microsoft/vstest>, <https://github.com/xunit/xunit> e <https://github.com/coverlet-coverage/coverlet>.

O texto da Apache License 2.0 está disponível em <https://www.apache.org/licenses/LICENSE-2.0>. Os pacotes restaurados pelo NuGet também conservam seus metadados e arquivos de licença originais no cache da ferramenta.

## Conteúdo autoral do projeto

O código, a cena JSON, a geometria procedural, os textos e a identidade visual em `src/`, `samples/` e `docs/images/` foram criados para este projeto. Nenhuma DLL, cena, textura, ícone, áudio, modelo 3D ou outro recurso do Factory I/O integra esta distribuição.

Este arquivo documenta terceiros; ele não define a licença do código autoral do projeto. Uma licença do projeto deve ser escolhida explicitamente pelo titular antes de distribuição pública.
