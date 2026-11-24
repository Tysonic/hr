import { Alert, Button, Center, Loader, Stack, Text } from '@mantine/core'
import { useEffect, useRef, type ReactNode } from 'react'
import { useAuth } from 'react-oidc-context'
import { isSigningOut, signIn, signOut } from '../lib/auth'
import { useMe } from '../lib/permissions'

/** Shows the app only to a signed-in user with at least one TyHR role; otherwise sends them to Keycloak. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const redirecting = useRef(false)
  const signedIn = auth.isAuthenticated && !auth.isLoading

  useEffect(() => {
    // Signing out removes the local user first; starting a sign-in then would cancel the Keycloak logout.
    if (isSigningOut()) return
    if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator && !auth.error && !redirecting.current) {
      redirecting.current = true
      void signIn()
    }
  }, [auth.isLoading, auth.isAuthenticated, auth.activeNavigator, auth.error])

  if (auth.error) {
    return (
      <Centered>
        <Alert color="red" title="Sign-in failed" maw={460}>
          <Stack gap="sm">
            <Text size="sm">{auth.error.message}</Text>
            <Text size="sm" c="dimmed">Check that Keycloak is running (deploy/keycloak/setup.sh).</Text>
            <Button size="xs" w="fit-content" onClick={() => void signIn()}>Try again</Button>
          </Stack>
        </Alert>
      </Centered>
    )
  }

  if (!signedIn) return <Centered><Loader /></Centered>
  return <HasRole>{children}</HasRole>
}

function HasRole({ children }: { children: ReactNode }) {
  const me = useMe()
  if (me.isPending) return <Centered><Loader /></Centered>
  if (me.isError || me.data.roles.length === 0) {
    return (
      <Centered>
        <Alert color="orange" title="No access to TyHR" maw={460}>
          <Stack gap="sm">
            <Text size="sm">
              {me.isError ? me.error.message : 'Your account has no TyHR role yet. Ask an administrator to give you one.'}
            </Text>
            <Button size="xs" variant="default" w="fit-content" onClick={() => void signOut()}>Sign out</Button>
          </Stack>
        </Alert>
      </Centered>
    )
  }
  return children
}

function Centered({ children }: { children: ReactNode }) {
  return <Center h="100vh" p="md">{children}</Center>
}
