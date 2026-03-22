import { ref, computed, watch } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { useSearchStore } from '@/stores/search.js'

export function useSearch() {
  const searchStore = useSearchStore()
  
  const localQuery = ref('')
  const showSuggestions = ref(false)
  const selectedSuggestionIndex = ref(-1)
  
  const debouncedSearch = useDebounceFn(() => {
    searchStore.executeSearch()
  }, 300)
  
  const debouncedSuggestions = useDebounceFn((q) => {
    searchStore.fetchSuggestions(q)
  }, 200)
  
  watch(localQuery, (q) => {
    searchStore.setQuery(q)
    if (q.trim()) {
      debouncedSearch()
      debouncedSuggestions(q)
    } else {
      searchStore.clearSuggestions()
    }
  })
  
  const suggestions = computed(() => searchStore.suggestions)
  const results = computed(() => searchStore.results)
  const isLoading = computed(() => searchStore.isLoading)
  const hasResults = computed(() => searchStore.hasResults)
  const error = computed(() => searchStore.error)
  
  function search() {
    searchStore.executeSearch()
  }
  
  function clearSearch() {
    localQuery.value = ''
    searchStore.setQuery('')
    searchStore.clearSuggestions()
  }
  
  function selectSuggestion(index) {
    if (index >= 0 && index < suggestions.value.length) {
      localQuery.value = suggestions.value[index]
      showSuggestions.value = false
      selectedSuggestionIndex.value = -1
      search()
    }
  }
  
  function handleKeyDown(e) {
    if (!showSuggestions.value || suggestions.value.length === 0) return
    
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        selectedSuggestionIndex.value = Math.min(
          selectedSuggestionIndex.value + 1,
          suggestions.value.length - 1
        )
        break
      case 'ArrowUp':
        e.preventDefault()
        selectedSuggestionIndex.value = Math.max(
          selectedSuggestionIndex.value - 1,
          -1
        )
        break
      case 'Enter':
        e.preventDefault()
        if (selectedSuggestionIndex.value >= 0) {
          selectSuggestion(selectedSuggestionIndex.value)
        } else {
          search()
        }
        break
      case 'Escape':
        showSuggestions.value = false
        selectedSuggestionIndex.value = -1
        break
    }
  }
  
  return {
    query: localQuery,
    suggestions,
    showSuggestions,
    selectedSuggestionIndex,
    results,
    isLoading,
    hasResults,
    error,
    filters: searchStore.filters,
    pagination: searchStore.pagination,
    nlpInterpretation: searchStore.nlpInterpretation,
    search,
    clearSearch,
    selectSuggestion,
    handleKeyDown,
    setFilter: searchStore.setFilter,
    toggleCategoryFilter: searchStore.toggleCategoryFilter,
    resetFilters: searchStore.resetFilters,
    setPage: searchStore.setPage,
    clearNlpInterpretation: searchStore.clearNlpInterpretation
  }
}
