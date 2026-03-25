/**
 * Centralized API client with auth header injection, logging, and error handling.
 * Automatically redirects to /login on 401 responses.
 */

import { logger } from './logger.js'

const BASE_URL = import.meta.env.VITE_API_BASE_URL || ''

/**
 * Custom API Error class for better error handling
 */
export class ApiError extends Error {
  constructor(message, status, data) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.data = data
  }

  get isAuthError() {
    return this.status === 401 || this.status === 403
  }

  get isValidationError() {
    return this.status === 400 || this.status === 422
  }

  get isServerError() {
    return this.status >= 500
  }

  get userMessage() {
    if (this.data?.message) return this.data.message
    if (this.data?.error) return this.data.error
    if (this.data?.errors) {
      const errors = this.data.errors
      if (Array.isArray(errors)) return errors.join(', ')
      if (typeof errors === 'object') return Object.values(errors).flat().join(', ')
    }
    return this.message
  }
}

/**
 * Core fetch wrapper with auth header injection, logging, and error handling.
 */
async function apiFetch(path, options = {}) {
  const headers = { ...(options.headers || {}) }

  if (!(options.body instanceof FormData) && options.body && typeof options.body === 'string') {
    headers['Content-Type'] = 'application/json'
  }

  // Fallback: Add Authorization header if cookie auth might fail (CORS scenarios)
  const token = localStorage.getItem('ged_access_token')
  if (token && !headers['Authorization']) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const method = options.method || 'GET'
  const url = `${BASE_URL}${path}`

  logger.request(method, url)

  let response
  try {
    response = await fetch(url, {
      ...options,
      headers,
      credentials: 'include'
    })
  } catch (networkError) {
    logger.error('api', `Network error on ${method} ${path}: ${networkError.message}`)
    throw new ApiError(
      'Impossible de se connecter au serveur',
      null,
      { code: networkError.code }
    )
  }

  logger.response(method, url, response.status)

  if (response.status === 401) {
    logger.error('api', `401 Unauthorized on ${method} ${path} — redirecting to login`)
    localStorage.removeItem('ged_user')
    window.location.href = '/login'
    throw new ApiError('Session expirée. Redirection vers la page de connexion.', 401, null)
  }

  if (response.status === 403) {
    logger.error('api', `403 Forbidden on ${method} ${path}`)
    throw new ApiError('Accès refusé. Vous n\'avez pas les permissions nécessaires.', 403, null)
  }

  if (!response.ok) {
    let errorData = null
    try {
      errorData = await response.json()
    } catch {
      // Ignore JSON parse errors
    }

    const errorMessage = errorData?.message
      || errorData?.error
      || `Erreur ${response.status}`
    
    logger.error('api', `${response.status} error on ${method} ${path}: ${errorMessage}`)
    throw new ApiError(errorMessage, response.status, errorData)
  }

  if (response.status === 204) {
    return null
  }

  try {
    return await response.json()
  } catch {
    return null
  }
}

/**
 * Wrapped API call with error handling
 * Returns { data, error } instead of throwing
 */
async function safeApiFetch(path, options = {}) {
  try {
    const data = await apiFetch(path, options)
    return { data, error: null }
  } catch (err) {
    return { data: null, error: err instanceof ApiError ? err : new ApiError(err.message, null, null) }
  }
}

// ── Auth ──────────────────────────────────────────────────────────────────────

export const auth = {
  login: (username, password) =>
    apiFetch('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password })
    }),

  me: () => apiFetch('/api/auth/me'),

  getUsers: () => apiFetch('/api/auth/users'),

  createUser: (data) =>
    apiFetch('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify(data)
    }),

  updateUser: (id, data) =>
    apiFetch(`/api/auth/users/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data)
    }),

  deactivateUser: (id) =>
    apiFetch(`/api/auth/users/${id}`, { method: 'DELETE' }),

  logout: async () => {
    await apiFetch('/api/auth/logout', { method: 'POST' })
    localStorage.removeItem('ged_user')
    localStorage.removeItem('ged_access_token')
    window.location.href = '/login'
  },

  getUser() {
    try {
      return JSON.parse(localStorage.getItem('ged_user') || 'null')
    } catch {
      return null
    }
  },

  isAdmin() {
    return this.getUser()?.role === 'Admin'
  }
}

// ── Groups ─────────────────────────────────────────────────────────────────

export const groups = {
  list:             ()              => apiFetch('/api/groups'),
  create:           (data)          => apiFetch('/api/groups', { method: 'POST', body: JSON.stringify(data) }),
  get:              (id)            => apiFetch(`/api/groups/${id}`),
  update:           (id, data)      => apiFetch(`/api/groups/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  delete:           (id)            => apiFetch(`/api/groups/${id}`, { method: 'DELETE' }),
  addDocuments:     (id, data)      => apiFetch(`/api/groups/${id}/documents`, { method: 'POST', body: JSON.stringify(data) }),
  removeDocument:   (id, memberId)  => apiFetch(`/api/groups/${id}/documents/${memberId}`, { method: 'DELETE' }),
  assign:           (id, data)      => apiFetch(`/api/groups/${id}/assign`, { method: 'POST', body: JSON.stringify(data) }),
  revokeAssignment: (id, assignId)  => apiFetch(`/api/groups/${id}/assignments/${assignId}`, { method: 'DELETE' }),
  accessSummary:    ()              => apiFetch('/api/groups/users/access-summary'),
  userAccess:       (userId)        => apiFetch(`/api/groups/users/${userId}/access`),
  changeUserRole:   (userId, role)  => apiFetch(`/api/groups/users/${userId}/role`, { method: 'PATCH', body: JSON.stringify({ role }) }),
}

