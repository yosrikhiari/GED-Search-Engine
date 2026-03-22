<template>
  <div
    class="empty-state"
    :class="{ 'empty-compact': compact }"
  >
    <div class="empty-illustration">
      <svg
        :width="sizeMap[size].svg"
        :height="sizeMap[size].svg"
        viewBox="0 0 120 120"
        fill="none"
      >
        <component
          :is="currentIllustration"
          :primary-color="primaryColor"
          :bg-color="bgColor"
        />
      </svg>
    </div>
    <div class="empty-content">
      <h3 class="empty-title">
        {{ title }}
      </h3>
      <p
        v-if="description"
        class="empty-description"
      >
        {{ description }}
      </p>
      <div
        v-if="$slots.action && !compact"
        class="empty-action"
      >
        <slot name="action" />
      </div>
    </div>
    <div
      v-if="tips && !compact"
      class="empty-tips"
    >
      <div
        v-for="(tip, i) in tips"
        :key="i"
        class="tip-item"
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
        >
          <path d="M9 18l6-6-6-6" />
        </svg>
        <span>{{ tip }}</span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, h } from 'vue'

const props = defineProps({
  type: {
    type: String,
    default: 'search'
  },
  title: {
    type: String,
    default: 'Aucun résultat'
  },
  description: {
    type: String,
    default: ''
  },
  size: {
    type: String,
    default: 'md',
    validator: (v) => ['sm', 'md', 'lg'].includes(v)
  },
  compact: {
    type: Boolean,
    default: false
  },
  tips: {
    type: Array,
    default: () => []
  }
})

const sizeMap = {
  sm: { svg: 80 },
  md: { svg: 120 },
  lg: { svg: 160 }
}

const illustrationMap = {
  search: SearchIllustration,
  documents: DocumentsIllustration,
  upload: UploadIllustration,
  users: UsersIllustration,
  groups: GroupsIllustration,
  settings: SettingsIllustration,
  default: DefaultIllustration
}

const currentIllustration = computed(() => illustrationMap[props.type] || DefaultIllustration)

const primaryColor = '#3b82f6'
const bgColor = '#eff6ff'

const SearchIllustration = {
  render() {
    return h('g', [
      h('circle', { cx: '60', cy: '60', r: '50', fill: '#eff6ff' }),
      h('circle', { cx: '60', cy: '60', r: '35', fill: 'white', stroke: '#bfdbfe', 'stroke-width': '2' }),
      h('circle', { cx: '60', cy: '60', r: '22', fill: '#dbeafe' }),
      h('path', { d: 'M75 75 L95 95', stroke: '#93c5fd', 'stroke-width': '6', 'stroke-linecap': 'round' }),
      h('path', { d: 'M52 52 L68 68 M60 52 L60 68 M52 60 L68 60', stroke: '#3b82f6', 'stroke-width': '2.5', 'stroke-linecap': 'round' })
    ])
  }
}

const DocumentsIllustration = {
  render() {
    return h('g', [
      h('rect', { x: '15', y: '25', width: '55', height: '70', rx: '8', fill: '#eff6ff' }),
      h('rect', { x: '35', y: '15', width: '55', height: '70', rx: '8', fill: 'white', stroke: '#bfdbfe', 'stroke-width': '2' }),
      h('rect', { x: '50', y: '30', width: '25', height: '4', rx: '2', fill: '#bfdbfe' }),
      h('rect', { x: '50', y: '40', width: '20', height: '4', rx: '2', fill: '#dbeafe' }),
      h('rect', { x: '50', y: '50', width: '22', height: '4', rx: '2', fill: '#dbeafe' }),
      h('path', { d: 'M35 55 L75 55', stroke: '#e2e8f0', 'stroke-width': '1.5', 'stroke-linecap': 'round' }),
      h('path', { d: 'M35 65 L65 65', stroke: '#e2e8f0', 'stroke-width': '1.5', 'stroke-linecap': 'round' }),
      h('path', { d: 'M35 75 L70 75', stroke: '#e2e8f0', 'stroke-width': '1.5', 'stroke-linecap': 'round' })
    ])
  }
}

