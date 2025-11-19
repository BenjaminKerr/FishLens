import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'
import './index.css'
import Sidebar from '../components/Sidebar.jsx'
import { Home } from '../pages/Home.jsx'
import { Settings } from '../pages/Settings.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <Router>
      <Sidebar />
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/settings" element={<Settings />} />
      </Routes>
    </Router>
  </StrictMode>,
)
