# Motor industrial básico

Pacote demonstrativo autoral do sistema de componentes instaláveis. Ele não contém
código executável nem ativos provenientes de outros simuladores.

Gere o arquivo instalável com:

```powershell
& .\scripts\New-ComponentPackage.ps1 `
  -SourceDirectory .\samples\component-packages\source\motor-basico `
  -OutputPath .\samples\component-packages\motor-basico.silabcomponent
```
