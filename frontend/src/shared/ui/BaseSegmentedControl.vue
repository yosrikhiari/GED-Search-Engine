<script setup>
import { computed } from 'vue'

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  options: {
    type: Array,
    required: true
  },
  modelValueKey: {
    type: String,
    default: 'value'
  },
  labelKey: {
    type: String,
    default: 'label'
  }
})

const emit = defineEmits(['update:modelValue'])

const selectedOption = computed(() => {
  return props.options.find(opt => opt[props.modelValueKey] === props.modelValue)
})

function select(option) {
  emit('update:modelValue', option[props.modelValueKey])
}
</script>

<template>
  <div class="inline-flex rounded-lg bg-gray-100 p-1 dark:bg-gray-800">
    <button
      v-for="option in options"
      :key="option[modelValueKey]"
      type="button"
      :class="[
        'px-3 py-1.5 text-sm font-medium rounded-md transition-all duration-200',
        selectedOption === option
          ? 'bg-white text-gray-900 shadow-sm dark:bg-gray-700 dark:text-white'
          : 'text-gray-600 hover:text-gray-900 dark:text-gray-400 dark:hover:text-white'
      ]"
      @click="select(option)"
    >
      {{ option[labelKey] }}
    </button>
  </div>
</template>
