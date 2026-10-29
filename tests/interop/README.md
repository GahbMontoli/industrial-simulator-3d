# Testes de interoperabilidade

Este diretório contém roteiros manuais/repetíveis contra sistemas externos. Eles não fazem parte de `dotnet test` e nenhum resultado deve ser marcado como aprovado sem evidência produzida por uma execução real.

## OpenPLC Runtime v4

O roteiro principal está em [openplc-60min/README.md](openplc-60min/README.md). Ele valida:

- servidor Modbus TCP local na porta 5020;
- leitura de entradas discretas e registradores de entrada;
- escrita de coils;
- ciclo de envase sob controle externo;
- perda e retomada de conexão sem estado silenciosamente válido;
- execução contínua por 60 minutos;
- exportação de logs, configuração e hashes.

## Política de evidência

- Use uma nova pasta por execução: `evidence/YYYYMMDD-HHMMSS/`.
- Não versionar senhas, tokens, certificados ou endereços de rede sensíveis.
- Registrar versões exatas, horário UTC, configuração e resultado.
- Gerar SHA-256 de cada arquivo coletado.
- Falha de coleta ou lacuna de log torna o requisito “não comprovado”, não “aprovado por observação”.

As pastas `evidence/` são ignoradas pelo Git e devem ser anexadas ao artefato interno de aceitação.
