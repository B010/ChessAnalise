import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/Card'
import { Button } from '../components/ui/Button'

function pct(value) {
  return typeof value === 'number' ? `${value.toFixed(1)}%` : '-'
}

function rating(value) {
  return typeof value === 'number' ? value : '-'
}

export function AnalysisPage() {
  const { nickname = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [result, setResult] = useState(null)

  useEffect(() => {
    let alive = true

    async function run() {
      setLoading(true)
      setError('')

      try {
        const response = await fetch(`/api/players/${encodeURIComponent(nickname)}/analysis`)
        const data = await response.json().catch(() => null)

        if (!response.ok) {
          throw new Error(data?.message || 'Nao foi possivel gerar a analise.')
        }

        if (alive) {
          setResult(data)
        }
      } catch (requestError) {
        if (alive) {
          setResult(null)
          setError(requestError.message || 'Erro inesperado ao gerar analise.')
        }
      } finally {
        if (alive) {
          setLoading(false)
        }
      }
    }

    run()

    return () => {
      alive = false
    }
  }, [nickname])

  const headerText = useMemo(() => {
    if (result?.player?.name) {
      return `${result.player.name} (@${result.player.username})`
    }

    return `@${nickname}`
  }, [nickname, result])

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
              Painel com sinais de abertura, cor, precisao e risco de decisao.
              {result?.dataWindowMonths ? ` Base de ${result.dataWindowMonths} meses recentes (${result.sampleSize ?? 0} partidas).` : ''}
            </p>
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
                <CardTitle>Melhores aberturas</CardTitle>
                <CardDescription>Ranking por familia de abertura (linhas e variantes agrupadas).</CardDescription>
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
                <CardDescription>Indice ponderado por volume de jogos para evitar distorcao de amostra pequena.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.openings?.worst ?? []).map((item) => (
                    <li key={`worst-${item.name}`}>
                      <span>{item.name}</span>
                      <strong>
                        Indice {pct(item.sufferingIndex)} | Derrotas {item.losses}/{item.games}
                      </strong>
                    </li>
                  ))}
                  {!result.openings?.worst?.length && <li>Sem dados suficientes.</li>}
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
                <CardTitle>Fases onde mais pontua</CardTitle>
                <CardDescription>Aproveitamento por fase da partida.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.phasePerformance ?? []).map((item) => (
                    <li key={`phase-performance-${item.phase}`}>
                      <span>{item.phase}</span>
                      <strong>{pct(item.scoreRate)} em {item.games} jogos</strong>
                    </li>
                  ))}
                  {!result.phasePerformance?.length && <li>Sem dados suficientes.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card className="analysis-ai-card">
              <CardHeader>
                <CardTitle>Dica geral IA</CardTitle>
                <CardDescription>Sintese automatica com base no comportamento recente.</CardDescription>
              </CardHeader>
              <CardContent>
                <p className="ai-tip">{result.aiTip || 'Ainda sem dica de IA para este perfil.'}</p>
              </CardContent>
            </Card>
          </section>
        )}
      </main>
    </div>
  )
}
