# Finance — core local

Backend/core doméstico em .NET 10, EF Core 10 e SQLite. A solution não contém frontend, API, autenticação ou executável de interface.

## Projetos

- `src/Finance.Core`: entidades e tipos do domínio.
- `src/Finance.Infrastructure`: mapeamento EF Core/SQLite e operações financeiras (`FinanceService`).
- `tests/Finance.Tests`: testes xUnit das regras com EF InMemory e teste de integração SQLite em memória.

## Compilar e testar
Com o SDK .NET 10:

```sh
dotnet restore Finance.sln
dotnet test Finance.sln
```

## Inicializar persistência SQLite

O provedor usa `winsqlite3.dll` já disponível no Windows. Em sistemas Linux/macOS, exige a biblioteca SQLite de sistema e o nome de ligação `sqlite3` (em Fedora, pacote `sqlite-devel`). Antes de abrir conexões, selecione o provedor nativo:

```csharp
SqliteProviderConfiguration.Initialize();
var options = new DbContextOptionsBuilder<FinanceDbContext>()
    .UseSqlite("Data Source=finance.db")
    .Options;
await using var db = new FinanceDbContext(options);
await db.Database.EnsureCreatedAsync();
```

O projeto usa `Microsoft.EntityFrameworkCore.Sqlite.Core` e provedores nativos do sistema para não distribuir a biblioteca SQLite empacotada atualmente sinalizada como vulnerável. A suite testa regras com EF InMemory e inclui integração com SQLite em memória.

`FinanceService` cria categorias dinamicamente e colapsa espaços/compara caixa invariavelmente; a unicidade também é protegida por índice no banco. Despesas pendentes têm `Amount = null`, não entram no total conhecido e aparecem em `PendingExpenseCount`. Recorrências mensais são geradas no máximo uma vez por mês. Parcelas distribuem centavos restantes nas primeiras parcelas, preservando a soma do plano.
