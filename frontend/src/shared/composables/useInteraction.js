import { onMounted, onUnmounted } from 'vue'

export function useClickOutside(targetRef, callback) {
  function handleClick(e) {
    if (targetRef.value && !targetRef.value.contains(e.target)) {
      callback()
    }
  }

  onMounted(() => {
    setTimeout(() => {
      document.addEventListener('click', handleClick)
    }, 0)
  })

  onUnmounted(() => {
    document.removeEventListener('click', handleClick)
  })
}

export function useFocusTrap(elementRef) {
  const focusableSelectors = [
    'button:not([disabled])',
    'a[href]',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])'
  ].join(',')

  function getFocusableElements() {
    return Array.from(elementRef.value?.querySelectorAll(focusableSelectors) || [])
  }

  function handleKeyDown(e) {
    if (e.key !== 'Tab') return

    const focusable = getFocusableElements()
    if (focusable.length === 0) return

    const first = focusable[0]
    const last = focusable[focusable.length - 1]

    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault()
      first.focus()
    }
  }

  onMounted(() => {
    elementRef.value?.addEventListener('keydown', handleKeyDown)
    
    const focusable = getFocusableElements()
    if (focusable.length > 0) {
      focusable[0].focus()
    }
  })

  onUnmounted(() => {
    elementRef.value?.removeEventListener('keydown', handleKeyDown)
  })
}
