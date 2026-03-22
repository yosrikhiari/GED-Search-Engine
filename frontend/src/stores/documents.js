import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { documents } from '@/api.js'

export const useDocumentsStore = defineStore('documents', () => {
  const documentsList = ref([])
  const currentDocument = ref(null)
  const isLoading = ref(false)
  const isUploading = ref(false)
  const uploadProgress = ref(0)
  const error = ref(null)
  const pagination = ref({
    page: 1,
    pageSize: 20,
    total: 0,
    totalPages: 0
  })

  const hasDocuments = computed(() => documentsList.value.length > 0)

  async function fetchDocuments(params = {}) {
    isLoading.value = true
    error.value = null
    
    try {
      const queryParams = {
        page: pagination.value.page,
        pageSize: pagination.value.pageSize,
        ...params
      }
      
      const response = await documents.list(queryParams)
      const data = await response.json()
      
      if (response.ok) {
        documentsList.value = data.documents || data.results || []
        if (data.total !== undefined) {
          pagination.value.total = data.total
          pagination.value.totalPages = Math.ceil(data.total / pagination.value.pageSize)
        }
      } else {
        error.value = data.error || 'Failed to fetch documents'
      }
    } catch (e) {
      error.value = e.message || 'Network error'
    } finally {
      isLoading.value = false
    }
  }

  async function fetchDocument(id) {
    isLoading.value = true
    error.value = null
    
    try {
      const response = await documents.get(id)
      const data = await response.json()
      
      if (response.ok) {
        currentDocument.value = data
        return data
      } else {
        error.value = data.error || 'Failed to fetch document'
        return null
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return null
    } finally {
      isLoading.value = false
    }
  }

  async function deleteDocument(id) {
    isLoading.value = true
    error.value = null
    
    try {
      const response = await documents.delete(id)
      
      if (response.ok) {
        documentsList.value = documentsList.value.filter(d => d.id !== id)
        if (currentDocument.value?.id === id) {
          currentDocument.value = null
        }
        return { success: true }
      } else {
        const data = await response.json()
        error.value = data.error || 'Failed to delete document'
        return { success: false, error: error.value }
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return { success: false, error: error.value }
    } finally {
      isLoading.value = false
    }
  }

  async function updateDocument(id, data) {
    isLoading.value = true
    error.value = null
    
    try {
      const response = await documents.update(id, data)
      const result = await response.json()
      
      if (response.ok) {
        const index = documentsList.value.findIndex(d => d.id === id)
        if (index !== -1) {
          documentsList.value[index] = { ...documentsList.value[index], ...result }
        }
        if (currentDocument.value?.id === id) {
          currentDocument.value = { ...currentDocument.value, ...result }
        }
        return { success: true, data: result }
      } else {
        error.value = result.error || 'Failed to update document'
        return { success: false, error: error.value }
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return { success: false, error: error.value }
    } finally {
      isLoading.value = false
    }
  }

  async function uploadDocuments(formData) {
    isUploading.value = true
    uploadProgress.value = 0
    error.value = null
    
    try {
      const response = await documents.upload(formData)
      const result = await response.json()
      
      if (response.ok) {
        uploadProgress.value = 100
        return { success: true, data: result }
      } else {
        error.value = result.error || 'Failed to upload documents'
        return { success: false, error: error.value }
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return { success: false, error: error.value }
    } finally {
      isUploading.value = false
    }
  }

  function setPage(page) {
    pagination.value.page = page
  }

  function clearError() {
    error.value = null
  }

  return {
    documentsList,
    currentDocument,
    isLoading,
    isUploading,
    uploadProgress,
    error,
    pagination,
    hasDocuments,
    fetchDocuments,
    fetchDocument,
    deleteDocument,
    updateDocument,
    uploadDocuments,
    setPage,
    clearError
  }
})