// ── Documents ─────────────────────────────────────────────────────────────────

export const documents = {
  upload: (formData) => {
    const idempotencyKey = crypto.randomUUID()
    return apiFetch('/api/documents/upload', {
      method: 'POST',
      body: formData,
      headers: { 'Idempotency-Key': idempotencyKey }
    })
  },

  list: (params = {}) => {
    const qs = new URLSearchParams(params).toString()
    return apiFetch(`/api/documents${qs ? '?' + qs : ''}`)
  },

  get: (id) => apiFetch(`/api/documents/${id}`),

  delete: (id) => apiFetch(`/api/documents/${id}`, { method: 'DELETE' }),

  update: (id, data) =>
    apiFetch(`/api/documents/${id}`, {
      method: 'PUT',
      body: JSON.stringify(data)
    }),

  downloadUrl: (id) => `${BASE_URL}/api/documents/${id}/download`,
  viewUrl:     (id) => `${BASE_URL}/api/documents/${id}/view`,

  ocrStatus: (id) => apiFetch(`/api/documents/${id}/ocr-status`),

  metadataSuggestions: (id) => apiFetch(`/api/documents/${id}/metadata-suggestions`)
}

// ── Search ────────────────────────────────────────────────────────────────────

export const search = {
  query: (params) =>
    apiFetch('/api/search/query', {
      method: 'POST',
      body: JSON.stringify(params)
    }),

  suggestions: (q) =>
    apiFetch(`/api/search/suggestions?q=${encodeURIComponent(q)}`)
}

// ── RAG ──────────────────────────────────────────────────────────────────────

export const rag = {
  ask: (requestBody) =>
    apiFetch('/api/rag/ask', {
      method: 'POST',
      body: JSON.stringify(requestBody)
    }),

  health: () => apiFetch('/api/rag/health')
}

// Export safe versions for components that want to handle errors themselves
export const safeApi = {
  auth: {
    login: (u, p) => safeApiFetch('/api/auth/login', { method: 'POST', body: JSON.stringify({ username: u, password: p }) }),
    me: () => safeApiFetch('/api/auth/me'),
    getUsers: () => safeApiFetch('/api/auth/users'),
    createUser: (data) => safeApiFetch('/api/auth/register', { method: 'POST', body: JSON.stringify(data) }),
    updateUser: (id, data) => safeApiFetch(`/api/auth/users/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deactivateUser: (id) => safeApiFetch(`/api/auth/users/${id}`, { method: 'DELETE' }),
  },
  documents: {
    list: (params) => {
      const qs = new URLSearchParams(params).toString()
      return safeApiFetch(`/api/documents${qs ? '?' + qs : ''}`)
    },
    get: (id) => safeApiFetch(`/api/documents/${id}`),
    delete: (id) => safeApiFetch(`/api/documents/${id}`, { method: 'DELETE' }),
    update: (id, data) => safeApiFetch(`/api/documents/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    ocrStatus: (id) => safeApiFetch(`/api/documents/${id}/ocr-status`),
  },
  search: {
    query: (params) => safeApiFetch('/api/search/query', { method: 'POST', body: JSON.stringify(params) }),
    suggestions: (q) => safeApiFetch(`/api/search/suggestions?q=${encodeURIComponent(q)}`),
  },
  rag: {
    ask: (body) => safeApiFetch('/api/rag/ask', { method: 'POST', body: JSON.stringify(body) }),
  },
  groups: {
    list: () => safeApiFetch('/api/groups'),
    create: (data) => safeApiFetch('/api/groups', { method: 'POST', body: JSON.stringify(data) }),
    delete: (id) => safeApiFetch(`/api/groups/${id}`, { method: 'DELETE' }),
  }
}

export default { auth, documents, search, rag, groups, safeApi, ApiError }
