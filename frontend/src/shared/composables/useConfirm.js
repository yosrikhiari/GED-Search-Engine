import { ref } from 'vue'

export function useConfirm(options = {}) {
  const isOpen = ref(false)
  const pendingAction = ref(null)
  const pendingParams = ref(null)
  const title = ref(options.title || 'Confirm Action')
  const message = ref(options.message || 'Are you sure you want to proceed?')
  const confirmText = ref(options.confirmText || 'Confirm')
  const cancelText = ref(options.cancelText || 'Cancel')
  const variant = ref(options.variant || 'danger')

  function confirm(action, params = null, customOptions = {}) {
    pendingAction.value = action
    pendingParams.value = params
    title.value = customOptions.title || options.title || 'Confirm Action'
    message.value = customOptions.message || options.message || 'Are you sure you want to proceed?'
    confirmText.value = customOptions.confirmText || options.confirmText || 'Confirm'
    cancelText.value = customOptions.cancelText || options.cancelText || 'Cancel'
    variant.value = customOptions.variant || options.variant || 'danger'
    isOpen.value = true
  }

  async function execute() {
    if (pendingAction.value) {
      try {
        await pendingAction.value(pendingParams.value)
      } finally {
        close()
      }
    }
  }

  function close() {
    isOpen.value = false
    pendingAction.value = null
    pendingParams.value = null
  }

  return {
    isOpen,
    title,
    message,
    confirmText,
    cancelText,
    variant,
    confirm,
    execute,
    close
  }
}
