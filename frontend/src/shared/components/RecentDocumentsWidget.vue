<template>
  <div class="recent-docs-widget">
    <div class="widget-header">
      <div class="header-left">
        <h4 class="widget-title">
          {{ title }}
        </h4>
        <span
          v-if="subtitle"
          class="widget-subtitle"
        >{{ subtitle }}</span>
      </div>
      <button
        v-if="showViewAll"
        class="view-all-btn"
        @click="$emit('view-all')"
      >
        Voir tout
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
      </button>
    </div>

    <div
      v-if="loading"
      class="widget-loading"
    >
      <div
        v-for="i in 5"
        :key="i"
        class="skeleton-item"
      >
        <div class="skeleton-icon shimmer" />
        <div class="skeleton-content">
          <div
            class="skeleton-line shimmer"
            style="width: 70%; height: 12px;"
          />
          <div
            class="skeleton-line shimmer"
            style="width: 40%; height: 10px; margin-top: 6px;"
          />
        </div>
      </div>
    </div>

    <div
      v-else-if="documents.length === 0"
      class="widget-empty"
    >
      <svg
        width="40"
        height="40"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
      >
        <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
        <path d="M14 2v6h6" />
      </svg>
      <p>Aucun document récent</p>
    </div>

    <div
      v-else
      class="widget-list"
    >
      <div 
        v-for="(doc, index) in documents" 
        :key="doc.id"
        class="doc-item"
        :style="{ animationDelay: `${index * 50}ms` }"
        @click="$emit('select', doc)"
      >
        <div
          class="doc-icon"
          :class="getFileTypeClass(doc.contentType)"
        >
          <svg
            v-if="isPdf(doc.contentType)"
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <path d="M14 2v6h6" />
          </svg>
          <svg
            v-else-if="isImage(doc.contentType)"
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <rect
              x="3"
              y="3"
              width="18"
              height="18"
              rx="2"
            />
            <circle
              cx="8.5"
              cy="8.5"
              r="1.5"
            />
            <path d="M21 15l-5-5L5 21" />
          </svg>
          <svg
            v-else
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <path d="M14 2v6h6" />
          </svg>
        </div>

        <div class="doc-info">
          <p class="doc-title">
            {{ doc.title }}
          </p>
          <div class="doc-meta">
            <span
              v-if="doc.category"
              class="doc-category"
            >{{ doc.category }}</span>
            <span class="doc-date">{{ formatDate(doc.createdAt) }}</span>
          </div>
        </div>

        <div class="doc-arrow">
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <path d="M9 18l6-6-6-6" />
          </svg>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { defineProps, defineEmits } from 'vue'

defineProps({
  title: {
    type: String,
    default: 'Documents récents'
  },
  subtitle: {
    type: String,
    default: ''
  },
  documents: {
    type: Array,
    default: () => []
  },
  loading: {
    type: Boolean,
    default: false
  },
  showViewAll: {
    type: Boolean,
    default: true
  }
})

defineEmits(['select', 'view-all'])

const isPdf = (ct) => ct?.includes('pdf')
const isImage = (ct) => ct?.includes('image')

const getFileTypeClass = (ct) => {
  if (isPdf(ct)) return 'type-pdf'
  if (isImage(ct)) return 'type-image'
  if (ct?.includes('word') || ct?.includes('document')) return 'type-word'
  if (ct?.includes('excel') || ct?.includes('spreadsheet')) return 'type-excel'
  return 'type-default'
}

const formatDate = (dateStr) => {
  if (!dateStr) return ''
  const date = new Date(dateStr)
  const now = new Date()
  const diff = now - date
  const days = Math.floor(diff / (1000 * 60 * 60 * 24))
  
  if (days === 0) return "Aujourd'hui"
  if (days === 1) return 'Hier'
  if (days < 7) return `Il y a ${days} jours`
  
  return date.toLocaleDateString('fr-FR', { day: '2-digit', month: 'short' })
}
</script>

<style scoped>
.recent-docs-widget {
  background: var(--color-surface);
  border-radius: var(--radius-lg);
  border: 1px solid var(--color-border);
  overflow: hidden;
}

.widget-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--color-border);
}

.header-left {
  display: flex;
  flex-direction: column;
}

.widget-title {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--color-text-primary);
  margin: 0;
}

.widget-subtitle {
  font-size: 0.75rem;
  color: var(--color-text-muted);
  margin-top: 0.15rem;
}

.view-all-btn {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.35rem 0.75rem;
  background: var(--color-primary-light);
  color: var(--color-primary);
  border: none;
  border-radius: 6px;
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease;
}

.view-all-btn:hover {
  background: var(--color-primary);
  color: white;
}

.widget-loading {
  padding: 0.75rem;
}

.skeleton-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.5rem;
}

.skeleton-icon {
  width: 36px;
  height: 36px;
  border-radius: 8px;
  flex-shrink: 0;
}

.skeleton-content {
  flex: 1;
}

.skeleton-line {
  border-radius: 4px;
}

.shimmer {
  background: linear-gradient(
    90deg,
    var(--color-surface-hover) 25%,
    var(--color-border) 50%,
    var(--color-surface-hover) 75%
  );
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
}

@keyframes shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

.widget-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2.5rem 1rem;
  color: var(--color-text-muted);
  gap: 0.75rem;
}

.widget-empty svg {
  opacity: 0.4;
}

.widget-empty p {
  font-size: 0.85rem;
  margin: 0;
}

.widget-list {
  display: flex;
  flex-direction: column;
}

.doc-item {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  padding: 0.875rem 1.25rem;
  cursor: pointer;
  transition: all 0.15s ease;
  border-bottom: 1px solid var(--color-border);
  animation: slideIn 0.3s ease-out both;
}

.doc-item:last-child {
  border-bottom: none;
}

.doc-item:hover {
  background: var(--color-surface-hover);
}

.doc-item:hover .doc-arrow {
  opacity: 1;
  transform: translateX(0);
}

@keyframes slideIn {
  from {
    opacity: 0;
    transform: translateX(-10px);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
}

.doc-icon {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.doc-icon.type-pdf { background: var(--color-pdf-bg); color: var(--color-pdf); }
.doc-icon.type-image { background: var(--color-image-bg); color: var(--color-image); }
.doc-icon.type-word { background: var(--color-word-bg); color: var(--color-word); }
.doc-icon.type-excel { background: var(--color-excel-bg); color: var(--color-excel); }
.doc-icon.type-default { background: var(--color-default-bg); color: var(--color-default); }

.doc-info {
  flex: 1;
  min-width: 0;
}

.doc-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text-primary);
  margin: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.doc-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 0.2rem;
}

.doc-category {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.1rem 0.4rem;
  background: var(--color-primary-light);
  color: var(--color-primary);
  border-radius: 4px;
}

.doc-date {
  font-size: 0.7rem;
  color: var(--color-text-muted);
}

.doc-arrow {
  opacity: 0;
  transform: translateX(-5px);
  transition: all 0.15s ease;
  color: var(--color-text-muted);
}
</style>
