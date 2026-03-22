<script setup>
import { computed } from 'vue'

const props = defineProps({
  modelValue: {
    type: [String, Number],
    default: ''
  },
  type: {
    type: String,
    default: 'text'
  },
  placeholder: {
    type: String,
    default: ''
  },
  label: {
    type: String,
    default: ''
  },
  error: {
    type: String,
    default: ''
  },
  disabled: Boolean,
  required: Boolean,
  icon: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['update:modelValue'])

const inputClasses = computed(() => {
  const base = 'w-full px-3 py-2 border rounded-lg transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-offset-0 disabled:bg-gray-100 disabled:cursor-not-allowed dark:disabled:bg-gray-800'
  
  const state = props.error
    ? 'border-red-300 focus:border-red-500 focus:ring-red-200 dark:border-red-700 dark:focus:ring-red-900'
    : 'border-gray-300 focus:border-blue-500 focus:ring-blue-200 dark:border-gray-600 dark:focus:border-blue-400 dark:focus:ring-blue-900/50 dark:bg-gray-700 dark:text-white'
  
  const padding = props.icon ? 'pl-10' : ''
  
  return [base, state, padding].join(' ')
})

function handleInput(e) {
  emit('update:modelValue', e.target.value)
}
</script>

<template>
  <div class="space-y-1">
    <label
      v-if="label"
      class="block text-sm font-medium text-gray-700 dark:text-gray-200"
    >
      {{ label }}
      <span
        v-if="required"
        class="text-red-500"
      >*</span>
    </label>
    
    <div class="relative">
      <div
        v-if="icon"
        class="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 pointer-events-none"
      >
        <component
          :is="icon"
          class="w-5 h-5"
        />
      </div>
      
      <input
        :type="type"
        :value="modelValue"
        :placeholder="placeholder"
        :disabled="disabled"
        :required="required"
        :class="inputClasses"
        @input="handleInput"
      >
    </div>
    
    <p
      v-if="error"
      class="text-sm text-red-600 dark:text-red-400"
    >
      {{ error }}
    </p>
  </div>
</template>
