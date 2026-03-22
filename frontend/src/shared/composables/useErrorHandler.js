/**
 * Error handling composable with toast notifications
 * Provides centralized error handling with user-friendly messages
 */

import { useUiStore } from '../stores/ui.js'

/**
 * Error types for better categorization
 */
const ErrorTypes = {
  NETWORK: 'network',
  AUTH: 'auth',
  VALIDATION: 'validation',
  SERVER: 'server',
  UNKNOWN: 'unknown'
}

/**
 * Maps HTTP status codes to error types
 */
function getErrorType(status, data) {
  if (!status) return ErrorTypes.NETWORK
  if (status === 401 || status === 403) return ErrorTypes.AUTH
  if (status === 400 || status === 422) return ErrorTypes.VALIDATION
  if (status >= 500) return ErrorTypes.SERVER
  return ErrorTypes.UNKNOWN
}

/**
 * Extracts user-friendly error message from various error formats
 */
function extractMessage(error) {
  if (!error) return 'Une erreur inattendue est survenue'
  
  // Handle response data
  if (error.response?.data?.message) return error.response.data.message
  if (error.response?.data?.error) return error.response.data.error
  if (error.response?.data?.errors) {
    const errors = error.response.data.errors
    if (Array.isArray(errors)) return errors.join(', ')
    if (typeof errors === 'object') return Object.values(errors).flat().join(', ')
  }
  
  // Handle direct message
  if (error.message) return error.message
  
  // Handle network errors
  if (error.code === 'ECONNREFUSED') return 'Impossible de se connecter au serveur'
  if (error.code === 'NETWORK_ERROR') return 'Erreur de connexion réseau'
  if (error.name === 'AbortError') return 'Requête annulée'
  
  return 'Une erreur inattendue est survenue'
}

/**
 * Creates a context-aware error handler
 * @param {string} context - Context name for logging (e.g., 'upload', 'search')
 * @returns {object} Error handling utilities
 */
export function useErrorHandler(context = 'app') {
  const ui = useUiStore()

  /**
   * Handle an error with optional custom message
   * @param {Error} error - The error to handle
   * @param {object} options - Handler options
   * @param {string} options.context - Override context for this call
   * @param {string} options.userMessage - Custom user-facing message
   * @param {boolean} options.showToast - Whether to show toast notification (default: true)
   * @param {string} options.toastType - Toast type: 'error', 'warning', 'info' (default: 'error')
   */
  function handleError(error, options = {}) {
    const {
      context: overrideContext = context,
      userMessage = null,
      showToast = true,
      toastType = 'error'
    } = options

    const errorType = getErrorType(error?.response?.status, error?.response?.data)
    const message = userMessage || extractMessage(error)
    
    // Log for debugging
    console.error(`[${overrideContext}] Error:`, {
      type: errorType,
      message,
      status: error?.response?.status,
      data: error?.response?.data
    })

    // Show toast notification
    if (showToast) {
      const title = getTitleForType(errorType, overrideContext)
      ui.addToast({
        type: toastType,
        title,
        message,
        duration: getDurationForType(errorType)
      })
    }

    return {
      errorType,
      message,
      handled: true
    }
  }

  /**
   * Async wrapper that automatically handles errors
   * @param {Function} fn - Async function to wrap
   * @param {object} options - Handler options
   * @returns {Promise} Result of the function or void on error
   */
  async function withErrorHandling(fn, options = {}) {
    try {
      return await fn()
    } catch (error) {
      handleError(error, options)
    }
  }

  /**
   * Handle multiple errors (e.g., batch operations)
   * @param {Error[]} errors - Array of errors
   * @param {object} options - Handler options
   */
  function handleErrors(errors, options = {}) {
    if (!Array.isArray(errors)) {
      handleError(errors, options)
      return
    }

    const message = errors.length === 1
      ? extractMessage(errors[0])
      : `${errors.length} erreurs sont survenues`

    console.error(`[${context}] Multiple errors:`, errors)
    
    ui.addToast({
      type: 'error',
      title: 'Erreurs multiples',
      message,
      duration: 7000
    })
  }

  /**
   * Create a scoped error handler for a specific operation
   * @param {string} scope - The scope name
   * @returns {object} Scoped error handler
   */
  function createScopedHandler(scope) {
    return {
      handle: (error, opts = {}) => handleError(error, { ...opts, context: `${context}:${scope}` }),
      with: (fn, opts = {}) => withErrorHandling(fn, { ...opts, context: `${context}:${scope}` }),
      handleMultiple: (errors, opts = {}) => handleErrors(errors, { ...opts, context: `${context}:${scope}` })
    }
  }

  return {
    handleError,
    withErrorHandling,
    handleErrors,
    createScopedHandler,
    ErrorTypes
  }
}

/**
 * Get appropriate title for error type
 */
function getTitleForType(type, context) {
  const titles = {
    [ErrorTypes.NETWORK]: 'Erreur de connexion',
    [ErrorTypes.AUTH]: 'Accès refusé',
    [ErrorTypes.VALIDATION]: 'Validation échouée',
    [ErrorTypes.SERVER]: 'Erreur serveur',
    [ErrorTypes.UNKNOWN]: 'Erreur'
  }
  return titles[type] || 'Erreur'
}

/**
 * Get appropriate duration for toast based on error type
 */
function getDurationForType(type) {
  const durations = {
    [ErrorTypes.NETWORK]: 8000,
    [ErrorTypes.AUTH]: 5000,
    [ErrorTypes.VALIDATION]: 6000,
    [ErrorTypes.SERVER]: 7000,
    [ErrorTypes.UNKNOWN]: 5000
  }
  return durations[type] || 5000
}

export default useErrorHandler
