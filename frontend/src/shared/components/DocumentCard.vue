<template>
  <div
    class="doc-card"
    :class="{ 'view-grid': viewMode === 'grid', 'view-list': viewMode === 'list' }"
    @click="$emit('click', doc)"
  >
    <!-- Grid view -->
    <template v-if="viewMode === 'grid'">
      <div class="card-thumb">
        <div
          class="thumb-preview"
          :class="fileTypeClass"
        >
          <svg
            v-if="isPdf"
            width="32"
            height="32"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <path d="M14 2v6h6" />
            <path d="M10 12h4M10 16h4M8 12h.01M8 16h.01" />
          </svg>
          <svg
            v-else-if="isImage"
            width="32"
            height="32"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
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
            v-else-if="isWord"
            width="32"
            height="32"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <path d="M14 2v6h6" />
            <path d="M16 13H8M16 17H8M10 9H8" />
          </svg>
          <svg
            v-else-if="isExcel"
            width="32"
            height="32"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <path d="M14 2v6h6" />
            <path d="M8 13h8M8 17h5" />
          </svg>
          <svg
            v-else
            width="32"
            height="32"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <path d="M14 2v6h6" />
          </svg>
        </div>
        
        <!-- Status overlay -->
        <div class="thumb-status">
          <span
            v-if="doc.isOcrProcessed"
            class="status-chip status-ocr"
            title="Contenu indexé via OCR"
          >
            <svg
              width="10"
              height="10"
              viewBox="0 0 24 24"
              fill="currentColor"
            ><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z" /></svg>
            OCR
          </span>
          <span
            v-else-if="doc.ocrStatus !== undefined && doc.ocrStatus < 4"
            class="status-chip status-processing"
            title="Traitement en cours"
          >
            <span class="pulse-dot" />
            En cours
          </span>
        </div>

        <!-- File type badge -->
        <div
          class="thumb-type-badge"
          :class="fileTypeClass"
        >
          {{ fileExtension }}
        </div>
      </div>

      <div class="card-body">
        <h4
          class="card-title"
          :title="doc.title"
        >
          {{ doc.title }}
        </h4>
        
        <div class="card-meta-row">
          <span
            v-if="doc.category"
            class="category-chip"
            :class="categoryClass"
          >
            {{ doc.category }}
          </span>
          <span
            v-if="doc.service"
            class="service-chip"
          >
            {{ doc.service }}
          </span>
        </div>

        <div class="card-footer">
          <span class="meta-date">{{ formattedDate }}</span>
          <span class="meta-size">{{ formattedSize }}</span>
        </div>
      </div>
    </template>

    <!-- List view -->
    <template v-else>
      <div
        class="list-thumb"
        :class="fileTypeClass"
      >
        <svg
          v-if="isPdf"
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
        >
          <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
          <path d="M14 2v6h6" />
          <path d="M10 12h4M10 16h4" />
        </svg>
        <svg
          v-else-if="isImage"
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
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
          v-else-if="isWord"
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
        >
          <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
          <path d="M14 2v6h6" />
          <path d="M16 13H8M16 17H8M10 9H8" />
        </svg>
        <svg
          v-else-if="isExcel"
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
        >
          <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
          <path d="M14 2v6h6" />
          <path d="M8 13h8M8 17h5" />
        </svg>
        <svg
          v-else
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
        >
          <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
          <path d="M14 2v6h6" />
        </svg>
      </div>

      <div class="list-body">
        <h4
          class="list-title"
          :title="doc.title"
        >
          {{ doc.title }}
        </h4>
        <div class="list-meta">
          <span
            v-if="doc.category"
            class="category-chip sm"
            :class="categoryClass"
          >{{ doc.category }}</span>
          <span class="meta-text">{{ formattedDate }}</span>
          <span class="meta-dot">·</span>
          <span class="meta-text">{{ formattedSize }}</span>
          <span
            v-if="doc.isOcrProcessed"
            class="ocr-indicator"
          >
            <svg
              width="10"
              height="10"
              viewBox="0 0 24 24"
              fill="currentColor"
            ><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z" /></svg>
          </span>
        </div>
      </div>

      <div class="list-actions">
        <button
          class="action-btn"
          title="Voir"
          @click.stop="$emit('view', doc)"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
            <circle
              cx="12"
              cy="12"
              r="3"
            />
          </svg>
        </button>
        <button
          class="action-btn"
          title="Télécharger"
          @click.stop="$emit('download', doc)"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
            <polyline points="7 10 12 15 17 10" />
            <line
              x1="12"
              y1="15"
              x2="12"
              y2="3"
            />
          </svg>
        </button>
      </div>
    </template>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  doc: {
    type: Object,
    required: true
  },
  viewMode: {
    type: String,
    default: 'grid',
    validator: (v) => ['list', 'grid'].includes(v)
  }
})

