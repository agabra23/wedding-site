import { Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Home from './pages/Home'
import Faq from './pages/Faq'
import Registry from './pages/Registry'
import Rsvp from './pages/Rsvp'
import Travel from './pages/Travel'
import NotFound from './pages/NotFound'
import Admin from './pages/Admin'
import EntryGate from './components/EntryGate'
import RequireAdmin from './components/RequireAdmin'
import { useSession } from './auth/session'

export default function App() {
  const { token } = useSession()
  if (!token) return <EntryGate />
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="faq" element={<Faq />} />
        <Route path="registry" element={<Registry />} />
        <Route path="rsvp" element={<Rsvp />} />
        <Route
          path="admin"
          element={
            <RequireAdmin>
              <Admin />
            </RequireAdmin>
          }
        />
        <Route path="travel" element={<Travel />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  )
}
