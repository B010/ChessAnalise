import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { buildApiUrl } from '../lib/api'

const TIME_OPTIONS = [
  { value: 'all', label: 'Todos' },
  { value: 'rapid', label: 'Rapid' },
  { value: 'blitz', label: 'Blitz' },
  { value: 'bullet', label: 'Bullet' },
  { value: 'daily', label: 'Daily' },
]

const QUICK_AI_QUESTIONS = [
  'Qual meu maior erro recorrente nas ultimas partidas?',
  'Que plano pratico devo seguir na proxima semana para subir meu score?',
  'Como ajustar meu repertorio de abertura com base nos meus ultimos jogos?',
  'Qual checklist devo usar para reduzir blunders no meio-jogo?',
]

function pct(value) {
  return typeof value === 'number' ? `${value.toFixed(1)}%` : '-'
}

function rating(value) {
  return typeof value === 'number' ? value : '-'
}

function dt(value) {
  return typeof value === 'number'
    ? new Date(value * 1000).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
    : '-'
}

function directionClass(value) {
  if (typeof value !== 'number' || value === 0) {
    return 'delta-neutral'
  }

  return value > 0 ? 'delta-positive' : 'delta-negative'
}

function buildOpeningTrainingLinks(openingFamily) {
  const query = encodeURIComponent(openingFamily)
  return [
    {
      label: 'Lichess Studies',
      url: `https://lichess.org/study/search?q=${query}`,
    },
    {
      label: 'Chessable',
      url: `https://www.chessable.com/courses/search/?q=${query}`,
    },
    {
      label: 'YouTube',
      url: `https://www.youtube.com/results?search_query=${query}+opening+chess`,
    },
  ]
}

