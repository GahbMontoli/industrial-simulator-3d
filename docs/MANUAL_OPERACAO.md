# Manual de operação

## Iniciar

1. Extraia todo o pacote para uma pasta gravável.
2. Execute `Iniciar-SimuladorIndustrial3D.cmd`.
3. Na primeira abertura, siga as quatro etapas da tela de boas-vindas.
4. Use **EXECUTAR**, **PAUSAR**, **REINICIAR** e **AVANÇAR 1 PASSO** para controlar o relógio lógico.

O cenário inicial é `samples/celula-envase.scene.json`. A aplicação valida o arquivo antes de iniciar e não executa scripts, caminhos externos ou URLs definidos pela cena.

## Navegação da interface

O menu lateral separa o programa em seis áreas:

1. **Visão geral**: processo 3D, estado operacional, métricas resumidas e timeline.
2. **Simulação**: processo 3D e controles do ciclo.
3. **Editor 3D**: edição dos componentes, tags, falhas e cena.
4. **Diagnóstico**: saúde das tags, métricas detalhadas, eventos e evidências.
5. **Conexões**: configuração e operação do Modbus TCP.
6. **Ajuda**: primeiros passos, atalhos e preferências.

Use `Alt+1` até `Alt+6` para abrir essas áreas diretamente. O botão superior do menu alterna entre os modos expandido e recolhido. `Tab` e `Shift+Tab` percorrem os controles; `Enter` ou `Espaço` aciona o item focado.

Em **Ajuda**, é possível configurar escala de 75% a 150%, página inicial, exibição da tela de boas-vindas e confirmações para ações críticas. As preferências ficam no diretório seguro `user://` do Godot e são validadas antes de serem carregadas.

## Navegação 3D

- `F`: alterna entre câmera orbital e livre.
- Orbital: botão esquerdo orbita, botão direito ou central desloca, roda aproxima e afasta.
- Livre: `WASD` movimenta, `Q/E` desce e sobe, botão direito controla o olhar.
- `Home`: restaura a vista inicial.
- Duplo clique no piso: altera o pivô da câmera orbital.

## Editor 3D

- `F2`: abre ou fecha o editor.
- `Tab`: seleciona o próximo componente.
- `WASD` e `R/F`: movem com snap de `0,25 m`; `Q/E` giram.
- `Ctrl+D`, `Delete`, `Ctrl+Z`, `Ctrl+Y`: duplicam, excluem, desfazem e refazem.
- `Ctrl+S`: salva a cena validada no diretório seguro da aplicação.

O painel lateral contém árvore, catálogo, transformações numéricas, editor de tags, forçamento e injeção de falhas. O botão **ABRIR** recarrega a última cena salva pelo editor.

## Modbus TCP

Abra **Conexões** (`Alt+5`), selecione cliente ou servidor, informe host, porta e Unit ID e escolha **VALIDAR E APLICAR**. A configuração inválida é rejeitada sem substituir a conexão atual. Para reutilizá-la após reiniciar, salve a cena no Editor 3D.

O botão **INICIAR MODBUS** pede confirmação, desativa o PLC demonstrativo e transfere o controle para a conexão configurada. **PARAR MODBUS** devolve o controle ao PLC demonstrativo.

No papel servidor, o mapeamento é:

| Direção da tag | Tipo | Área | Operação do PLC |
|---|---|---|---|
| `plcInput` | `bool` | discrete inputs | FC02 |
| `plcInput` | `int32`/`float32` | input registers | FC04 |
| `plcOutput` | `bool` | coils | FC01/FC05/FC15 |
| `plcOutput` | `int32`/`float32` | holding registers | FC03/FC06/FC16 |

No papel cliente, a aplicação espelha periodicamente as quatro áreas remotas para as tags de mesma direção, tipo e endereço. Falha de comunicação altera a qualidade, e uma conexão perdida é tentada novamente a cada dois segundos. Valores de 32 bits ocupam dois registradores, com a palavra mais significativa primeiro.

Use `127.0.0.1` e uma porta não privilegiada, como `5020`, em laboratório. Não exponha o servidor em interfaces públicas sem controles de rede externos.

## Diagnóstico e evidência

Abra **Diagnóstico** (`Alt+4`) para consultar qualidade e valor das tags, timeline, passos perdidos e métricas de lógica. O painel operacional também exibe estado Modbus, requisições/falhas e métricas de lógica, física e renderização. **EXPORTAR EVIDÊNCIA** gera JSON e CSV versionados no diretório de dados da aplicação.

O ensaio real de 60 minutos com OpenPLC deve seguir `docs/interop/openplc-60min/README.md`. Ele somente pode ser marcado como aprovado após execução com evidências e hashes.

## Limitações da versão 0.2.0

- O executável não possui assinatura de código; o Windows pode exibir SmartScreen.
- O pacote não inclui OpenPLC nem programa de PLC.
- Não há importação de cenas ou assets proprietários.
