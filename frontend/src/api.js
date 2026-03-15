/**
 * Centralized API client.
 *
 * request, and redirects to /login on 401 responses.
 */

import { logger } from './logger.js'

const BASE_URL = import.meta.env.VITE_API_BASE_URL || ''

/**
 * Core fetch wrapper with auth header injection and logging.
 */
async function apiFetch(path, options = {}) {
  const headers = { ...(options.headers || {}) }

  // Don't set Content-Type for FormData — browser sets it with boundary
  if (!(options.body instanceof FormData) && options.body && typeof options.body === 'string') {
    headers['Content-Type'] = 'application/json'
  }

  const method = options.method || 'GET'
  const url = `${BASE_URL}${path}`

  logger.request(method, url)

  const response = await fetch(url, { 
    ...options, 
    headers,
    credentials: 'include' 
  })

  logger.response(method, url, response.status)

  // Auto-logout on 401
  if (response.status === 401) {
    logger.error('api', `401 Unauthorized on ${method} ${path} — redirecting to login`)
    localStorage.removeItem('ged_user')
    window.location.href = '/login'
    throw new Error('Unauthorized — redirecting to login')
  }

  return response
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
  localStorage.removeItem('ged_user')  // still store non-sensitive user info locally
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
// ── groups ─────────────────────────────────────────────────────────────────

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
  const idempotencyKey = crypto.randomUUID()   // one key per button click
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

// ── RAG ───────────────────────────────────────────────────────────────────────

export const rag = {
  ask: (requestBody) =>
    apiFetch('/api/rag/ask', {
      method: 'POST',
      body: JSON.stringify(requestBody)
    }),

  health: () => apiFetch('/api/rag/health')
}

 export default { auth, documents, search, rag, groups }