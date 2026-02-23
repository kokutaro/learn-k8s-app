import { useQuery } from '@tanstack/react-query'
import './App.css'

type User = {
  id: string
  name: string
  email: string
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? (import.meta.env.DEV ? 'http://localhost:5134' : '')

async function fetchUsers(): Promise<User[]> {
  const response = await fetch(`${apiBaseUrl}/api/v1/users`)

  if (!response.ok) {
    throw new Error(`Failed to fetch users: ${response.status}`)
  }

  return await response.json()
}

function App() {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['users'],
    queryFn: fetchUsers,
  })

  if (isLoading) {
    return <main className="container">Loading users...</main>
  }

  if (isError) {
    return <main className="container">Error: {error.message}</main>
  }

  return (
    <main className="container">
      <h1>Users</h1>
      <table>
        <thead>
          <tr>
            <th>Id</th>
            <th>Name</th>
            <th>Email</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((user) => (
            <tr key={user.id}>
              <td>{user.id}</td>
              <td>{user.name}</td>
              <td>{user.email}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </main>
  )
}

export default App
