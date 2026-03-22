<template>
  <div class="data-display">
    <!-- Loading State -->
    <div v-if="loading" class="data-loading">
      <slot name="loading">
        <div class="loading-grid" :class="gridClass">
          <div v-for="i in skeletonCount" :key="i" class="skeleton-card">
            <div class="skeleton-line w-3/4"></div>
            <div class="skeleton-line w-1/2"></div>
            <div class="skeleton-line w-full"></div>
          </div>
        </div>
      </slot>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="data-error">
      <slot name="error">
        <ErrorState
          :type="errorType"
          :title="errorTitle"
          :message="errorMessage"
          :retry="onRetry"
          :show-go-back="showGoBack"
          @retry="$emit('retry')"
        />
      </slot>
    </div>

    <!-- Empty State -->
    <div v-else-if="isEmpty" class="data-empty">
      <slot name="empty">
        <EmptyState
          v-if="showEmptyState"
          :icon="emptyIcon"
          :title="emptyTitle"
          :description="emptyDescription"
        >
          <template #action>
            <slot name="empty-action" />
          </template>
        </EmptyState>
        <div v-else class="text-center py-8">
          <p class="text-gray-500 dark:text-gray-400">{{ emptyTitle }}</p>
        </div>
      </slot>
    </div>

    <!-- Data Content -->
    <div v-else class="data-content" :class="contentClass">
      <slot />
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import ErrorState from './ErrorState.vue'
import EmptyState from './EmptyState.vue'

const props = defineProps({
  loading: {
    type: Boolean,
    default: false
  },
  error: {
    type: [Error, String, Object, null],
    default: null
  },
  data: {
    type: [Array, Object, null],
    default: null
  },
  skeletonCount: {
    type: Number,
    default: 6
  },
  emptyMessage: {
    type: String,
    default: 'Aucune donnée disponible'
  },
  emptyIcon: {
    type: String,
    default: 'document'
  },
  emptyTitle: {
    type: String,
    default: 'Aucun élément'
  },
  emptyDescription: {
    type: String,
    default: 'Les données que vous recherchez n\'existent pas encore.'
  },
  showEmptyState: {
    type: Boolean,
    default: true
  },
  showGoBack: {
    type: Boolean,
    default: false
  },
  gridClass: {
    type: String,
    default: 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3'
  },
  contentClass: {
    type: String,
    default: ''
  },
  errorType: {
    type: String,
    default: 'error'
  },
  errorTitle: {
    type: String,
    default: 'Une erreur est survenue'
  },
  errorMessage: {
    type: String,
    default: 'Veuillez réessayer ou contacter le support.'
  }
})

const emit = defineEmits(['retry', 'refresh'])

const isEmpty = computed(() => {
  if (props.data === null || props.data === undefined) return false
  if (Array.isArray(props.data)) return props.data.length === 0
  return false
})

function onRetry() {
  emit('retry')
  emit('refresh')
}
</script>

<style scoped>
.data-display {
  @apply w-full;
}

.data-content {
  @apply w-full;
}

.loading-grid {
  @apply grid gap-4;
}

.skeleton-card {
  @apply bg-white dark:bg-gray-800 rounded-lg p-4 shadow-sm;
}

.skeleton-line {
  @apply h-4 bg-gray-200 dark:bg-gray-700 rounded mb-2 animate-pulse;
}

.skeleton-line:last-child {
  @apply mb-0;
}

.data-empty,
.data-error {
  @apply w-full;
}
</style>
