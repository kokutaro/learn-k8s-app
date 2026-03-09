import { Navigate, createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/')({
  component: IndexRedirect,
})

function IndexRedirect() {
  return <Navigate to="/facilities" search={{ sort: 'name', limit: 20 }} />
}
