import { UserManager, WebStorageStateStore } from 'oidc-client-ts'

// Sign-in is through the shared Tysoft Keycloak realm "tysoft-apps" (one sign-in for Tycount and TyHR), public client
// "tyhr-web", code flow with PKCE.
// Defaults suit local development; see .env.development and deploy/keycloak/setup.sh.
const authority = import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:8082/realms/tysoft-apps'
const clientId = import.meta.env.VITE_OIDC_CLIENT_ID ?? 'tyhr-web'

export const userManager = new UserManager({
  authority,
  client_id: clientId,
  redirect_uri: `${window.location.origin}/`,
  post_logout_redirect_uri: `${window.location.origin}/`,
  response_type: 'code',
  scope: 'openid profile email',
  // Tokens live for the browser tab only and are renewed with the refresh token before they expire.
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  automaticSilentRenew: true,
})

/** The current access token, or null when signed out or expired. */
export async function getAccessToken(): Promise<string | null> {
  const user = await userManager.getUser()
  return user && !user.expired ? user.access_token : null
}

/** Starts sign-in, coming back to the page the user was on. */
export function signIn() {
  return userManager.signinRedirect({ state: window.location.pathname + window.location.search })
}

let signingOut = false

/** True once sign-out has started, so nothing starts a sign-in that would cancel the Keycloak logout. */
export const isSigningOut = () => signingOut

export function signOut() {
  signingOut = true
  return userManager.signoutRedirect()
}
