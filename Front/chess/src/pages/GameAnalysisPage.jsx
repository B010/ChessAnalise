import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/Card'
import { Button } from '../components/ui/Button'

function pct(value) {
  return typeof value === 'number' ? `${value.toFixed(1)}%` : '-'
}

function dt(value) {
  return typeof value === 'number'
    ? new Date(value * 1000).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
    : '-'
}

export function GameAnalysisPage() {
  const { nickname = '' } = useParams()
  const [searchParams] = useSearchParams()
  const gameUrl = searchParams.get('gameUrl') || ''

  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [result, setResult] = useState(null)

  useEffect(() => {
    const controller = new AbortController()

    async function run() {
      if (!gameUrl) {
        setError('URL da partida nao informada.')
        setLoading(false)
        return
      }

      setLoading(true)
      setError('')

      try {
        const response = await fetch(
          `/api/players/${encodeURIComponent(nickname)}/game-analysis?gameUrl=${encodeURIComponent(gameUrl)}`,
          { signal: controller.signal },
        )

        const data = await response.json().catch(() => null)
        if (!response.ok) {
          throw new Error(data?.message || 'Nao foi possivel analisar essa partida.')
        }

        setResult(data)
      } catch (requestError) {
        if (requestError?.name !== 'AbortError') {
          setResult(null)
          setError(requestError.message || 'Erro inesperado ao analisar partida.')
        }
      } finally {
        if (!controller.signal.aborted) {
          setLoading(false)
        }
      }
    }

    run()
    return () => controller.abort()
  }, [nickname, gameUrl])

  const title = useMemo(() => {
    if (!result?.overview) {
      return `Partida de @${nickname}`
    }

    const o = result.overview
    return `${o.result} vs ${o.opponent} (${o.timeClass})`
  }, [nickname, result])

  return (
    <div className="app-shell">
      <div className="app-orb app-orb-one" />
      <div className="app-orb app-orb-two" />

      <main className="analysis-layout">
        <section className="analysis-topbar">
          <div>
            <div className="eyebrow">Analise da partida</div>
            <h1 className="analysis-title">{title}</h1>
            <p className="hero-description">Leitura ponto a ponto da partida selecionada, com destaques, erros e ajustes praticos.</p>
          </div>

          <Link to={`/analysis/${nickname}`}>
            <Button variant="secondary">Voltar ao painel</Button>
          </Link>
        </section>

        {loading && <p className="status-muted">Analisando partida...</p>}
        {error && <p className="status-error">{error}</p>}

        {result && !loading && (
          <section className="analysis-grid">
            <Card>
              <CardHeader>
                <CardTitle>Resumo da partida</CardTitle>
                <CardDescription>Contexto principal do jogo selecionado.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="stats-grid">
                  <div>
                    <span>Resultado</span>
                    <strong>{result.overview?.result || '-'}</strong>
                  </div>
                  <div>
                    <span>Cor</span>
                    <strong>{result.overview?.color || '-'}</strong>
                  </div>
                  <div>
                    <span>Abertura</span>
                    <strong>{result.overview?.openingFamily || '-'}</strong>
                  </div>
                  <div>
                    <span>Precisao</span>
                    <strong>{pct(result.overview?.accuracy)}</strong>
                  </div>
                </div>

                <div className="score-band">
                  <div>
                    <span>Data</span>
                    <strong>{dt(result.overview?.playedAtUnix)}</strong>
                  </div>
                  <div>
                    <span>Oponente</span>
                    <strong>{result.overview?.opponent || '-'}</strong>
                  </div>
                  <div>
                    <span>Lances</span>
                    <strong>{result.overview?.fullMoves || '-'}</strong>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Pontos fortes</CardTitle>
                <CardDescription>Elementos que funcionaram bem nessa partida.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list analysis-list--success">
                  {(result.strengths ?? []).map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                  {!result.strengths?.length && <li>Sem destaques claros.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Erros mais relevantes</CardTitle>
                <CardDescription>Onde a partida saiu do controle.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.mistakes ?? []).map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                  {!result.mistakes?.length && <li>Nenhum erro relevante detectado.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Ajustes para o proximo jogo</CardTitle>
                <CardDescription>Checklist pratico de melhoria imediata.</CardDescription>
              </CardHeader>
              <CardContent>
                <ul className="analysis-list">
                  {(result.improvements ?? []).map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                  {!result.improvements?.length && <li>Sem ajustes sugeridos.</li>}
                </ul>
              </CardContent>
            </Card>

            <Card className="analysis-ai-card">
              <CardHeader>
                <CardTitle>Comentario IA da partida</CardTitle>
                <CardDescription>Sintese orientada para proxima performance.</CardDescription>
              </CardHeader>
              <CardContent>
                <p className="ai-tip">{result.aiComment || 'Sem comentario disponivel.'}</p>
                {result.overview?.gameUrl && (
                  <a className="analysis-link" href={result.overview.gameUrl} target="_blank" rel="noreferrer">
                    Abrir partida no Chess.com
                  </a>
                )}
              </CardContent>
            </Card>
          </section>
        )}
      </main>
    </div>
  )
}
