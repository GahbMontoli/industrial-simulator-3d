# Requisitos funcionais limpos

## Origem permitida

- documentação pública de protocolos e ferramentas;
- comportamento observado em cópia legitimamente instalada;
- requisitos próprios da célula de envase;
- decisões arquiteturais originais.

## Requisitos iniciais

1. Executar lógica de processo em passo fixo configurável.
2. Manter estados parado, executando e pausado.
3. Resetar o processo de maneira explícita e auditável.
4. Representar tags booleanas, inteiras e reais.
5. Definir a direção sempre do ponto de vista do PLC.
6. Bloquear escrita ou publicação no sentido incorreto.
7. Bloquear colisões de endereço no mesmo espaço lógico.
8. Expor qualidade, falha e forçamento separadamente do valor bruto.
9. Persistir cenas em JSON próprio com versão de esquema.
10. Rejeitar cenas inválidas, versões futuras e referências inseguras.
11. Permitir futura conexão Modbus sem acoplar protocolo ao núcleo.
12. Registrar eventos com sequência monotônica.
