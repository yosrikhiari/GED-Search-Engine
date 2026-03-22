<template>
  <div class="error-state">
    <div class="error-state-content">
      <!-- Error Icon -->
      <div class="error-icon" :class="iconClass">
        <svg v-if="type === 'network'" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" d="M18.364 5.636a9 9 0 010 12.728m0 0l-2.829-2.829m2.829 2.829L21 21M15.536 8.464a5 5 0 010 7.072m0 0l-2.829-2.829m-4.243 2.829a4.978 4.978 0 01-1.414-2.83m-1.414 5.658a9 9 0 01-2.167-9.238m7.824 2.167a1 1 0 111.414 1.414m-1.414-1.414L3 3m8.293 8.293l1.414 1.414" />
        </svg>
        <svg v-else-if="type === 'auth'" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" />
        </svg>
        <svg v-else-if="type === 'empty'" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" d="M20.25 7.5l-.625 10.632a2.25 2.25 0 01-2.247 2.118H6.622a2.25 2.25 0 01-2.247-2.118L3.75 7.5m6 4.125l2.25 2.25m0 0l2.25 2.25M12 13.875l2.25-2.25M12 13.875l-2.25 2.25M3.375 7.5h17.25c.621 0 1.125-.504 1.125-1.125v-1.5c0-.621-.504-1.125-1.125-1.125H3.375c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125z" />
        </svg>
        <svg v-else xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
        </svg>
      </div>

      <!-- Title -->
      <h3 class="error-title">{{ title }}</h3>

      <!-- Message -->
      <p class="error-message">{{ message }}</p>

      <!-- Actions -->
      <div class="error-actions">
        <button v-if="retry" @click="handleRetry" class="error-btn retry-btn">
          <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="btn-icon">
            <path stroke-linecap="round" stroke-linejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />
          </svg>
          Réessayer
        </button>
        <button v-if="showGoBack" @click="goBack" class="error-btn back-btn">
          <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="btn-icon">
            <path stroke-linecap="round" stroke-linejoin="round" d="M9 15L3 9m0 0l6-6M3 9h12a6 6 0 010 12h-3" />
          </svg>
          Retour
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  type: {
    type: String,
    default: 'error',
    validator: (v) => ['error', 'network', 'auth', 'empty', 'warning'].includes(v)
  },
  title: {
    type: String,
    default: 'Une erreur est survenue'
  },
  message: {
    type: String,
    default: 'Veuillez réessayer ou contacter le support.'
  },
  retry: {
    type: Function,
    default: null
  },
  showGoBack: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['retry', 'goBack'])

const iconClass = computed(() => ({
  'error': props.type === 'error',
  'network': props.type === 'network',
  'auth': props.type === 'auth',
  'empty': props.type === 'empty',
  'warning': props.type === 'warning'
}))

function handleRetry() {
  if (props.retry) {
    props.retry()
  }
  emit('retry')
}

function goBack() {
  if (window.history.length > 1) {
    window.history.back()
  }
  emit('goBack')
}
</script>

<style scoped>
.error-state {
  @apply flex items-center justify-center py-12 px-4;
}

.error-state-content {
  @apply text-center max-w-md;
}

.error-icon {
  @apply mx-auto h-16 w-16 mb-4;
}

.error-icon.error {
  @apply text-red-500;
}

.error-icon.network {
  @apply text-orange-500;
}

.error-icon.auth {
  @apply text-yellow-500;
}

.error-icon.empty {
  @apply text-gray-400;
}

.error-icon.warning {
  @apply text-amber-500;
}

.error-icon svg {
  @apply h-full w-full;
}

.error-title {
  @apply text-lg font-semibold text-gray-900 dark:text-gray-100 mb-2;
}

.error-message {
  @apply text-sm text-gray-500 dark:text-gray-400 mb-6;
}

.error-actions {
  @apply flex items-center justify-center gap-3;
}

.error-btn {
  @apply inline-flex items-center gap-2 px-4 py-2 text-sm font-medium rounded-lg transition-colors;
}

.retry-btn {
  @apply bg-blue-600 text-white hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600;
}

.back-btn {
  @apply bg-gray-100 text-gray-700 hover:bg-gray-200 dark:bg-gray-700 dark:text-gray-200 dark:hover:bg-gray-600;
}

.btn-icon {
  @apply h-4 w-4;
}
</style>
