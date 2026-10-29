# ADR 0001: núcleo independente do motor 3D

## Decisão

O domínio, o relógio lógico, as tags, a persistência e os drivers não dependerão do Godot.

## Motivo

- permite testes headless rápidos;
- separa lógica determinística de física e renderização;
- reduz dependência tecnológica;
- facilita integração futura com outras interfaces.

## Consequência

A camada Godot atuará como adaptador de visualização, entrada e física. Ela não será a fonte exclusiva da verdade do processo.