defineEmits(['click', 'view', 'download'])

const ct = computed(() => props.doc.contentType || '')
const isPdf = computed(() => ct.value.includes('pdf'))
const isImage = computed(() => ct.value.includes('image'))
const isWord = computed(() => ct.value.includes('word') || ct.value.includes('document'))
const isExcel = computed(() => ct.value.includes('spreadsheet') || ct.value.includes('excel'))

const fileExtension = computed(() => {
  const ext = ct.value.split('/')[1]?.toUpperCase() || 'FILE'
  const map = {
    'PDF': 'PDF',
    'WORD': 'DOC',
    'DOCUMENT': 'DOC',
    'SPREADSHEET': 'XLS',
    'JPEG': 'JPG',
    'PLAIN': 'TXT'
  }
  return map[ext] || ext.slice(0, 3)
})

const fileTypeClass = computed(() => {
  if (isPdf.value) return 'type-pdf'
  if (isImage.value) return 'type-image'
  if (isWord.value) return 'type-word'
  if (isExcel.value) return 'type-excel'
  return 'type-default'
})

const categoryClass = computed(() => {
  const cat = props.doc.category?.toLowerCase() || ''
  if (cat.includes('invoice') || cat.includes('facture')) return 'cat-invoice'
  if (cat.includes('contract') || cat.includes('contrat')) return 'cat-contract'
  if (cat.includes('report') || cat.includes('rapport')) return 'cat-report'
  return 'cat-default'
})

const formattedDate = computed(() => {
  if (!props.doc.createdAt) return ''
  return new Date(props.doc.createdAt).toLocaleDateString('fr-FR', { 
    day: '2-digit', 
    month: 'short', 
    year: 'numeric' 
  })
})

const formattedSize = computed(() => {
  const bytes = props.doc.fileSize
  if (!bytes) return ''
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / (1024 * 1024)).toFixed(1) + ' MB'
})
</script>

