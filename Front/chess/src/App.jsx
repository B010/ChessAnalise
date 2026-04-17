import { useState } from 'react'
import { Input } from './components/ui/Input'
import { Button } from './components/ui/Button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from './components/ui/Card'

const highlights = ['Perfil público', 'Resumo rápido', 'Melhorias sugeridas']

export default function Home() {
  const [nickname, setNickname] = useState('')

  function handleSubmit(event) {
    event.preventDefault()
  }

  return (
    <div className="app-shell">
      <div className="app-orb app-orb-one" />
      <div className="app-orb app-orb-two" />

      <main className="hero-layout">
        <section className="hero-copy">
          <div className="eyebrow">ChessAnalise</div>
          <h1>Análise de perfil com visual mais forte e fluxo mais claro.</h1>
          <p className="hero-description">
            Digite seu nickname do Chess.com para começar a leitura do seu perfil e preparar os próximos insights.
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
              <Button type="submit">Analisar</Button>
            </div>
          </form>

          <div className="highlight-list">
            {highlights.map((item) => (
              <span key={item} className="highlight-pill">
                {item}
              </span>
            ))}
          </div>
        </section>

        <section className="hero-visual">
          <Card className="profile-card">
            <CardHeader>
              <CardTitle>Prévia do perfil</CardTitle>
              <CardDescription>Uma base pronta para receber os dados da API.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="profile-art">
                <img src="/horse.png" alt="Chess Knight" className="hero-image" />
              </div>

              <div className="profile-metrics">
                <div>
                  <strong>1.2k</strong>
                  <span>partidas</span>
                </div>
                <div>
                  <strong>67%</strong>
                  <span>vitórias</span>
                </div>
                <div>
                  <strong>Rapid</strong>
                  <span>modalidade</span>
                </div>
              </div>
            </CardContent>
          </Card>
        </section>
      </main>
    </div>
  )
}