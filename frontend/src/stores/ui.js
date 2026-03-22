import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useUiStore = defineStore('ui', () => {
  const _storedSidebar = localStorage.getItem('ged_sidebar_open')
  const isSidebarOpen = ref(_storedSidebar === null ? true : _storedSidebar === 'true')
  const isDarkMode = ref(false)
  const activeModal = ref(null)
  const modalData = ref(null)
  const toasts = ref([])
  const isGlobalLoading = ref(false)

  const hasActiveModal = computed(() => activeModal.value !== null)

  function toggleSidebar() {
    isSidebarOpen.value = !isSidebarOpen.value
    localStorage.setItem('ged_sidebar_open', String(isSidebarOpen.value))
  }

  function openSidebar() {
    isSidebarOpen.value = true
  }

  function closeSidebar() {
    isSidebarOpen.value = false
  }

  function toggleDarkMode() {
    isDarkMode.value = !isDarkMode.value
    applyDarkMode()
  }

  function applyDarkMode() {
    if (isDarkMode.value) {
      document.documentElement.classList.add('dark')
    } else {
      document.documentElement.classList.remove('dark')
    }
  }

  function initDarkMode() {
    const stored = localStorage.getItem('ged_theme')
    if (stored === 'dark' || (!stored && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
      isDarkMode.value = true
      applyDarkMode()
    }
  }

  function openModal(name, data = null) {
    activeModal.value = name
    modalData.value = data
  }

  function closeModal() {
    activeModal.value = null
    modalData.value = null
  }

  function addToast(message, type = 'info', duration = 5000, action = null) {
    const id = Date.now() + Math.random()
    const toast = { id, message, type, duration, action, createdAt: new Date() }
    
    toasts.value.push(toast)
    
    if (duration > 0) {
      setTimeout(() => {
        removeToast(id)
      }, duration)
    }
    
    return id
  }

  function removeToast(id) {
    const index = toasts.value.findIndex(t => t.id === id)
    if (index !== -1) {
      toasts.value.splice(index, 1)
    }
  }

  function clearToasts() {
    toasts.value = []
  }

  function setGlobalLoading(loading) {
    isGlobalLoading.value = loading
  }

  function success(message, duration = 5000) {
    return addToast(message, 'success', duration)
  }

  function error(message, duration = 7000) {
    return addToast(message, 'error', duration)
  }

  function warning(message, duration = 6000) {
    return addToast(message, 'warning', duration)
  }

  function info(message, duration = 5000) {
    return addToast(message, 'info', duration)
  }

  return {
    isSidebarOpen,
    isDarkMode,
    activeModal,
    modalData,
    toasts,
    isGlobalLoading,
    hasActiveModal,
    toggleSidebar,
    openSidebar,
    closeSidebar,
    toggleDarkMode,
    applyDarkMode,
    initDarkMode,
    openModal,
    closeModal,
    addToast,
    removeToast,
    clearToasts,
    setGlobalLoading,
    success,
    error,
    warning,
    info
  }
})
