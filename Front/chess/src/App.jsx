import { Navigate, Route, Routes } from 'react-router-dom'
import { AnalysisPage } from './pages/AnalysisPage'
import { HomePage } from './pages/HomePage'

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/analysis/:nickname" element={<AnalysisPage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}