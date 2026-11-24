import '@mantine/core/styles.css'
import '@mantine/dates/styles.css'
import '@mantine/notifications/styles.css'

import { createTheme, MantineProvider } from '@mantine/core'
import { ModalsProvider } from '@mantine/modals'
import { Notifications } from '@mantine/notifications'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { lazy, StrictMode, Suspense } from 'react'
import { createRoot } from 'react-dom/client'
import type { User } from 'oidc-client-ts'
import { AuthProvider } from 'react-oidc-context'
import { RouterProvider } from 'react-router'
import { RequireAuth } from './components/RequireAuth'
import { userManager } from './lib/auth'
import { router } from './router'

// Back from Keycloak: drop ?code=… from the URL and return to the page the user started on.
function onSigninCallback(user: User | undefined) {
  const target = typeof user?.state === 'string' && user.state.startsWith('/') ? user.state : '/'
  void router.navigate(target, { replace: true })
}

const theme = createTheme({
  primaryColor: 'indigo',
  defaultRadius: 'md',
  fontFamily: 'Inter, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
})

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: 1, refetchOnWindowFocus: false },
  },
})

// The public careers page needs no sign-in, so it skips Keycloak and the staff app entirely.
const CareersApp = lazy(() => import('./features/careers/CareersApp').then((m) => ({ default: m.CareersApp })))
const isCareers = window.location.pathname.startsWith('/careers/')

createRoot(document.getElementById('root')!).render(isCareers ? (
  <StrictMode>
    <MantineProvider theme={theme} defaultColorScheme="auto">
      <QueryClientProvider client={queryClient}>
        <Suspense fallback={null}><CareersApp /></Suspense>
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>
) : 
  <StrictMode>
    <AuthProvider userManager={userManager} onSigninCallback={onSigninCallback}>
      <MantineProvider theme={theme} defaultColorScheme="auto">
        <QueryClientProvider client={queryClient}>
          <ModalsProvider>
            <Notifications position="bottom-right" />
            <RequireAuth>
              <RouterProvider router={router} />
            </RequireAuth>
          </ModalsProvider>
        </QueryClientProvider>
      </MantineProvider>
    </AuthProvider>
  </StrictMode>,
)
