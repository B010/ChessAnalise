import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Chess } from 'chess.js'
import { Chessground as createChessground } from '@lichess-org/chessground'
import '@lichess-org/chessground/assets/chessground.base.css'
import '@lichess-org/chessground/assets/chessground.brown.css'
import '@lichess-org/chessground/assets/chessground.cburnett.css'

function buildDests(game) {
  const dests = new Map()
  for (const move of game.moves({ verbose: true })) {
    const arr = dests.get(move.from) || []
    arr.push(move.to)
    dests.set(move.from, arr)
  }
  return dests
}

function lastMoveSquares(game) {
  const history = game.history({ verbose: true })
  const last = history.at(-1)
  return last ? [last.from, last.to] : undefined
}

function turnToColor(turn) {
  return turn === 'w' ? 'white' : 'black'
}

export default function ChessBoard({ sanMoves, orientation = 'white', compact = false }) {
  const gameRef = useRef(new Chess())
  const apiRef = useRef(null)
  const containerRef = useRef(null)
  const cpuTimerRef = useRef(null)

  const [fen, setFen] = useState(() => new Chess().fen())
  const [selectedSquare, setSelectedSquare] = useState('')
  const [lastMove, setLastMove] = useState(undefined)
  const [lastAction, setLastAction] = useState('Pronto para o primeiro lance.')

  const replayMoves = useMemo(() => sanMoves ?? [], [sanMoves])
  const isReplay = replayMoves.length > 0
  const boardGame = useMemo(() => new Chess(fen), [fen])
  const turnColor = turnToColor(boardGame.turn())
  const legalDests = useMemo(() => buildDests(boardGame), [boardGame])

  const makeRandomMove = useCallback(() => {
    const game = gameRef.current
    if (!game || game.isGameOver()) return
    const possible = game.moves({ verbose: true })
    if (possible.length === 0) return
    const mv = possible[Math.floor(Math.random() * possible.length)]
    game.move(mv)
    setFen(game.fen())
    setSelectedSquare('')
    setLastMove([mv.from, mv.to])
    setLastAction(`CPU moved ${mv.san}`)
  }, [])

  const handlePlayerMove = useCallback(
    (orig, dest) => {
      const game = gameRef.current
      if (!game) return
      const legal = game.moves({ square: orig, verbose: true }).find((m) => m.to === dest)
      if (!legal) return
      const mv = game.move({ from: orig, to: dest, promotion: 'q' })
      if (!mv) return
      setFen(game.fen())
      setSelectedSquare('')
      setLastMove([mv.from, mv.to])
      setLastAction(`player moved ${mv.san}`)
      if (!isReplay) {
        if (cpuTimerRef.current) clearTimeout(cpuTimerRef.current)
        cpuTimerRef.current = setTimeout(makeRandomMove, 350)
      }
    },
    [isReplay, makeRandomMove],
  )

  const handleSelect = useCallback((key) => {
    setSelectedSquare(key || '')
    setLastAction(key ? `selected ${key}` : 'selection cleared')
  }, [])

  useEffect(() => {
    const g = new Chess()
    for (const san of replayMoves) {
      const m = g.move(san)
      if (!m) break
    }
    gameRef.current = g
    setFen(g.fen())
    setSelectedSquare('')
    setLastMove(lastMoveSquares(g))
    setLastAction(isReplay ? 'Partida carregada' : 'Pronto para jogar')
    return () => {
      if (cpuTimerRef.current) clearTimeout(cpuTimerRef.current)
    }
  }, [replayMoves, isReplay])

  useEffect(() => {
    if (!containerRef.current) return

    apiRef.current = createChessground(containerRef.current, {
      fen,
      orientation,
      turnColor,
      selected: selectedSquare || undefined,
      lastMove,
      coordinates: !compact,
      coordinatesOnSquares: false,
      autoCastle: true,
      trustAllEvents: true,
      viewOnly: false,
      disableContextMenu: true,
      highlight: { lastMove: true, check: true },
      // Always pass a movable object to avoid chessground reading undefined
      movable: {
        color: turnColor,
        dests: legalDests,
        showDests: true,
        events: { after: handlePlayerMove },
        rookCastle: true,
        enabled: true
      },
      draggable: { enabled: true, showGhost: true, autoDistance: true },
      selectable: { enabled: true },
      events: { select: handleSelect, move: handlePlayerMove }
    })

    return () => {
      apiRef.current?.destroy()
      apiRef.current = null
    }
  }, [containerRef])

  useEffect(() => {
    if (!apiRef.current) return
    apiRef.current.set({
      fen,
      orientation,
      turnColor,
      selected: selectedSquare || undefined,
      lastMove,
      coordinates: !compact,
      coordinatesOnSquares: false,
      autoCastle: true,
      trustAllEvents: true,
      viewOnly: false,
      disableContextMenu: true,
      highlight: { lastMove: true, check: true },
      movable: { color: turnColor, dests: legalDests, showDests: true, events: { after: handlePlayerMove }, rookCastle: true, enabled: true },
      draggable: { enabled: true, showGhost: true, autoDistance: true },
      selectable: { enabled: true },
      events: { select: handleSelect, move: handlePlayerMove }
    })
  }, [compact, fen, isReplay, lastMove, legalDests, orientation, selectedSquare, turnColor, handlePlayerMove, handleSelect])

  return (
    <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start', width: '100%' }}>
      <div style={{ width: compact ? 320 : 'min(92vw,560px)', aspectRatio: '1/1' }} ref={containerRef} />
      {!compact && (
        <aside style={{ minWidth: 260 }}>
          <div style={{ marginBottom: 12, color: '#9ca3af' }}>{lastAction}</div>
          <div style={{ fontFamily: 'monospace', color: '#d4d4d8' }}>{fen}</div>
        </aside>
      )}
    </div>
  )
}
