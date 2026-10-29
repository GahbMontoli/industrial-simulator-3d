# Registro de observações clean-room

Data-base: 26 de outubro de 2026  
Última revisão documental: 27 de outubro de 2026

Este registro separa fatos observados e fontes públicas de decisões da implementação. Ele não autoriza copiar expressão protegida e não substitui revisão jurídica.

## Escopo da observação

- Artefato legitimamente instalado em `C:\Factory IO`: Factory I/O 2.5.5.
- Documentação pública consultada: páginas oficiais de funcionalidades, manual, interface, cenas, drivers, Web API e notas de versão.
- Objetivo permitido: extrair requisitos comportamentais abstratos para um produto independente.
- Objetivo excluído: reproduzir código, arquivos, cenas, esquema proprietário, identidade visual ou mecanismos de licença.

## Fatos observados

| ID | Observação | Tipo de evidência | Limite |
|---|---|---|---|
| O-001 | O executável local examinado se identifica como Factory I/O 2.5.5 e possui assinatura Authenticode da Real Games. | Metadados do artefato instalado. | Vale somente para a cópia local examinada. |
| O-002 | O pacote local usa Unity 2023.1.5f1 e contém assemblies .NET/Mono gerenciados. | Inventário estático do pacote. | Inventário estático não comprova execução de cada caminho. |
| O-003 | A documentação oficial publica suporte a cenas 3D, catálogo de peças, I/O digital/analógico, drivers, falhas e modo instrutor. | Documentação pública oficial. | Funcionalidades e versões podem mudar. |
| O-004 | A instalação local contém 21 cenas `.factoryio`; o levantamento registrou 787 instâncias, 574 tags e 67 tipos usados. | Inventário local de arquivos e metadados. | Não implica cobertura completa do produto nem licença para redistribuir as cenas. |
| O-005 | A cena local de referência de tanque demonstra comandos separados de enchimento e descarga, válvulas de entrada/saída e sinalização. | Observação comportamental e inventário de cena. | A cena proprietária não entra no projeto e não é convertida. |
| O-006 | A documentação pública descreve ações de editar/executar, pausa, reset, escala de tempo, câmeras e monitoramento de tags. | Manual público oficial. | Somente o comportamento geral é requisito; layout, textos e aparência não são reproduzidos. |
| O-007 | A documentação pública descreve Modbus TCP em papéis cliente e servidor. | Manual público de drivers. | A implementação própria deve seguir a especificação Modbus publicada. |

## Referências de evidência estática

Os identificadores abaixo apontam para registros REA produzidos durante a investigação. Eles documentam a origem dos requisitos, não são fonte de código para a implementação.

- identidade gerenciada: `ev_ba21ce27c62c5b5f64e2de053f553f6a481d9b67eeca3285d71664ed5cf0e63c`;
- inventário de membros: `ev_9966e29d2f843fac731f3ae95850a5dbf780a97803c87fbbb8cd7b5b6a5e1971`;
- recorte de drivers, cenas e Web API: `ev_457a518ac73e650ec5dde88f48f7cf67b7cfd8ecb38729040b63cf856d7081b9`.

Conclusões extraídas desses registros devem conservar as limitações originais: análise estática não demonstra execução, resultados negativos são limitados ao escopo pesquisado e a versão local não representa versões posteriores.

## Fontes públicas permitidas

- <https://factoryio.com/features/>
- <https://docs.factoryio.com/manual/>
- <https://docs.factoryio.com/manual/user-interface/>
- <https://docs.factoryio.com/manual/scenes/>
- <https://docs.factoryio.com/manual/drivers/>
- <https://docs.factoryio.com/manual/web-api/>
- <https://docs.factoryio.com/release-notes/>
- <https://www.modbus.org/modbus-specifications>
- <https://github.com/Autonomy-Logic/openplc-runtime>
- <https://docs.godotengine.org/en/stable/about/complying_with_licenses.html>

## Decisões próprias derivadas

- Formato de cena JSON próprio e incompatível com `.factoryio`.
- Identidade visual azul/ciano e organização de interface próprias.
- Geometria procedural autoral no MVP.
- Núcleo .NET separado do Godot.
- Direção das tags explicitamente definida do ponto de vista do PLC.
- Modbus implementado a partir da especificação pública, sem biblioteca ou código do produto observado.
- Porta de desenvolvimento 5020 para evitar requisito de privilégio da porta 502.

## Conteúdo que não foi transferido

Não foram incorporados DLLs, código decompilado, modelos, texturas, ícones, sons, cenas, textos, logotipos, marcas, arquivos de configuração ou mecanismos de licença do produto observado. Consulte também [PROIBICOES.md](PROIBICOES.md).

## Pendências jurídicas e comerciais

- Não há neste repositório registro de revisão do EULA efetivamente aceito na instalação observada.
- Não há parecer jurídico sobre lançamento público ou comercial.
- A licença do código autoral ainda precisa ser escolhida pelo titular.
- Nome e marca devem passar por busca no INPI antes de uso comercial.

Essas pendências não bloqueiam o protótipo interno, mas bloqueiam qualquer afirmação de liberação jurídica para distribuição comercial.