<style scoped>
.doc-card {
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

/* ── Grid view ── */
.doc-card.view-grid {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 12px;
  overflow: hidden;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  animation: fadeInUp 0.3s ease-out both;
}

.doc-card.view-grid:hover {
  border-color: var(--color-primary);
  box-shadow: 0 8px 25px -5px rgba(37, 99, 235, 0.1);
  transform: translateY(-4px);
}

.card-thumb {
  height: 120px;
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.thumb-preview {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: transform 0.3s ease;
}

.thumb-preview.type-pdf { background: linear-gradient(135deg, #fef2f2, #fecaca); }
.thumb-preview.type-pdf svg { color: #dc2626; }

.thumb-preview.type-image { background: linear-gradient(135deg, #f0fdf4, #bbf7d0); }
.thumb-preview.type-image svg { color: #16a34a; }

.thumb-preview.type-word { background: linear-gradient(135deg, #eff6ff, #bfdbfe); }
.thumb-preview.type-word svg { color: #2563eb; }

.thumb-preview.type-excel { background: linear-gradient(135deg, #f0fdf4, #bbf7d0); }
.thumb-preview.type-excel svg { color: #16a34a; }

.thumb-preview.type-default { background: linear-gradient(135deg, #f9fafb, #f3f4f6); }
.thumb-preview.type-default svg { color: #6b7280; }

.doc-card.view-grid:hover .thumb-preview {
  transform: scale(1.05);
}

.thumb-status {
  position: absolute;
  top: 8px;
  left: 8px;
  display: flex;
  gap: 4px;
}

.status-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  border-radius: 6px;
  font-size: 0.65rem;
  font-weight: 600;
}

.status-ocr {
  background: rgba(34, 197, 94, 0.9);
  color: white;
}

.status-processing {
  background: rgba(59, 130, 246, 0.9);
  color: white;
}

.pulse-dot {
  width: 6px;
  height: 6px;
  background: white;
  border-radius: 50%;
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.5; transform: scale(0.8); }
}

.thumb-type-badge {
  position: absolute;
  bottom: 8px;
  right: 8px;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.6rem;
  font-weight: 700;
  color: white;
}

.thumb-type-badge.type-pdf { background: #dc2626; }
.thumb-type-badge.type-image { background: #16a34a; }
.thumb-type-badge.type-word { background: #2563eb; }
.thumb-type-badge.type-excel { background: #16a34a; }
.thumb-type-badge.type-default { background: #6b7280; }

.card-body {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex: 1;
}

.card-title {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--color-text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.4;
}

.card-meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.category-chip {
  font-size: 0.65rem;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 12px;
  background: var(--color-primary-light);
  color: var(--color-primary);
}

.category-chip.cat-invoice { background: #fef3c7; color: #d97706; }
.category-chip.cat-contract { background: #ede9fe; color: #7c3aed; }
.category-chip.cat-report { background: #dbeafe; color: #2563eb; }

.service-chip {
  font-size: 0.65rem;
  font-weight: 500;
  padding: 2px 8px;
  border-radius: 12px;
  background: var(--color-surface-hover);
  color: var(--color-text-secondary);
}

.card-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: auto;
  padding-top: 8px;
  border-top: 1px solid var(--color-border);
}

.meta-date,
.meta-size {
  font-size: 0.7rem;
  color: var(--color-text-muted);
}

/* ── List view ── */
.doc-card.view-list {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 10px;
  cursor: pointer;
}

.doc-card.view-list:hover {
  background: var(--color-surface-hover);
  border-color: var(--color-primary);
  box-shadow: 0 2px 8px rgba(37, 99, 235, 0.08);
}

.list-thumb {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.list-thumb.type-pdf { background: #fef2f2; }
.list-thumb.type-pdf svg { color: #dc2626; }
.list-thumb.type-image { background: #f0fdf4; }
.list-thumb.type-image svg { color: #16a34a; }
.list-thumb.type-word { background: #eff6ff; }
.list-thumb.type-word svg { color: #2563eb; }
.list-thumb.type-excel { background: #f0fdf4; }
.list-thumb.type-excel svg { color: #16a34a; }
.list-thumb.type-default { background: #f9fafb; }
.list-thumb.type-default svg { color: #6b7280; }

.list-body {
  flex: 1;
  min-width: 0;
}

.list-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  margin-bottom: 4px;
}

.list-meta {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.category-chip.sm {
  font-size: 0.6rem;
  padding: 1px 6px;
}

.meta-text {
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.meta-dot {
  color: var(--color-text-muted);
  font-size: 0.75rem;
}

.ocr-indicator {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  background: var(--color-success);
  color: white;
  border-radius: 50%;
}

.list-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.action-btn {
  width: 32px;
  height: 32px;
  border: 1px solid var(--color-border);
  border-radius: 8px;
  background: var(--color-surface);
  color: var(--color-text-muted);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s ease;
}

.action-btn:hover {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: white;
}

@keyframes fadeInUp {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
