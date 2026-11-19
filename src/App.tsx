import { Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Home from './pages/Home'
import Faq from './pages/Faq'
import Registry from './pages/Registry'
import Rsvp from './pages/Rsvp'
import Travel from './pages/Travel'
import NotFound from './pages/NotFound'

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="faq" element={<Faq />} />
        <Route path="registry" element={<Registry />} />
        <Route path="rsvp" element={<Rsvp />} />
        <Route path="travel" element={<Travel />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  )
}
