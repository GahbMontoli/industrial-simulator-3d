# Aceitação OpenPLC Runtime v4 — 60 minutos

Status: **roteiro preparado, ainda não executado neste ambiente**.

## Objetivo

Comprovar que um OpenPLC Runtime v4 controla a célula de envase por Modbus TCP durante 60 minutos contínuos, sem perda silenciosa de estado, e que desconexão/reconexão aparecem na qualidade das tags e no journal.

## Pré-requisitos

1. Windows x64 com o Simulador Industrial 3D exportado ou em modo de desenvolvimento.
2. OpenPLC Runtime v4 instalado legalmente e operante.
3. Driver do simulador selecionado como **Modbus TCP Server**.
4. Bind local `127.0.0.1`, porta `5020`, Unit ID `1`.
5. PLC demonstrativo interno desativado enquanto o OpenPLC controla a célula.
6. Relógios do Windows e do ambiente OpenPLC sincronizados.
7. Pasta de evidência nova conforme `RESULTADO.template.md`.

Não exponha a porta 5020 em todas as interfaces durante o teste local. Não use a porta privilegiada 502 quando 5020 atende ao laboratório.

## Mapeamento do cenário autoral

Os endereços do arquivo de cena são base zero. Valores de 32 bits usam dois registradores em ordem de palavra mais significativa primeiro.

| Sinal | Direção PLC | Área Modbus | Endereço base | Operação esperada |
|---|---|---|---:|---|
| `recipienteDetectado` | entrada | discrete input | 0 | OpenPLC lê por FC02 |
| `volumePreenchido` (`float32`) | entrada | input registers | 0–1 | OpenPLC lê por FC04; IEEE-754, palavra alta primeiro |
| `bloqueadorFechado` | saída | coil | 0 | OpenPLC escreve por FC05 ou FC15 |
| `valvulaEnchimento` | saída | coil | 1 | OpenPLC escreve por FC05 ou FC15 |

Caso o configurador OpenPLC exiba endereços base um (`00001`, `10001`, `30001`, `40001`), documente a conversão usada; o valor transmitido no PDU Modbus continua base zero.

## Lógica mínima do PLC

Use nomes equivalentes aos da tabela. A lógica deve:

1. ao detectar recipiente, fechar o bloqueador;
2. somente abrir a válvula quando recipiente e bloqueador estiverem ativos;
3. fechar a válvula ao atingir o volume-alvo;
4. abrir o bloqueador após fechar a válvula;
5. nunca manter a válvula aberta sem detecção.

O programa OpenPLC e seu checksum devem ser anexados à evidência. Não copie lógica ou projeto de terceiros.

## Procedimento

### A. Pré-voo — 5 minutos

1. Inicie o OpenPLC e confirme estado `RUNNING`.
2. Abra o simulador, carregue `Célula de Envase 01`, selecione servidor Modbus e confirme `127.0.0.1:5020 / Unit 1`.
3. Execute `Reset` e depois `Run`.
4. Confirme no monitor de tags que o sensor muda sem erro de qualidade.
5. Confirme no journal uma leitura FC02/FC04 e uma escrita FC05/FC15.
6. Exporte a primeira evidência JSON/CSV e anote o horário UTC inicial.

Se qualquer passo falhar, pare: o ensaio de 60 minutos ainda não começou.

### B. Execução contínua — 60 minutos

1. Inicie o cronômetro somente após o primeiro ciclo completo controlado pelo OpenPLC.
2. Não altere cena, endereço, escala de tempo ou programa PLC durante o período.
3. A cada 10 minutos, registre:
   - horário UTC;
   - tick lógico;
   - total de ciclos completos;
   - qualidade das quatro tags;
   - estado da conexão;
   - contagem de erros e reconexões;
   - uso de memória do processo e FPS.
4. Exporte evidência JSON/CSV nos minutos 0, 30 e 60.
5. Ao final, pause o simulador e preserve os logs antes de encerrar processos.

### C. Desconexão e recuperação — ensaio separado

1. Com um ciclo estável, pare o OpenPLC por 30 segundos.
2. Confirme que a conexão é marcada como desconectada e que a qualidade das tags remotas deixa de ser `Good`.
3. Confirme que nenhuma saída antiga é tratada silenciosamente como comando atual.
4. Reinicie o OpenPLC sem reiniciar o simulador.
5. Confirme reconexão, qualidade recuperada e retorno do ciclo sem reset oculto.
6. Exporte evidência antes da falha, durante a falha e após a recuperação.

## Critérios de aprovação

- 60 minutos contínuos completos, medidos após o primeiro ciclo válido.
- Pelo menos 100 ciclos de envase completos e contagem monotônica.
- Nenhuma exceção não tratada, travamento ou encerramento inesperado.
- Nenhuma violação do intertravamento “válvula aberta sem recipiente e bloqueador”.
- Leituras e escritas Modbus correspondem ao mapeamento documentado.
- Timeout/desconexão altera a qualidade; reconexão e recuperação ficam no journal.
- Nenhuma divergência silenciosa entre valor PLC, tag efetiva e animação por mais de dois ciclos de polling.
- Evidências dos minutos 0, 30 e 60, mais o ensaio de recuperação, possuem SHA-256.

Qualquer critério ausente ou sem evidência resulta em **não aprovado** ou **inconclusivo**.

## Encerramento

Preencha [RESULTADO.template.md](RESULTADO.template.md), renomeie para `RESULTADO.md` dentro da pasta da execução e gere os hashes:

```powershell
Get-ChildItem -File -Recurse | Sort-Object FullName | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.FullName)"
} | Set-Content -LiteralPath .\SHA256SUMS.txt -Encoding Ascii
```

O comando deve ser executado dentro da pasta de evidência, nunca na raiz do projeto.
