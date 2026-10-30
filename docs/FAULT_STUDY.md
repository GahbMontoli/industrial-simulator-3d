# Estudo programacional de injeção de falhas

## Objetivo

Executar uma campanha determinística e de baixo custo computacional para observar
como a célula de envase responde a falhas de sensores e atuadores. O estudo roda
no núcleo headless, usa o mesmo passo lógico da cena e não depende do motor 3D.

Ele não calcula probabilidade de falha e não substitui análise de risco, teste em
hardware, validação de PLC real ou avaliação de segurança funcional.

## Cenários

| Cenário | Falha aplicada | Condição observável esperada |
|---|---|---|
| `Nominal` | nenhuma | alvo alcançado e comando de enchimento encerrado |
| `ContainerSensorAlwaysOff` | sensor de presença sempre desligado | produção bloqueada pela ausência de presença |
| `FillValveAlwaysOff` | válvula efetiva sempre fechada | controlador comanda enchimento, mas o volume não aumenta |
| `VolumeSensorStuck` | indicação de volume travada no valor inicial | capacidade física alcançada com comando ainda ativo |
| `FillValveAlwaysOn` | válvula efetiva sempre aberta | controlador comanda fechamento, mas o estado efetivo diverge |

## Métricas

- volume físico final do modelo;
- volume final indicado ao controlador;
- maior desvio entre volume físico e indicado;
- tempo simulado até o volume-alvo;
- passos com válvula aberta após atingir a capacidade;
- passos em que o comando PLC diverge do estado efetivo da válvula.

## Reproduzir

```powershell
Set-Location .\industrial-simulator-3d
dotnet run --project .\src\SimuladorIndustrial.Cli -c Release -- `
  --fault-study `
  --scene .\samples\celula-envase.scene.json `
  --output .\artifacts\fault-study.csv
```

Com a cena de exemplo, a execução contém 5 cenários de 8 segundos, totalizando
2.000 passos lógicos de 20 ms. O CSV é deliberadamente compacto: uma linha por
cenário, sem interface ou telemetria de alta frequência.

A mesma execução gera `fault-study.manifest.json`. Esse manifesto registra o
`runId`, a versão do contrato, a quantidade de linhas, a classificação
`synthetic`, o limite da evidência e hashes SHA-256 da cena de origem e do CSV.
Isso permite detectar troca ou alteração de arquivos antes da atualização no
Power BI sem transformar a simulação em evidência de campo.

## Leitura correta dos resultados

Uma condição `expectationMet=true` demonstra que o modelo reproduziu a resposta
definida para aquele cenário e protege contra regressões de software. Não prova
que um equipamento real terá a mesma resposta. Para avançar a evidência, conecte
um PLC por Modbus TCP, execute o roteiro de interoperabilidade e depois repita em
bancada desenergizada ou de baixa energia com critérios de segurança aprovados.
