import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { buildApiUrl } from '../lib/api'

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
  const [chatMessages, setChatMessages] = useState([])
  const [chatQuestion, setChatQuestion] = useState('')
  const [chatLoading, setChatLoading] = useState(false)
  const [chatError, setChatError] = useState('')

  const chatStorageKey = useMemo(() => {
    if (!nickname || !gameUrl) {
      return ''
    }

    return `game-chat:${nickname}:${encodeURIComponent(gameUrl)}`
  }, [nickname, gameUrl])

  useEffect(() => {
    if (!chatStorageKey) {
      setChatMessages([])
      return
    }

    try {
      const raw = window.localStorage.getItem(chatStorageKey)
      if (!raw) {
        setChatMessages([])
        return
      }

      const parsed = JSON.parse(raw)
      if (!Array.isArray(parsed)) {
        setChatMessages([])
        return
      }

      const validMessages = parsed
        .filter((item) => item && typeof item.content === 'string' && typeof item.role === 'string')
        .map((item) => ({
          role: item.role === 'assistant' ? 'assistant' : 'user',
          content: item.content,
          atUnix: typeof item.atUnix === 'number' ? item.atUnix : Math.floor(Date.now() / 1000),
        }))

      setChatMessages(validMessages)
    } catch {
      setChatMessages([])
    }
  }, [chatStorageKey])

  useEffect(() => {
    if (!chatStorageKey) {
      return
    }

    window.localStorage.setItem(chatStorageKey, JSON.stringify(chatMessages))
  }, [chatStorageKey, chatMessages])

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
          buildApiUrl(`/api/players/${encodeURIComponent(nickname)}/game-analysis?gameUrl=${encodeURIComponent(gameUrl)}`),
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

  async function askGameChat(questionText) {
    const trimmedQuestion = questionText.trim()
    if (!trimmedQuestion) {
      setChatError('Digite uma pergunta para o chat.')
      return
    }

    const userMessage = {
      role: 'user',
      content: trimmedQuestion,
      atUnix: Math.floor(Date.now() / 1000),
    }

    const historyForRequest = chatMessages.slice(-20).map((item) => ({
      role: item.role,
      content: item.content,
    }))

    setChatError('')
    setChatLoading(true)
    setChatQuestion('')
    setChatMessages((prev) => [...prev, userMessage])

    try {
      const response = await fetch(buildApiUrl(`/api/players/${encodeURIComponent(nickname)}/game-analysis/chat`), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          gameUrl,
          question: trimmedQuestion,
          history: historyForRequest,
        }),
      })

      const data = await response.json().catch(() => null)
      if (!response.ok) {
        throw new Error(data?.message || 'Nao foi possivel responder no chat agora.')
      }

      const assistantMessage = {
        role: 'assistant',
        content: data?.answer || 'Sem resposta no momento.',
        atUnix: Math.floor(Date.now() / 1000),
      }

      setChatMessages((prev) => [...prev, assistantMessage])
    } catch (requestError) {
      setChatError(requestError.message || 'Erro inesperado no chat da partida.')
    } finally {
      setChatLoading(false)
    }
  }

  function handleChatSubmit(event) {
    event.preventDefault()
    askGameChat(chatQuestion)
  }

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

            <Card className="analysis-ai-card">
              <CardHeader>
                <CardTitle>Chat com IA da partida</CardTitle>
                <CardDescription>Conversa continua com historico completo salvo por partida.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="game-chat-history">
                  {!chatMessages.length && (
                    <p className="status-muted">Sem mensagens ainda. Pergunte algo sobre essa partida.</p>
                  )}

                  {chatMessages.map((message, index) => (
                    <div
                      key={`${message.role}-${message.atUnix}-${index}`}
                      className={`game-chat-message ${message.role === 'assistant' ? 'is-assistant' : 'is-user'}`}
                    >
                      <strong>{message.role === 'assistant' ? 'Coach IA' : 'Voce'}</strong>
                      <p>{message.content}</p>
                    </div>
                  ))}
                </div>

                <form className="game-chat-form" onSubmit={handleChatSubmit}>
                  <Input
                    value={chatQuestion}
                    onChange={(event) => setChatQuestion(event.target.value)}
                    placeholder="Ex: onde exatamente essa partida saiu do controle?"
                    maxLength={700}
                    disabled={chatLoading}
                  />
                  <Button type="submit" disabled={chatLoading}>
                    {chatLoading ? 'Enviando...' : 'Enviar'}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    disabled={chatLoading || !chatMessages.length}
                    onClick={() => setChatMessages([])}
                  >
                    Limpar historico
                  </Button>
                </form>

                {chatError && <p className="status-error">{chatError}</p>}
              </CardContent>
            </Card>
          </section>
        )}
      </main>
    </div>
  )
}
