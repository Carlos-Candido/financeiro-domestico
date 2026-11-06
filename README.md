# Finance

Aplicativo familiar de controle financeiro local para Linux e Windows, com Avalonia UI, .NET 10, EF Core e SQLite. Não há API ou servidor.

## Projetos

- `src/Finance.Core`: entidades e tipos compartilhados.
- `src/Finance.Infrastructure`: operações financeiras e persistência SQLite.
- `src/Finance.Avalonia`: interface multiplataforma para dashboard, salários da família, lançamentos, histórico, recorrências, configurações e parcelamentos.
- `src/Finance.Desktop`: interface WPF para execução nativa no Windows, com início, lançamentos mensais e planejamento de salários, despesas mensais e parcelamentos.
- `tests/Finance.Tests`: testes do serviço com SQLite em memória.

## Layout Avalonia

A interface Avalonia organiza a visão geral com o saldo em destaque, ações rápidas logo abaixo, totais mensais, pendências e lançamentos recentes. Histórico, salários, recorrências e configurações usam cabeçalhos e cartões compactos, com textos ajustáveis à largura disponível; as ações dos salários ficam junto ao resumo de cada pessoa.

Os seletores de mês mantêm as setas centralizadas horizontal e verticalmente em botões de 44 px.

## Dados locais

O banco fica em `%LOCALAPPDATA%\Finance\finance.db` no Windows. No Linux usa `$XDG_DATA_HOME/Finance/finance.db` quando definido; sem ele, usa `~/.local/share/Finance/finance.db`. O caminho Windows continua igual ao da versão WPF para preservar os dados existentes. A pasta publicada nunca contém dados financeiros. Faça cópia de segurança com o aplicativo fechado.

Na inicialização, a pasta `Finance`, o banco e os arquivos auxiliares existentes do SQLite recebem permissões privadas: `0700` para a pasta e `0600` para os arquivos no Linux; no Windows, proprietário atual e ACL protegida com acesso somente ao usuário atual e ao LocalSystem. Se não for possível aplicar a proteção, a inicialização falha sem abrir o banco. A pasta `Finance` e seus arquivos SQLite não podem ser links simbólicos ou pontos de nova análise. Use um diretório-base local sob controle da sua conta, não uma pasta compartilhada gravável por terceiros.

Essa proteção restringe outras contas locais; não criptografa o banco nem protege contra programas executados na mesma conta, administradores ou SYSTEM. O ajuste das permissões preserva o conteúdo existente e não altera o esquema; voltar para uma versão anterior não restaura permissões mais abertas nem elimina cópias já feitas.

O saldo e os totais mensais são calculados pelo `FinanceService`; somente lançamentos confirmados entram no saldo disponível. Os salários familiares reutilizam recorrências de receita: o ajuste de uma ocorrência altera somente aquele mês, enquanto o padrão é usado nos meses ainda não gerados. Recorrências variáveis ficam pendentes e o rateio de parcelas preserva os centavos.

Lançamentos gerados por recorrências não podem ser excluídos individualmente pela interface, pois seriam gerados novamente ao atualizar o mês. Edite o valor da ocorrência ou encerre a recorrência para os próximos meses; lançamentos manuais continuam com exclusão mediante confirmação.

No WPF, a navegação principal é **Início**, **Lançamentos**, **Planejamento** e **Configurações**. O Planejamento agrupa salários, recorrências e compras parceladas. Alterar ou encerrar uma recorrência afeta a regra para novas gerações; lançamentos já gerados são preservados. Ao abrir um mês, o app gera as ocorrências desse mês de forma idempotente.

A visão de parcelamentos separa registros anteriores, do mês e posteriores ao mês selecionado. “Parcelas futuras” é uma contagem por data, não uma indicação de parcelas pagas ou de dívida em aberto; o modelo atual não registra quitação por parcela.

## Compilar e executar

Instale o SDK .NET 10. Na raiz do repositório, restaure, teste e compile:

```sh
dotnet restore Finance.sln
dotnet test Finance.sln
dotnet build Finance.sln --configuration Release
```

Execute o app Avalonia em Linux ou Windows:

```sh
dotnet run --project src/Finance.Avalonia/Finance.Avalonia.csproj
```

Execute a interface WPF no Windows:

```powershell
dotnet run --project src/Finance.Desktop/Finance.Desktop.csproj
```

Ao abrir, os formulários WPF se ajustam à área útil do monitor da janela proprietária e à escala de exibição, mantendo a rolagem do conteúdo.

Ao adicionar o primeiro lançamento, se não houver pessoas cadastradas, o aplicativo pede o nome da pessoa responsável. Categorias novas são criadas ao salvar lançamentos.

## Publicar para Linux x64

```sh
dotnet publish src/Finance.Avalonia/Finance.Avalonia.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output ./publish/linux-x64
```

Execute `./publish/linux-x64/Finance.Avalonia`.

## Publicar para Windows x64

No PowerShell. Para Avalonia:

```powershell
dotnet publish src/Finance.Avalonia/Finance.Avalonia.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\publish\avalonia-win-x64
```

Execute `.\publish\avalonia-win-x64\Finance.Avalonia.exe` para a versão Avalonia.

Para o desktop WPF self-contained:

```powershell
dotnet publish src/Finance.Desktop/Finance.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\publish\win-x64
```

Distribua `publish\win-x64` e execute `Finance.Desktop.exe`; o computador não precisa ter o runtime .NET instalado. A pasta publicada não contém banco financeiro: o SQLite fica em `%LOCALAPPDATA%\Finance\finance.db`.

## Verificação

A suíte automatizada cobre saldos, valores pendentes e previstos, confirmação mensal, recorrências idempotentes e rateio de parcelas. WPF requer Windows para execução; Avalonia pode ser executado em Linux e Windows.
