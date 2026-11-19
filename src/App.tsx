import { Link, Route, Routes } from 'react-router-dom'
import Home from './pages/Home'
import Rsvp from './pages/Rsvp'
import Admin from './pages/Admin'

export default function App() {
  return (
    <div className="min-h-screen bg-stone-50 text-stone-800">
      <header className="border-b border-stone-200 bg-white">
        <nav className="mx-auto flex max-w-3xl items-center justify-between p-4">
          <Link to="/" className="font-serif text-xl">
            Our Wedding
          </Link>
          <Link to="/rsvp" className="text-sm underline">
            RSVP
          </Link>
        </nav>
      </header>
      <main className="mx-auto max-w-3xl p-4">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/rsvp" element={<Rsvp />} />
          <Route path="/admin" element={<Admin />} />
        </Routes>
      </main>
    </div>
  )
}
