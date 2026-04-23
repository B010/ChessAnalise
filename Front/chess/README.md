# ChessAnalise

Analise inteligente de partidas do Chess.com com foco em insights praticos para evolucao no xadrez.

## O que este projeto faz

- Consulta perfil e estatisticas de um jogador no Chess.com.
- Gera analise aprofundada de partidas recentes com filtros por ritmo.
- Mostra tendencias, desempenho por fase da partida e por cor.
- Sugere plano de treino de 7 dias com dicas tematicas.
- Permite analise de uma partida especifica por URL.

## Stack

- Frontend: React + Vite + React Router
- Backend: ASP.NET Core Minimal API (.NET 9)
- Fonte de dados: Chess.com Public API

## Estrutura do workspace

```text
ChessAnalise/
|- Api/ApiChess       # Backend .NET
|- Front/chess        # Frontend React (este diretório)
|- Front/package.json # Scripts "atalho" para o front
```

## Pre-requisitos

- Node.js 20+
- npm 10+
- .NET SDK 9.0+

## Como rodar o projeto

### 1) Backend (API)

No diretório `Api/ApiChess`:

```bash
dotnet restore
dotnet run
```

API em desenvolvimento: `http://localhost:5267`

### 2) Frontend

Voce pode iniciar por dois caminhos:

No diretório `Front/chess`:

```bash
npm install
npm run dev
```

Ou no diretório `Front` (scripts de atalho):

```bash
npm install
npm run dev
```

Front em desenvolvimento: `http://localhost:5173`

## Variaveis de ambiente (frontend)

O frontend usa `VITE_API_BASE_URL` para definir a base da API.

- Em desenvolvimento local, voce pode deixar vazio para usar o proxy do Vite (`/api` -> `http://localhost:5267`).
- Em producao (Vercel), configure com a URL publica da API no Azure.

Exemplo:

```bash
VITE_API_BASE_URL=https://chessapi-h0bza8ftfcgvf9h2.canadacentral-01.azurewebsites.net
```

Arquivo modelo: `.env.example`

## Scripts uteis (frontend)

No `Front/chess`:

- `npm run dev` inicia o servidor de desenvolvimento
- `npm run build` gera build de producao
- `npm run preview` sobe preview da build
- `npm run lint` executa o ESLint

No `Front`:

- `npm run dev` encaminha para `chess`
- `npm run build` encaminha para `chess`
- `npm run lint` encaminha para `chess`

## Endpoints principais da API

- `GET /api/players/{username}` perfil rapido
- `GET /api/players/{username}/analysis` analise aprofundada (aceita `timeClass` opcional)
- `POST /api/players/{username}/analysis/ask` perguntas sobre a analise
- `GET /api/players/{username}/game-analysis` analise de uma partida por URL
- `POST /api/players/{username}/game-analysis/chat` chat sobre a partida analisada

## Observacoes

- A API usa CORS liberado para localhost/127.0.0.1 em desenvolvimento.
- Em caso de erro de build no backend com arquivo em uso, encerre instancias antigas da API e rode novamente.

## Proximos passos sugeridos

- Adicionar testes automatizados no backend.
- Documentar exemplos de request/response por endpoint.
- Configurar variaveis de ambiente para producao e CI.
