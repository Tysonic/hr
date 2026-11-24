import { getAccessToken, isSigningOut, signIn } from '../lib/auth'

/** An error returned by the API, carrying the readable message from its problem details. */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

type Query = Record<string, string | number | boolean | null | undefined>

function buildUrl(path: string, query?: Query) {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  }
  const qs = params.toString()
  return `/api${path}${qs ? `?${qs}` : ''}`
}

async function request<T>(method: string, path: string, body?: unknown, query?: Query): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const isForm = body instanceof FormData
  if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json'
  const token = await getAccessToken()
  if (token) headers.Authorization = `Bearer ${token}`

  let response: Response
  try {
    response = await fetch(buildUrl(path, query), {
      method,
      headers,
      body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'Cannot reach the TyHR API. Is it running?')
  }

  // No valid token (session ended or renewal failed): sign in again and come back here. If a token was sent
  // and still refused, signing in again would loop, so report it instead (e.g. API and Keycloak misconfigured).
  if (response.status === 401 && !token && !isSigningOut()) {
    void signIn()
    throw new ApiError(401, 'Your session has ended. Signing you in again…')
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  const data = text ? JSON.parse(text) : undefined

  if (!response.ok) {
    const message =
      data?.detail ??
      (data?.errors ? Object.values(data.errors as Record<string, string[]>).flat().join(' ') : undefined) ??
      data?.title ??
      `Request failed (${response.status})`
    throw new ApiError(response.status, message)
  }
  return data as T
}

export const api = {
  get: <T>(path: string, query?: Query) => request<T>('GET', path, undefined, query),
  post: <T>(path: string, body?: unknown, query?: Query) => request<T>('POST', path, body ?? {}, query),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  delete: <T>(path: string) => request<T>('DELETE', path),
  /** multipart/form-data, e.g. file uploads; the browser sets the boundary. */
  upload: <T>(path: string, form: FormData, query?: Query) => request<T>('POST', path, form, query),
}

/** Fetches a protected file with the user's token and hands it to the browser as a download. */
export async function download(path: string, fileName: string, query?: Query) {
  const token = await getAccessToken()
  let response: Response
  try {
    response = await fetch(buildUrl(path, query), { headers: token ? { Authorization: `Bearer ${token}` } : {} })
  } catch {
    throw new ApiError(0, 'Cannot reach the TyHR API. Is it running?')
  }
  if (!response.ok) {
    const data = await response.json().catch(() => undefined)
    throw new ApiError(response.status, data?.detail ?? data?.title ?? `Download failed (${response.status})`)
  }
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}
