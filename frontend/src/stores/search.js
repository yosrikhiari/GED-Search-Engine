import { defineStore } from 'pinia'
import { ref, computed, reactive } from 'vue'
import { search } from '@/api.js'

export const useSearchStore = defineStore('search', () => {
  const query = ref('')
  const results = ref([])
  const isLoading = ref(false)
  const error = ref(null)
  const suggestions = ref([])
  const nlpInterpretation = ref(null)
  const ragAnswer = ref(null)
  const isRagLoading = ref(false)
  
  const filters = reactive({
    categories: [],
    fileTypes: [],
    dateFrom: null,
    dateTo: null,
    ocrStatus: null,
    service: null
  })

  const pagination = ref({
    page: 1,
    pageSize: 20,
    total: 0,
    totalPages: 0
  })

  const hasResults = computed(() => results.value.length > 0)
  const hasActiveFilters = computed(() => 
    filters.categories.length > 0 ||
    filters.fileTypes.length > 0 ||
    filters.dateFrom ||
    filters.dateTo ||
    filters.ocrStatus ||
    filters.service
  )

  async function executeSearch() {
    if (!query.value.trim() && !hasActiveFilters.value) {
      results.value = []
      return
    }

    isLoading.value = true
    error.value = null
    
    try {
      const params = {
        q: query.value,
        page: pagination.value.page,
        pageSize: pagination.value.pageSize,
        ...buildFilters()
      }
      
      const response = await search.query(params)
      const data = await response.json()
      
      if (response.ok) {
        results.value = data.documents || data.results || []
        if (data.totalResults !== undefined) {
          pagination.value.total = data.totalResults
          pagination.value.totalPages = Math.ceil(data.totalResults / pagination.value.pageSize)
        }
        if (data.nlpInterpretation) {
          nlpInterpretation.value = data.nlpInterpretation
        }
      } else {
        error.value = data.error || 'Search failed'
      }
    } catch (e) {
      error.value = e.message || 'Network error'
    } finally {
      isLoading.value = false
    }
  }

  function buildFilters() {
    const filterParams = {}
    
    if (filters.categories.length > 0) {
      filterParams.categories = filters.categories.join(',')
    }
    if (filters.fileTypes.length > 0) {
      filterParams.fileTypes = filters.fileTypes.join(',')
    }
    if (filters.dateFrom) {
      filterParams.dateFrom = filters.dateFrom
    }
    if (filters.dateTo) {
      filterParams.dateTo = filters.dateTo
    }
    if (filters.ocrStatus) {
      filterParams.ocrStatus = filters.ocrStatus
    }
    if (filters.service) {
      filterParams.service = filters.service
    }
    
    return filterParams
  }

  async function fetchSuggestions(q) {
    if (!q.trim()) {
      suggestions.value = []
      return
    }
    
    try {
      const response = await search.suggestions(q)
      const data = await response.json()
      
      if (response.ok) {
        suggestions.value = data.suggestions || []
      }
    } catch (e) {
      /* silent fail */
    }
  }

  async function askRag(question, documentIds = []) {
    isRagLoading.value = true
    ragAnswer.value = null
    error.value = null
    
    try {
      const response = await search.askRag({
        query: question,
        documentIds
      })
      const data = await response.json()
      
      if (response.ok) {
        ragAnswer.value = data
      } else {
        error.value = data.error || 'RAG query failed'
      }
    } catch (e) {
      error.value = e.message || 'Network error'
    } finally {
      isRagLoading.value = false
    }
  }

  function setQuery(q) {
    query.value = q
    pagination.value.page = 1
  }

  function setFilter(key, value) {
    filters[key] = value
    pagination.value.page = 1
  }

  function toggleCategoryFilter(category) {
    const index = filters.categories.indexOf(category)
    if (index === -1) {
      filters.categories.push(category)
    } else {
      filters.categories.splice(index, 1)
    }
    pagination.value.page = 1
  }

  function resetFilters() {
    filters.categories = []
    filters.fileTypes = []
    filters.dateFrom = null
    filters.dateTo = null
    filters.ocrStatus = null
    filters.service = null
    pagination.value.page = 1
  }

  function setPage(page) {
    pagination.value.page = page
  }

  function clearNlpInterpretation() {
    nlpInterpretation.value = null
  }

  function clearRagAnswer() {
    ragAnswer.value = null
  }

  function clearSuggestions() {
    suggestions.value = []
  }

  function clearError() {
    error.value = null
  }

  return {
    query,
    results,
    isLoading,
    error,
    suggestions,
    nlpInterpretation,
    ragAnswer,
    isRagLoading,
    filters,
    pagination,
    hasResults,
    hasActiveFilters,
    executeSearch,
    fetchSuggestions,
    askRag,
    setQuery,
    setFilter,
    toggleCategoryFilter,
    resetFilters,
    setPage,
    clearNlpInterpretation,
    clearRagAnswer,
    clearSuggestions,
    clearError
  }
})