export function AnalysisPage() {
  const { nickname = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [result, setResult] = useState(null)
  const [timeClass, setTimeClass] = useState('all')
  const [aiQuestion, setAiQuestion] = useState('')
  const [aiAnswer, setAiAnswer] = useState('')
  const [aiAskedQuestion, setAiAskedQuestion] = useState('')
  const [aiLoading, setAiLoading] = useState(false)
  const [aiError, setAiError] = useState('')

  useEffect(() => {
    const controller = new AbortController()

    async function run() {
      setLoading(true)
      setError('')

      try {
        const query = timeClass === 'all' ? '' : `?timeClass=${encodeURIComponent(timeClass)}`
        const response = await fetch(buildApiUrl(`/api/players/${encodeURIComponent(nickname)}/analysis${query}`), {
          signal: controller.signal,
        })

        const data = await response.json().catch(() => null)

        if (!response.ok) {
          throw new Error(data?.message || 'Nao foi possivel gerar a analise.')
        }

        setResult(data)
      } catch (requestError) {
        if (requestError?.name !== 'AbortError') {
          setResult(null)
          setError(requestError.message || 'Erro inesperado ao gerar analise.')
        }
      } finally {
        if (!controller.signal.aborted) {
          setLoading(false)
        }
      }
    }

    run()
    return () => controller.abort()
  }, [nickname, timeClass])

  const headerText = useMemo(() => {
    if (result?.player?.name) {
      return `${result.player.name} (@${result.player.username})`
    }

    return `@${nickname}`
  }, [nickname, result])

  const profileUsername = result?.player?.username || nickname
  const monthComparison = result?.monthComparison

  async function askAi(questionText) {
    const trimmedQuestion = questionText.trim()
    if (!trimmedQuestion) {
      setAiError('Digite uma pergunta para a IA.')
      return
    }

    setAiLoading(true)
    setAiError('')

    try {
      const response = await fetch(buildApiUrl(`/api/players/${encodeURIComponent(profileUsername)}/analysis/ask`), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          question: trimmedQuestion,
          timeClass,
        }),
      })

      const data = await response.json().catch(() => null)
      if (!response.ok) {
        throw new Error(data?.message || 'Nao foi possivel responder sua pergunta agora.')
      }

      setAiAskedQuestion(data?.question || trimmedQuestion)
      setAiAnswer(data?.answer || 'Sem resposta no momento.')
    } catch (requestError) {
      setAiAnswer('')
      setAiAskedQuestion('')
      setAiError(requestError.message || 'Erro inesperado ao perguntar para IA.')
    } finally {
      setAiLoading(false)
    }
  }

  function handleAskSubmit(event) {
    event.preventDefault()
    askAi(aiQuestion)
  }

  return (
    <div className="app-shell">
      <div className="app-orb app-orb-one" />
      <div className="app-orb app-orb-two" />

      <main className="analysis-layout">
        <section className="analysis-topbar">
          <div>
            <div className="eyebrow">Analise completa</div>
            <h1 className="analysis-title">{headerText}</h1>
            <p className="hero-description">
              Painel com base nos ultimos 3 meses, agrupando aberturas por familia e mostrando tendencia semanal.
              {result?.sampleSize ? ` Janela atual: ${result.sampleSize} partidas.` : ''}
            </p>

            <div className="filter-tabs" role="tablist" aria-label="Filtro por ritmo">
              {TIME_OPTIONS.map((item) => (
                <button
                  key={item.value}
                  type="button"
                  className={`filter-tab ${timeClass === item.value ? 'is-active' : ''}`}
                  onClick={() => setTimeClass(item.value)}
                >
                  {item.label}
                </button>
              ))}
            </div>
          </div>

          <Link to="/">
            <Button variant="secondary">Voltar e trocar jogador</Button>
          </Link>
        </section>

        {loading && <p className="status-muted">Carregando analise...</p>}
        {error && <p className="status-error">{error}</p>}

        {result && !loading && (
          <section className="analysis-grid">
            <Card>
              <CardHeader>
                <CardTitle>Resumo de rating</CardTitle>
                <CardDescription>Visao geral das modalidades principais.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="stats-grid">
                  <div>
                    <span>Rapid</span>
                    <strong>{rating(result.profileStats?.rapidRating)}</strong>
                  </div>
                  <div>
                    <span>Blitz</span>
                    <strong>{rating(result.profileStats?.blitzRating)}</strong>
                  </div>
                  <div>
                    <span>Bullet</span>
                    <strong>{rating(result.profileStats?.bulletRating)}</strong>
                  </div>
                  <div>
                    <span>Precisao media</span>
                    <strong>{pct(result.accuracy?.overallAverage)}</strong>
                  </div>
                </div>

                <div className="score-band">
                  <div>
                    <span>Score geral</span>
                    <strong>{pct(result.overallScore?.value)}</strong>
                  </div>
                  <div>
                    <span>Nivel</span>
                    <strong>{result.overallScore?.level || '-'}</strong>
                  </div>
                  <div>
                    <span>Confianca da amostra</span>
                    <strong>{result.confidence?.sampleLabel || '-'}</strong>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Brancas vs Pretas</CardTitle>
                <CardDescription>Desempenho separado por cor.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="color-grid">
                  <div>
                    <h3>Brancas</h3>
                    <p>{result.byColor?.white?.wins ?? 0}W / {result.byColor?.white?.draws ?? 0}D / {result.byColor?.white?.losses ?? 0}L</p>
                    <strong>Win rate: {pct(result.byColor?.white?.winRate)}</strong>
                  </div>
                  <div>
                    <h3>Pretas</h3>
                    <p>{result.byColor?.black?.wins ?? 0}W / {result.byColor?.black?.draws ?? 0}D / {result.byColor?.black?.losses ?? 0}L</p>
                    <strong>Win rate: {pct(result.byColor?.black?.winRate)}</strong>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Onde esta acertando</CardTitle>
                <CardDescription>Seus principais sinais de acerto na janela recente.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list analysis-list--success">
                  {(result.successSummary?.highlights ?? []).map((item) => (
                    <li key={`success-${item}`}>
                      <span>{item}</span>
                    </li>
                  ))}
                  {!result.successSummary?.highlights?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Tendencia por semana</CardTitle>
                <CardDescription>Evolucao de pontuacao e precisao nas ultimas semanas.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.weeklyTrend ?? []).map((item) => (
                    <li key={`week-${item.weekStartUnix}`}>
                      <span>
                        Semana de {dt(item.weekStartUnix)}
                        <small>{item.games} jogos</small>
                      </span>
                      <strong>{pct(item.scoreRate)} | Acc {pct(item.averageAccuracy)}</strong>
                    </li>
                  ))}
                  {!result.weeklyTrend?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Melhores aberturas</CardTitle>
                <CardDescription>Ranking por familia de abertura.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.openings?.best ?? []).map((item) => (
                    <li key={`best-${item.name}`}>
                      <span>{item.name}</span>
                      <strong>{pct(item.scoreRate)} em {item.games} jogos</strong>
                    </li>
                  ))}
                  {!result.openings?.best?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Familias onde mais sofre</CardTitle>
                <CardDescription>Indice ponderado por volume de jogos.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.openings?.worst ?? []).map((item) => (
                    <li key={`worst-${item.name}`}>
                      <span>{item.name}</span>
                      <strong>Indice {pct(item.sufferingIndex)} | {item.losses}/{item.games}</strong>
                    </li>
                  ))}
                  {!result.openings?.worst?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Recomendacoes de abertura</CardTitle>
                <CardDescription>O que manter, revisar ou reduzir no repertorio.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.openingRecommendations ?? []).map((item) => (
                    <li key={`${item.openingFamily}-${item.action}`}>
                      <span>
                        {item.openingFamily}
                        <small>{item.reason}</small>
                      </span>
                      <strong>{item.action} ({item.confidence})</strong>
                    </li>
                  ))}
                  {!result.openingRecommendations?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Peca com risco maior</CardTitle>
                <CardDescription>Indicador estimado de decisoes ruins por tipo de peca.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.piecePressure ?? []).map((item) => (
                    <li key={`piece-${item.piece}`}>
                      <span>{item.piece}</span>
                      <strong>Indice: {pct(item.riskRate)}</strong>
                    </li>
                  ))}
                  {!result.piecePressure?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Fase mais critica</CardTitle>
                <CardDescription>Momento do jogo onde as derrotas mais aparecem.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.phasePressure ?? []).map((item) => (
                    <li key={`phase-${item.phase}`}>
                      <span>{item.phase}</span>
                      <strong>{item.losses} derrotas</strong>
                    </li>
                  ))}
                  {!result.phasePressure?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Desempenho por ritmo</CardTitle>
                <CardDescription>Comparativo entre rapid, blitz, bullet e daily.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.timeClassBreakdown ?? []).map((item) => (
                    <li key={`time-${item.timeClass}`}>
                      <span>{item.timeClass}</span>
                      <strong>{pct(item.scoreRate)} | {item.games} jogos</strong>
                    </li>
                  ))}
                  {!result.timeClassBreakdown?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Faixa de adversarios</CardTitle>
                <CardDescription>Resultado contra oponentes acima, parelhos ou abaixo.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.opponentRanges?.buckets ?? []).map((item) => (
                    <li key={`opp-${item.range}`}>
                      <span>{item.range}</span>
                      <strong>{pct(item.scoreRate)} em {item.games} jogos</strong>
                    </li>
                  ))}
                  {!result.opponentRanges?.buckets?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Comparativo mensal</CardTitle>
                <CardDescription>Leitura de progresso no ultimo mes contra o anterior.</CardDescription>
              </CardHeader>
              <CardContent>
                {monthComparison?.current ? (
                  <div className="month-compare-grid">
                    <div>
                      <span>Mes atual</span>
                      <strong>{monthComparison.current.month}</strong>
                    </div>
                    <div>
                      <span>Pontuacao</span>
                      <strong>{pct(monthComparison.current.scoreRate)}</strong>
                    </div>
                    <div>
                      <span>Delta score</span>
                      <strong className={directionClass(monthComparison.scoreRateDelta)}>{pct(monthComparison.scoreRateDelta)}</strong>
                    </div>
                    <div>
                      <span>Delta precisao</span>
                      <strong className={directionClass(monthComparison.accuracyDelta)}>{pct(monthComparison.accuracyDelta)}</strong>
                    </div>
                  </div>
                ) : (
                  <p className="status-muted">Sem comparativo mensal suficiente.</p>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Plano de treino em 7 dias</CardTitle>
                <CardDescription>Roteiro semanal orientado pelos seus dados.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.trainingPlan?.days ?? []).map((item) => (
                    <li key={item.day}>
                      <span>
                        {item.day} - {item.focus}
                        <small>{item.task}</small>
                      </span>
                    </li>
                  ))}
                  {!result.trainingPlan?.days?.length && <li>Sem plano no momento.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Historico dos ultimos 5 jogos</CardTitle>
                <CardDescription>Agora com acesso direto a analise da partida na nossa tela.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.recentGames ?? []).map((game) => (
                    <li key={`${game.playedAtUnix}-${game.opponent}`} className="recent-game-item">
                      <span>
                        <strong>{game.result}</strong> vs {game.opponent} ({game.color}, {game.timeClass})
                        <small>{game.openingFamily} - {game.fullMoves} lances - precisao {pct(game.accuracy)} - {dt(game.playedAtUnix)}</small>
                      </span>
                      {game.gameUrl ? (
                        <Link
                          className="analysis-link"
                          to={{
                            pathname: `/analysis/${profileUsername}/game`,
                            search: `?gameUrl=${encodeURIComponent(game.gameUrl)}`,
                          }}
                        >
                          Nossa analise
                        </Link>
                      ) : (
                        <strong>Sem link</strong>
                      )}
                    </li>
                  ))}
                  {!result.recentGames?.length && <li>Sem jogos recentes suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card className="analysis-ai-card">
              <CardHeader>
                <CardTitle>Pergunte para a IA</CardTitle>
                <CardDescription>A resposta ja considera seus dados recentes e o filtro de ritmo atual.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="ai-quick-questions">
                  {QUICK_AI_QUESTIONS.map((question) => (
                    <button
                      key={question}
                      type="button"
                      className="filter-tab"
                      onClick={() => {
                        setAiQuestion(question)
                        askAi(question)
                      }}
                      disabled={aiLoading}
                    >
                      {question}
                    </button>
                  ))}
                </div>

                <form className="ai-question-form" onSubmit={handleAskSubmit}>
                  <Input
                    value={aiQuestion}
                    onChange={(event) => setAiQuestion(event.target.value)}
                    placeholder="Ex: Como devo jogar a abertura quando estou de pretas no blitz?"
                    maxLength={420}
                    disabled={aiLoading}
                  />
                  <Button type="submit" disabled={aiLoading}>
                    {aiLoading ? 'Respondendo...' : 'Perguntar'}
                  </Button>
                </form>

                {aiError && <p className="status-error">{aiError}</p>}

                {aiAnswer && (
                  <div className="ai-answer-box">
                    <strong>Pergunta: {aiAskedQuestion}</strong>
                    <p>{aiAnswer}</p>
                  </div>
                )}
              </CardContent>
            </Card>

            <Card className="analysis-ai-card">
              <CardHeader>
                <CardTitle>Dicas por fase + IA</CardTitle>
                <CardDescription>Conselhos tematicos e comentario global para a proxima semana.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="theme-grid">
                  <div>
                    <span>Abertura</span>
                    <p>{result.themedTips?.opening || '-'}</p>
                  </div>
                  <div>
                    <span>Meio-jogo</span>
                    <p>{result.themedTips?.middlegame || '-'}</p>
                  </div>
                  <div>
                    <span>Final</span>
                    <p>{result.themedTips?.endgame || '-'}</p>
                  </div>
                  <div>
                    <span>Decisao</span>
                    <p>{result.themedTips?.decision || '-'}</p>
                  </div>
                </div>
                <div className="training-links-wrap">
                  <h3>Links para treinar aberturas recomendadas</h3>
                  <ul className="training-links-list">
                    {(result.openingRecommendations ?? [])
                      .filter((item) => item.action !== 'Evitar por enquanto')
                      .slice(0, 3)
                      .map((item) => (
                        <li key={`training-${item.openingFamily}`}>
                          <span>{item.openingFamily}</span>
                          <div className="training-links-inline">
                            {buildOpeningTrainingLinks(item.openingFamily).map((link) => (
                              <a
                                key={`${item.openingFamily}-${link.label}`}
                                className="analysis-link"
                                href={link.url}
                                target="_blank"
                                rel="noreferrer"
                              >
                                {link.label}
                              </a>
                            ))}
                          </div>
                        </li>
                      ))}
                    {!result.openingRecommendations?.length && <li>Sem recomendacoes de abertura no momento.</li>}
                  </ul>
                </div>
                <p className="ai-tip">{result.aiTip || 'Ainda sem dica de IA para este perfil.'}</p>
              </CardContent>
            </Card>
          </section>
        )}
      </main>
    </div>
  )
}