const UploadIllustration = {
  render() {
    return h('g', [
      h('rect', { x: '20', y: '40', width: '80', height: '55', rx: '10', fill: '#eff6ff' }),
      h('path', { d: 'M45 65 L60 50 L75 65', stroke: '#3b82f6', 'stroke-width': '3', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', fill: 'none' }),
      h('path', { d: 'M60 50 L60 75', stroke: '#3b82f6', 'stroke-width': '3', 'stroke-linecap': 'round' }),
      h('path', { d: 'M30 75 L30 80', stroke: '#93c5fd', 'stroke-width': '3', 'stroke-linecap': 'round' }),
      h('path', { d: 'M60 75 L60 85', stroke: '#93c5fd', 'stroke-width': '3', 'stroke-linecap': 'round' }),
      h('path', { d: 'M90 75 L90 80', stroke: '#93c5fd', 'stroke-width': '3', 'stroke-linecap': 'round' })
    ])
  }
}

const UsersIllustration = {
  render() {
    return h('g', [
      h('circle', { cx: '60', cy: '40', r: '18', fill: '#dbeafe' }),
      h('path', { d: 'M35 90 Q35 65 60 65 Q85 65 85 90', fill: '#eff6ff' }),
      h('circle', { cx: '30', cy: '60', r: '10', fill: '#f1f5f9', opacity: '0.8' }),
      h('path', { d: 'M15 95 Q15 80 30 80 Q45 80 45 95', fill: '#f8fafc', opacity: '0.6' }),
      h('circle', { cx: '90', cy: '60', r: '10', fill: '#f1f5f9', opacity: '0.8' }),
      h('path', { d: 'M75 95 Q75 80 90 80 Q105 80 105 95', fill: '#f8fafc', opacity: '0.6' })
    ])
  }
}

const GroupsIllustration = {
  render() {
    return h('g', [
      h('circle', { cx: '60', cy: '35', r: '15', fill: '#ede9fe' }),
      h('path', { d: 'M30 80 Q30 60 60 60 Q90 60 90 80', fill: '#f5f3ff' }),
      h('circle', { cx: '30', cy: '70', r: '10', fill: '#fef3c7', opacity: '0.8' }),
      h('circle', { cx: '90', cy: '70', r: '10', fill: '#dbeafe', opacity: '0.8' }),
      h('path', { d: 'M45 60 L45 75', stroke: '#c4b5fd', 'stroke-width': '2', 'stroke-linecap': 'round', 'stroke-dasharray': '3 3' }),
      h('path', { d: 'M75 60 L75 75', stroke: '#93c5fd', 'stroke-width': '2', 'stroke-linecap': 'round', 'stroke-dasharray': '3 3' })
    ])
  }
}

const SettingsIllustration = {
  render() {
    return h('g', [
      h('circle', { cx: '60', cy: '60', r: '30', fill: '#f1f5f9' }),
      h('circle', { cx: '60', cy: '60', r: '18', fill: 'white', stroke: '#e2e8f0', 'stroke-width': '2' }),
      h('path', { d: 'M60 42 L60 48 M60 72 L60 78 M42 60 L48 60 M72 60 L78 60', stroke: '#94a3b8', 'stroke-width': '3', 'stroke-linecap': 'round' }),
      h('circle', { cx: '60', cy: '60', r: '5', fill: '#cbd5e1' })
    ])
  }
}

const DefaultIllustration = {
  render() {
    return h('g', [
      h('circle', { cx: '60', cy: '60', r: '40', fill: '#f8fafc', stroke: '#e2e8f0', 'stroke-width': '2' }),
      h('path', { d: 'M45 60 L55 70 L75 50', stroke: '#94a3b8', 'stroke-width': '3', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', fill: 'none' })
    ])
  }
}
</script>

<style scoped>
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1.25rem;
  padding: 3rem 1.5rem;
  text-align: center;
  animation: fadeInUp 0.4s ease-out;
}

.empty-state.empty-compact {
  padding: 1.5rem 1rem;
  gap: 0.75rem;
}

.empty-illustration {
  opacity: 0.95;
}

.empty-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  max-width: 360px;
}

.empty-title {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--color-text-primary);
  margin: 0;
}

.empty-compact .empty-title {
  font-size: 0.9rem;
}

.empty-description {
  font-size: 0.875rem;
  color: var(--color-text-muted);
  line-height: 1.6;
  margin: 0;
}

.empty-compact .empty-description {
  font-size: 0.8rem;
}

.empty-action {
  margin-top: 0.75rem;
}

.empty-tips {
  margin-top: 1rem;
  padding: 1rem;
  background: var(--color-surface-hover);
  border-radius: 12px;
  max-width: 320px;
  text-align: left;
}

.tip-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  margin-bottom: 8px;
}

.tip-item:last-child {
  margin-bottom: 0;
}

.tip-item svg {
  flex-shrink: 0;
  margin-top: 2px;
  opacity: 0.5;
}

@keyframes fadeInUp {
  from {
    opacity: 0;
    transform: translateY(12px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
