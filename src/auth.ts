import { PublicClientApplication, type AccountInfo } from '@azure/msal-browser'

const clientId = import.meta.env.VITE_ADMIN_CLIENT_ID

export const msal = new PublicClientApplication({
  auth: {
    clientId,
    authority: 'https://login.microsoftonline.com/common',
    redirectUri: `${window.location.origin}/admin`,
  },
  cache: { cacheLocation: 'sessionStorage' },
})

const scopes = [`api://${clientId}/access_as_user`]

async function currentAccount(): Promise<AccountInfo | null> {
  await msal.initialize()
  // Completes a redirect sign-in if we just came back from Microsoft
  const redirected = await msal.handleRedirectPromise()
  return redirected?.account ?? msal.getAllAccounts()[0] ?? null
}

/** Calls an admin API path with a bearer token; redirects to Microsoft sign-in if needed. */
export async function adminFetch(path: string): Promise<Response | null> {
  const account = await currentAccount()
  if (!account) {
    await msal.loginRedirect({ scopes })
    return null // page navigates away
  }
  const { accessToken } = await msal.acquireTokenSilent({ scopes, account })
  return fetch(`${import.meta.env.VITE_API_URL}${path}`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
}
