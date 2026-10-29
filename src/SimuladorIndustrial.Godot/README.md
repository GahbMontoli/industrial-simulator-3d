# Interface 3D Godot

Visualização autoral da célula de envase sobre um plano cartesiano tridimensional.

## Recursos

- plano `XZ` de `8 × 6 m`, grade de `0,25 m` e marcações de `1 m`;
- eixos cartesianos `X` vermelho, `Y` verde e `Z` azul;
- câmera orbital, pan, zoom, seleção de pivô e restauração com `Home`;
- esteira e travessas animadas;
- recipiente móvel com nível de líquido visível;
- sensor, feixe, LED, bloqueador e fluxo de envase animados;
- painel operacional com `Run`, `Pause` e `Reset`;
- núcleo determinístico e PLC demonstrativo executados em passo fixo.

## Executar

```powershell
Set-Location .\industrial-simulator-3d
& .\Start-Simulador3D.ps1
```

Use `& .\Start-Simulador3D.ps1 -Editor` para abrir o projeto no editor do Godot.

## Controles

- botão esquerdo + arrastar: órbita;
- botão direito ou meio + arrastar: pan;
- roda: zoom;
- duplo clique no piso: novo pivô;
- `Home`: vista isométrica inicial.
