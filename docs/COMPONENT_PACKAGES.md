# Pacotes de componentes instaláveis

O Simulador Industrial aceita pacotes autorais com a extensão
`.silabcomponent`. O arquivo é um ZIP com `component-package.json` na raiz e,
opcionalmente, modelos e ativos auxiliares.

## Instalar

1. Abra **Editor 3D**.
2. Escolha **INSTALAR COMPONENTE...**.
3. Selecione o arquivo `.silabcomponent`.
4. O catálogo é recarregado imediatamente.

Os pacotes ficam em `user://component-packages/<packageId>/<version>`. No
Windows, `user://` é resolvido pelo Godot dentro dos dados do usuário da
aplicação.

## Contrato inicial

O manifesto descreve:

- identidade e versão SemVer do pacote;
- nome, categoria e descrição de cada componente;
- envelope visual e cor;
- portas digitais ou analógicas;
- parâmetros tipados e valores padrão;
- modelo opcional `.glb` contido no próprio pacote.

O catálogo nativo e os pacotes usam o mesmo contrato. Uma cena continua
armazenando somente o `type` do componente e seus parâmetros, portanto o
formato existente permanece compatível.

## Segurança

O instalador valida tamanho, quantidade de entradas, manifesto, tipos,
referências de modelos e caminhos antes da extração. São rejeitados:

- caminhos absolutos ou com `..`;
- entradas duplicadas e links simbólicos;
- DLLs, executáveis e scripts;
- modelos referenciados que não existam no pacote;
- pacote que exija uma versão mais nova do simulador;
- reinstalação silenciosa da mesma versão;
- tipos que colidam com o catálogo nativo.

Esta primeira versão é deliberadamente orientada a dados. Comportamento
programável será introduzido por uma DSL limitada ou por plugins assinados,
após definição do modelo de ameaça.

## Criar o exemplo

```powershell
& .\scripts\New-ComponentPackage.ps1 `
  -SourceDirectory .\samples\component-packages\source\motor-basico `
  -OutputPath .\samples\component-packages\motor-basico.silabcomponent
```

O pacote de exemplo adiciona um motor de indução ao catálogo. Componentes com
visual primitivo são renderizados diretamente. Pacotes com `kind: model` são
validados e instalados, mas ainda usam o envelope declarado como representação
no editor até o carregador seguro de GLB ser habilitado.
