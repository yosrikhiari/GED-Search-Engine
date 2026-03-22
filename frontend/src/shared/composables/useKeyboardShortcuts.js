import { ref, onMounted, onUnmounted } from 'vue'

const registeredShortcuts = new Map()

export function useKeyboardShortcuts() {
  const activeShortcuts = ref(new Map())

  function register(shortcut, callback, description = '') {
    const key = shortcut.toLowerCase()
    
    if (registeredShortcuts.has(key)) {
      return () => {}
    }
    
    registeredShortcuts.set(key, { callback, description, active: true })
    
    return () => unregister(shortcut)
  }

  function unregister(shortcut) {
    const key = shortcut.toLowerCase()
    registeredShortcuts.delete(key)
    activeShortcuts.value.delete(key)
  }

  function handleKeyDown(e) {
    const key = buildKeyString(e)
    
    const handler = registeredShortcuts.get(key)
    if (handler && handler.active) {
      e.preventDefault()
      handler.callback(e)
    }
  }

  function buildKeyString(e) {
    const parts = []
    
    if (e.ctrlKey || e.metaKey) parts.push('ctrl')
    if (e.altKey) parts.push('alt')
    if (e.shiftKey) parts.push('shift')
    
    const key = e.key.toLowerCase()
    if (!['control', 'alt', 'shift', 'meta'].includes(key)) {
      parts.push(key)
    }
    
    return parts.join('+')
  }

  onMounted(() => {
    document.addEventListener('keydown', handleKeyDown)
  })

  onUnmounted(() => {
    document.removeEventListener('keydown', handleKeyDown)
    registeredShortcuts.clear()
  })

  return {
    register,
    unregister,
    activeShortcuts
  }
}

export const defaultShortcuts = [
  { key: 'ctrl+shift+r', label: 'Open RAG Assistant', action: 'openRag' },
  { key: 'ctrl+shift+u', label: 'Upload Documents', action: 'openUpload' },
  { key: 'ctrl+k', label: 'Focus Search', action: 'focusSearch' },
  { key: 'escape', label: 'Close Modal', action: 'closeModal' },
  { key: 'g i', label: 'Go to Home', action: 'goHome' },
  { key: 'g a', label: 'Go to Admin', action: 'goAdmin' }
]
