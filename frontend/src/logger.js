/**
 * GED Frontend Logger
 * Structured, color-coded console logging with step tracking.
 * Each "flow" (upload, search, view, rag) gets its own colored group.
 */

const COLORS = {
  upload:  '#f59e0b',   // amber
  search:  '#2563eb',   // blue
  view:    '#059669',   // green
  ocr:     '#7c3aed',   // purple
  rag:     '#db2777',   // pink
  api:     '#64748b',   // slate
  error:   '#dc2626',   // red
  success: '#16a34a',   // green
  warn:    '#d97706',   // orange
}

const ICONS = {
  upload:  '📤',
  search:  '🔍',
  view:    '👁️',
  ocr:     '🔬',
  rag:     '🤖',
  api:     '🌐',
  error:   '❌',
  success: '✅',
  warn:    '⚠️',
  step:    '→',
  data:    '📦',
  time:    '⏱️',
}

class GedLogger {
  constructor() {
    this._timers = {}
    this._stepCounters = {}
    this._enabled = true
  }

  _color(flow) { return COLORS[flow] || '#374151' }
  _icon(flow)  { return ICONS[flow]  || '•' }

  // ── Core log methods ──────────────────────────────────────────────────────

  /** Start a named flow group */
  startFlow(flow, label) {
    if (!this._enabled) return
    this._stepCounters[flow] = 0
    this._timers[flow] = performance.now()
    console.group(
      `%c${this._icon(flow)} [${flow.toUpperCase()}] ${label}`,
      `color: ${this._color(flow)}; font-weight: bold; font-size: 13px;`
    )
    console.log('%c  Started at ' + new Date().toLocaleTimeString(), 'color: #9ca3af; font-size: 11px;')
  }

  /** End a flow group and print total elapsed time */
  endFlow(flow, label = 'Done') {
    if (!this._enabled) return
    const elapsed = this._timers[flow]
      ? `${(performance.now() - this._timers[flow]).toFixed(0)}ms`
      : '?ms'
    console.log(
      `%c${ICONS.time} [${flow.toUpperCase()}] ${label} — total: ${elapsed}`,
      `color: ${this._color(flow)}; font-weight: bold;`
    )
    console.groupEnd()
    delete this._timers[flow]
    delete this._stepCounters[flow]
  }

  /** Log a numbered step within a flow */
  step(flow, message, data = null) {
    if (!this._enabled) return
    this._stepCounters[flow] = (this._stepCounters[flow] || 0) + 1
    const n = this._stepCounters[flow]
    console.log(
      `%c  ${ICONS.step} Step ${n}: ${message}`,
      `color: ${this._color(flow)}; font-weight: 600;`
    )
    if (data !== null && data !== undefined) {
      console.log('%c    ' + ICONS.data + ' Data:', 'color: #6b7280; font-size: 11px;', data)
    }
  }

  /** Log success within a flow */
  success(flow, message, data = null) {
    if (!this._enabled) return
    console.log(
      `%c  ${ICONS.success} ${message}`,
      `color: ${COLORS.success}; font-weight: 600;`
    )
    if (data !== null && data !== undefined) {
      console.log('%c    ' + ICONS.data + ' Result:', 'color: #6b7280; font-size: 11px;', data)
    }
  }

  /** Log a warning within a flow */
  warn(flow, message, data = null) {
    if (!this._enabled) return
    console.warn(
      `%c  ${ICONS.warn} ${message}`,
      `color: ${COLORS.warn}; font-weight: 600;`,
      data !== null ? data : ''
    )
  }

  /** Log an error within a flow */
  error(flow, message, err = null) {
    if (!this._enabled) return
    console.error(
      `%c  ${ICONS.error} [${flow.toUpperCase()}] ${message}`,
      `color: ${COLORS.error}; font-weight: bold;`,
      err || ''
    )
  }

  /** Log an HTTP request */
  request(method, url, body = null) {
    if (!this._enabled) return
    console.log(
      `%c  ${ICONS.api} ${method} ${url}`,
      'color: #64748b; font-size: 11px; font-style: italic;',
      body ? body : ''
    )
  }

  /** Log an HTTP response */
  response(method, url, status, data = null) {
    if (!this._enabled) return
    const ok = status >= 200 && status < 300
    const color = ok ? COLORS.success : COLORS.error
    console.log(
      `%c  ${ok ? ICONS.success : ICONS.error} ${method} ${url} → ${status}`,
      `color: ${color}; font-size: 11px;`,
      data ? data : ''
    )
  }

  /** General info log (outside a flow) */
  info(message, data = null) {
    if (!this._enabled) return
    console.log('%c• ' + message, 'color: #374151;', data !== null ? data : '')
  }

  /** Measure elapsed time since startFlow */
  elapsed(flow) {
    if (!this._timers[flow]) return null
    return `${(performance.now() - this._timers[flow]).toFixed(0)}ms`
  }
}

export const logger = new GedLogger()
export default logger