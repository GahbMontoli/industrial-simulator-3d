# Matriz requisito → fonte → evidência

Baseline documental: 27 de outubro de 2026. A coluna “evidência no projeto” aponta para implementação ou teste autoral e deve ser atualizada quando o requisito mudar.

| ID | Requisito abstrato | Fonte permitida | Evidência no projeto | Estado da baseline |
|---|---|---|---|---|
| R-01 | Editor 3D com grade, seleção, mover, girar, duplicar e excluir. | Necessidade do usuário e padrões gerais de CAD/editor 3D. | `src/SimuladorIndustrial.Core/Editing/SceneEditor.cs`; `src/SimuladorIndustrial.Godot/SceneEditor3DController.cs`; `tests/SimuladorIndustrial.Core.Tests/SceneEditorTests.cs` | Implementado e validado visualmente. |
| R-02 | Câmera orbital e câmera livre. | Necessidade do usuário; documentação pública de interface apenas como requisito comportamental. | `src/SimuladorIndustrial.Godot/OrbitCameraController.cs` | Implementado. |
| R-03 | Catálogo autoral de componentes industriais do MVP. | Necessidade da célula de envase; observações O-003/O-005. | `src/SimuladorIndustrial.Godot/FillingCellView.cs`; `src/SimuladorIndustrial.Godot/SceneEditor3DController.cs`; `samples/celula-envase.scene.json` | Todos os componentes do MVP implementados com geometria autoral. |
| R-04 | Executar, pausar, resetar e avançar um passo lógico. | Necessidade de diagnóstico; observação O-006. | `src/SimuladorIndustrial.Core/Runtime/SimulationEngine.cs`; `src/SimuladorIndustrial.Godot/Main.cs` | Implementado. |
| R-05 | Tags `bool`, `int32` e `float32`. | Requisito próprio de interoperabilidade. | `src/SimuladorIndustrial.Core/Tags/TagValue.cs`; `TagRegistryTests.cs` | Implementado e testado. |
| R-06 | Direção explícita do ponto de vista do PLC. | Convenção definida pelo projeto. | `TagDefinition.cs`; `TagRegistry.cs`; `TagRegistryTests.cs` | Implementado e testado. |
| R-07 | UUID, nome, endereço, valor, qualidade, forçamento e falha auditáveis. | Requisito próprio de diagnóstico; documentação pública somente para capacidade geral. | `TagDefinition.cs`; `TagSnapshot.cs`; `EventJournal.cs` | Implementado no núcleo. |
| R-08 | Modbus TCP cliente e servidor selecionáveis; FC01/02/03/04/05/06/15/16. | Especificação pública Modbus Application Protocol. | `src/SimuladorIndustrial.Core/Drivers/Modbus/`; `tests/SimuladorIndustrial.Core.Tests/ModbusTcpLoopbackTests.cs` | Cliente, servidor, sincronização, qualidade e reconexão validados em loopback; OpenPLC real pendente por indisponibilidade do runtime. |
| R-09 | Monitor de tags e linha do tempo. | Necessidade própria de rastreabilidade; observação O-006. | `EventJournal.cs`; `EvidenceExporter.cs`; HUD em `Main.cs` | Implementado no protótipo 3D. |
| R-10 | Cena autoral de envase. | Necessidade do usuário; comportamento abstrato O-005. | `samples/celula-envase.scene.json`; `AnimatedFillingLineSystem.cs`; testes associados | Implementado e testado. |
| R-11 | Formato `scene.json` próprio, versionado e validado. | Decisão arquitetural própria. | `Persistence/SceneDocument.cs`; `SceneSerializer.cs`; `SceneMigrator.cs`; `SceneValidator.cs`; testes de serialização | Versão 1, migração da baseline legada e limites de segurança implementados. |
| R-12 | Testes automatizados e pacote Windows x86_64. | Critério de entrega próprio; documentação oficial Godot. | `tests/`; `src/SimuladorIndustrial.Godot/export_presets.cfg`; `scripts/Export-Windows.ps1`; `dist/` | Testes ativos e exportação reproduzível com templates oficiais verificados. |
| R-13 | Evidência JSON/CSV e hashes de entrega. | Requisito próprio de auditoria. | `Diagnostics/EvidenceExporter.cs`; `EvidenceExporterTests.cs`; `scripts/Export-Windows.ps1` | Implementado. |
| R-14 | Licenças e SBOM junto ao pacote. | Conformidade oficial Godot e política clean-room. | `THIRD_PARTY_NOTICES.md`; `sbom/simulador-industrial.spdx.json` | Implementado; revisar a cada release. |

## Regras de uso

1. Uma fonte proprietária observada pode originar apenas requisito comportamental abstrato.
2. Código e assets devem vir de autoria própria ou dependências declaradas em `THIRD_PARTY_NOTICES.md` e na SBOM.
3. Evidência estática REA nunca deve ser tratada como execução observada.
4. Mudanças de dependência, versão ou asset exigem atualizar a SBOM antes da release.
5. Funcionalidade não testada deve permanecer marcada como parcial ou pendente.
