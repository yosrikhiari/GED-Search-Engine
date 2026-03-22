<script setup>
defineProps({
  variant: {
    type: String,
    default: 'text',
    validator: (v) => ['text', 'circular', 'rect', 'card', 'list'].includes(v)
  },
  width: {
    type: String,
    default: '100%'
  },
  height: {
    type: String,
    default: '16px'
  }
})
</script>

<template>
  <div
    :class="[
      'skeleton-base',
      variant === 'circular' ? 'skeleton-circular' : '',
      variant === 'rect' ? 'skeleton-rect' : '',
      variant === 'card' ? 'skeleton-card-variant' : '',
      variant === 'list' ? 'skeleton-list-variant' : ''
    ]"
    :style="{
      width: variant === 'text' || variant === 'rect' ? width : undefined,
      height: variant === 'text' || variant === 'rect' ? height : undefined
    }"
  >
    <slot />
  </div>
</template>

<style scoped>
.skeleton-base {
  background: linear-gradient(
    90deg,
    var(--color-surface-hover) 25%,
    var(--color-border) 50%,
    var(--color-surface-hover) 75%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
  border-radius: 6px;
}

.skeleton-circular {
  border-radius: 50%;
}

.skeleton-rect {
  border-radius: 8px;
}

.skeleton-card-variant {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-radius: 12px;
  border: 1px solid var(--color-border);
  background: var(--color-surface);
}

.skeleton-card-variant::before {
  content: '';
  display: block;
  height: 120px;
  background: linear-gradient(
    90deg,
    var(--color-surface-hover) 25%,
    var(--color-border) 50%,
    var(--color-surface-hover) 75%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
}

.skeleton-card-variant::after {
  content: '';
  display: block;
  padding: 12px;
  background: var(--color-surface);
}

.skeleton-list-variant {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  border-radius: 10px;
  border: 1px solid var(--color-border);
  background: var(--color-surface);
}

@keyframes shimmer {
  0% {
    background-position: 200% 0;
  }
  100% {
    background-position: -200% 0;
  }
}
</style>
