# ADR 0001: Motor e linguagem: Godot 4.7.1 .NET, C# em .NET 10

- **Status:** Aceita
- **Data:** 2026-11-01

## Contexto
O jogo precisava de um motor 2D rápido de iterar, e os outros projetos da casa (Ecos da Fenda, Glitch Lanes) já usam Godot 4.7.1 .NET com C#. O SDK 10.0.302 está instalado.

## Decisão
- Godot 4.7.1 .NET (mono). Tudo em **C#**, `net10.0`; `global.json` fixa o SDK 10.0.302 com `rollForward: latestFeature`.
- `PrismaBlade.csproj` é o jogo; `src/Prisma.Sim` é a lógica; `tests/Prisma.Sim.Tests` são os testes xUnit.
- Sem GDScript.

## Consequências
- Uma linguagem só, e as receitas dos outros projetos servem aqui.
- Rodar o jogo pede o executável `_mono`; para testes sem janela, o `_console.exe` com `--headless`.
