import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Input } from '../components/ui/Input'
import { Button } from '../components/ui/Button'

const highlights = ['Aberturas fortes e fracas', 'Diagnostico por cor', 'Dica geral com IA']

export function HomePage() {
  const [nickname, setNickname] = useState('')
  const [error, setError] = useState('')
  const navigate = useNavigate()

  function handleSubmit(event) {
    event.preventDefault()

    const trimmed = nickname.trim()
    if (!trimmed) {
      setError('Informe um nickname para abrir a tela de analise.')
      return
    }

    setError('')
    navigate(`/analysis/${encodeURIComponent(trimmed)}`)
  }

  return (
    <div className="app-shell">
      <div className="app-orb app-orb-one" />
      <div className="app-orb app-orb-two" />

      <main className="hero-layout hero-layout--landing">
        <section className="hero-copy">
          <div className="eyebrow">ChessAnalise</div>
          <h1>Seu raio-x de decisao no tabuleiro, em uma tela de analise dedicada.</h1>
          <p className="hero-description">
            Digite seu nickname do Chess.com e abra uma pagina com sinais reais de performance: abertura, cor, precisao, peca sensivel e fase critica.
          </p>

          <form className="search-form" onSubmit={handleSubmit}>
            <label htmlFor="nickname">Nickname do Chess.com</label>
            <div className="input-row">
              <Input
                id="nickname"
                type="text"
                value={nickname}
                onChange={(event) => setNickname(event.target.value)}
                placeholder="Ex: MagnusCarlsen"
                className="grow"
              />
              <Button type="submit">Ir para analise</Button>
            </div>
          </form>

          {error && <p className="status-error">{error}</p>}

          <div className="highlight-list">
            {highlights.map((item) => (
              <span key={item} className="highlight-pill">
                {item}
              </span>
            ))}
          </div>
        </section>
      </main>
    </div>
  )
}
