<template>
  <nav
    class="breadcrumb"
    aria-label="Breadcrumb"
  >
    <ol class="breadcrumb-list">
      <li 
        v-for="(crumb, index) in crumbs" 
        :key="index"
        class="breadcrumb-item"
      >
        <span
          v-if="index > 0"
          class="breadcrumb-separator"
        >
          <svg
            v-if="separator === 'chevron'"
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <path d="M9 18l6-6-6-6" />
          </svg>
          <span v-else>/</span>
        </span>
        
        <component 
          :is="crumb.to ? 'router-link' : 'span'"
          :to="crumb.to"
          class="breadcrumb-link"
          :class="{ 'is-current': index === crumbs.length - 1 }"
          @click="crumb.click && crumb.click()"
        >
          <span
            v-if="crumb.icon"
            class="breadcrumb-icon"
            v-html="crumb.icon"
          />
          <span>{{ crumb.label }}</span>
        </component>
      </li>
    </ol>
  </nav>
</template>

<script setup>
defineProps({
  crumbs: {
    type: Array,
    required: true
  },
  separator: {
    type: String,
    default: 'chevron',
    validator: (v) => ['chevron', 'slash'].includes(v)
  }
})
</script>

<style scoped>
.breadcrumb {
  display: flex;
  align-items: center;
}

.breadcrumb-list {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  list-style: none;
  margin: 0;
  padding: 0;
  flex-wrap: wrap;
}

.breadcrumb-item {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.breadcrumb-separator {
  color: var(--color-text-muted);
  display: flex;
  align-items: center;
  font-size: 0.8rem;
}

.breadcrumb-link {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  text-decoration: none;
  padding: 0.25rem 0.5rem;
  border-radius: 6px;
  transition: all 0.15s ease;
  cursor: pointer;
}

.breadcrumb-link:hover:not(.is-current) {
  background: var(--color-surface-hover);
  color: var(--color-text-primary);
}

.breadcrumb-link.is-current {
  color: var(--color-text-primary);
  font-weight: 600;
  cursor: default;
}

.breadcrumb-icon {
  display: flex;
  align-items: center;
  width: 16px;
  height: 16px;
}

.breadcrumb-icon :deep(svg) {
  width: 16px;
  height: 16px;
}
</style>
