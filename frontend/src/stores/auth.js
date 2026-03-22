import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { auth } from '@/api.js'

export const useAuthStore = defineStore('auth', () => {
  const user = ref(null)
  const isLoading = ref(false)
  const error = ref(null)

  const isAuthenticated = computed(() => !!user.value)
  const isAdmin = computed(() => user.value?.role === 'Admin')
  const isManager = computed(() => user.value?.role === 'Manager')
  const isReadOnly = computed(() => user.value?.role === 'ReadOnly')
  const canUpload = computed(() => ['Admin', 'Manager', 'User'].includes(user.value?.role))
  const canDelete = computed(() => ['Admin', 'Manager'].includes(user.value?.role))
  const canEdit = computed(() => ['Admin', 'Manager'].includes(user.value?.role))

  function initFromStorage() {
    try {
      const stored = localStorage.getItem('ged_user')
      if (stored) {
        user.value = JSON.parse(stored)
      }
    } catch (e) {
      localStorage.removeItem('ged_user')
    }
  }

  async function login(username, password) {
    isLoading.value = true
    error.value = null
    
    try {
      const response = await auth.login(username, password)
      const data = await response.json()
      
      if (response.ok) {
        user.value = data
        localStorage.setItem('ged_user', JSON.stringify(data))
        return { success: true }
      } else {
        error.value = data.error || 'Login failed'
        return { success: false, error: error.value }
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return { success: false, error: error.value }
    } finally {
      isLoading.value = false
    }
  }

  async function logout() {
    isLoading.value = true
    try {
      await auth.logout()
    } catch (e) {
      /* silent fail */
    } finally {
      user.value = null
      localStorage.removeItem('ged_user')
      isLoading.value = false
    }
  }

  async function fetchCurrentUser() {
    try {
      const response = await auth.me()
      if (response.ok) {
        const data = await response.json()
        user.value = data
        localStorage.setItem('ged_user', JSON.stringify(data))
      }
    } catch (e) {
      /* silent fail */
    }
  }

  return {
    user,
    isLoading,
    error,
    isAuthenticated,
    isAdmin,
    isManager,
    isReadOnly,
    canUpload,
    canDelete,
    canEdit,
    initFromStorage,
    login,
    logout,
    fetchCurrentUser
  }
})
