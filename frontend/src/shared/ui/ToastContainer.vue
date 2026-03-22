<script setup>

import { CheckCircle, XCircle, AlertTriangle, Info, X } from 'lucide-vue-next'
import { useUiStore } from '@/stores/ui.js'

const uiStore = useUiStore()

const icons = {
  success: CheckCircle,
  error: XCircle,
  warning: AlertTriangle,
  info: Info
}

const colorClasses = {
  success: 'bg-green-50 border-green-200 text-green-800 dark:bg-green-900/30 dark:border-green-800 dark:text-green-200',
  error: 'bg-red-50 border-red-200 text-red-800 dark:bg-red-900/30 dark:border-red-800 dark:text-red-200',
  warning: 'bg-amber-50 border-amber-200 text-amber-800 dark:bg-amber-900/30 dark:border-amber-800 dark:text-amber-200',
  info: 'bg-blue-50 border-blue-200 text-blue-800 dark:bg-blue-900/30 dark:border-blue-800 dark:text-blue-200'
}

const iconClasses = {
  success: 'text-green-500',
  error: 'text-red-500',
  warning: 'text-amber-500',
  info: 'text-blue-500'
}
</script>

<template>
  <Teleport to="body">
    <div class="fixed top-4 right-4 z-[60] flex flex-col gap-2 pointer-events-none">
      <TransitionGroup
        enter-active-class="transition-all duration-300"
        leave-active-class="transition-all duration-300"
        enter-from-class="opacity-0 translate-x-8"
        leave-to-class="opacity-0 translate-x-8"
      >
        <div
          v-for="toast in uiStore.toasts"
          :key="toast.id"
          :class="[
            'flex items-start gap-3 p-4 rounded-lg border shadow-lg pointer-events-auto max-w-sm',
            colorClasses[toast.type]
          ]"
        >
          <component
            :is="icons[toast.type]"
            :class="['w-5 h-5 flex-shrink-0 mt-0.5', iconClasses[toast.type]]"
          />
          
          <div class="flex-1 min-w-0">
            <p class="text-sm font-medium">
              {{ toast.message }}
            </p>
            <button
              v-if="toast.action"
              class="mt-1 text-sm underline hover:no-underline"
              @click="toast.action.callback"
            >
              {{ toast.action.label }}
            </button>
          </div>
          
          <button
            type="button"
            class="flex-shrink-0 p-1 rounded hover:bg-black/10 dark:hover:bg-white/10 transition-colors"
            @click="uiStore.removeToast(toast.id)"
          >
            <X class="w-4 h-4" />
          </button>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>
