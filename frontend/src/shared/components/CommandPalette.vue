<template>
  <Teleport to="body">
    <Transition name="command-palette">
      <div
        v-if="isOpen"
        class="cp-overlay"
        @click.self="close"
        @keydown.escape="close"
      >
        <div
          class="cp-dialog"
          role="dialog"
          aria-modal="true"
          aria-label="Recherche globale"
        >
          <!-- Search input -->
          <div class="cp-search-row">
            <svg
              class="cp-search-icon"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
              />
            </svg>
            <input
              ref="inputRef"
              v-model="query"
              type="text"
              class="cp-input"
              placeholder="Rechercher des documents, naviguer…"
              @keydown.down.prevent="selectResult(1)"
              @keydown.up.prevent="selectResult(-1)"
              @keydown.enter.prevent="executeSelected"
              @keydown.escape="close"
            >
            <kbd class="cp-kbd-hint">ESC</kbd>
          </div>

          <!-- Quick actions -->
          <div
            v-if="!query"
            class="cp-section"
          >
            <div class="cp-section-title">
              Actions rapides
            </div>
            <div class="cp-quick-actions">
              <button
                v-for="(action, i) in quickActions"
                :key="action.id"
                class="cp-action-item"
                :class="{ selected: selectedIndex === i }"
                @click="runAction(action)"
                @mouseenter="selectedIndex = i"
              >
                <span
                  class="cp-action-icon"
                  :style="{ background: action.iconBg, color: action.iconColor }"
                >
                  <span v-html="action.icon" />
                </span>
                <div class="cp-action-content">
                  <span class="cp-action-label">{{ action.label }}</span>
                  <span class="cp-action-desc">{{ action.description }}</span>
                </div>
                <kbd
                  v-if="action.shortcut"
                  class="cp-action-kbd"
                >{{ action.shortcut }}</kbd>
              </button>
            </div>
          </div>

          <!-- Search results -->
          <div
            v-else
            class="cp-section"
          >
            <div
              v-if="isLoading"
              class="cp-loading"
            >
              <svg
                class="spinner"
                width="18"
                height="18"
                fill="none"
                viewBox="0 0 24 24"
              >
                <circle
                  cx="12"
                  cy="12"
                  r="10"
                  stroke="currentColor"
                  stroke-width="4"
                  opacity="0.25"
                />
                <path
                  fill="currentColor"
                  opacity="0.75"
                  d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                />
              </svg>
              Recherche en cours…
            </div>

            <div
              v-else-if="results.length"
              class="cp-results"
            >
              <div class="cp-section-title">
                {{ results.length }} résultat(s)
              </div>
              <button
                v-for="(result, i) in results"
                :key="result.id"
                class="cp-result-item"
                :class="{ selected: selectedIndex === i }"
                @click="openResult(result)"
                @mouseenter="selectedIndex = i"
              >
                <div
                  class="cp-result-icon"
                  :class="getFileIconClass(result.contentType)"
                >
                  <svg
                    v-if="isPdf(result.contentType)"
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
                <div class="cp-result-body">
                  <span class="cp-result-title">{{ result.title }}</span>
                  <span class="cp-result-meta">
                    {{ result.category || '—' }}
                    <span v-if="result.createdAt"> · {{ formatDate(result.createdAt) }}</span>
                    <span v-if="result.fileSize"> · {{ formatSize(result.fileSize) }}</span>
                  </span>
                </div>
                <div
                  v-if="result.isOcrProcessed"
                  class="cp-result-badge"
                >
                  OCR
                </div>
                <svg
                  class="cp-result-arrow"
                  width="16"
                  height="16"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M9 18l6-6-6-6"
                  />
                </svg>
              </button>
            </div>

            <div
              v-else
              class="cp-empty"
            >
              <svg
                width="48"
                height="48"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
              >
                <circle
                  stroke-width="1.5"
                  d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                />
              </svg>
              <p>Aucun résultat pour <strong>"{{ query }}"</strong></p>
              <span class="cp-empty-hint">Essayez avec d'autres termes</span>
            </div>
          </div>

          <!-- Footer -->
          <div class="cp-footer">
            <span class="cp-footer-hint">
              <kbd>↑</kbd><kbd>↓</kbd> naviguer
              <kbd>↵</kbd> sélectionner
              <kbd>ESC</kbd> fermer
            </span>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup>
import { ref, computed, watch, nextTick } from 'vue'

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:modelValue', 'navigate', 'open-upload', 'open-rag'])

const inputRef = ref(null)
const query = ref('')
const selectedIndex = ref(0)
const isLoading = ref(false)
const results = ref([])

const isOpen = computed({
  get: () => props.modelValue,
  set: (val) => emit('update:modelValue', val)
})

watch(isOpen, async (val) => {
  if (val) {
    query.value = ''
    results.value = []
    selectedIndex.value = 0
    await nextTick()
    inputRef.value?.focus()
    document.body.style.overflow = 'hidden'
  } else {
    document.body.style.overflow = ''
  }
})

watch(query, async (q) => {
  if (!q.trim()) {
    results.value = []
    isLoading.value = false
    return
  }
  isLoading.value = true
  try {
    const res = await fetch('/api/documents/search', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ query: q, page: 1, pageSize: 8 })
    })
    if (res.ok) {
      const data = await res.json()
      results.value = (data.results || []).slice(0, 8)
    }
  } catch {
    results.value = []
  } finally {
    isLoading.value = false
    selectedIndex.value = 0
  }
})

const quickActions = [
  {
    id: 'search',
    label: 'Recherche avancée',
    description: 'Rechercher dans tous les documents',
    icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/></svg>',
    iconBg: 'var(--color-primary-light)',
    iconColor: 'var(--color-primary)',
    action: () => { close(); emit('navigate', 'search') }
  },
  {
    id: 'upload',
    label: 'Importer des documents',
    description: 'Téléverser de nouveaux fichiers',
    icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/></svg>',
    iconBg: 'var(--color-success-light)',
    iconColor: 'var(--color-success)',
    shortcut: '⌘U',
    action: () => { close(); emit('open-upload') }
  },
  {
    id: 'rag',
    label: 'Assistant Elise',
    description: 'Poser une question à Elise sur vos documents',
    icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"/></svg>',
    iconBg: '#f5f3ff',
    iconColor: '#7c3aed',
    shortcut: '⌘K',
    action: () => { close(); emit('open-rag') }
  }
]

function selectResult(dir) {
  const len = query.value ? results.value.length : quickActions.length
  selectedIndex.value = (selectedIndex.value + dir + len) % len
}

function executeSelected() {
  if (query.value) {
    const result = results.value[selectedIndex.value]
    if (result) openResult(result)
  } else {
    const action = quickActions[selectedIndex.value]
    if (action) runAction(action)
  }
}

function runAction(action) {
  close()
  setTimeout(() => action.action(), 100)
}

function openResult(result) {
  close()
  emit('navigate', 'search', result)
}

function close() {
  isOpen.value = false
}

const isPdf = (ct) => ct?.includes('pdf')

function getFileIconClass(ct) {
  if (isPdf(ct)) return 'icon-pdf'
  if (ct?.includes('image')) return 'icon-image'
  if (ct?.includes('word')) return 'icon-word'
  if (ct?.includes('excel')) return 'icon-excel'
  return 'icon-default'
}

function formatDate(dateStr) {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', year: 'numeric' })
}

function formatSize(bytes) {
  if (!bytes) return ''
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / (1024 * 1024)).toFixed(1) + ' MB'
}
</script>

<style scoped>
.cp-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(8px);
  z-index: 9999;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 10vh;
}

.cp-dialog {
  width: 100%;
  max-width: 640px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
  overflow: hidden;
  max-height: 75vh;
  display: flex;
  flex-direction: column;
}

.cp-search-row {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--color-border);
}

.cp-search-icon {
  width: 22px;
  height: 22px;
  color: var(--color-text-muted);
  flex-shrink: 0;
}

.cp-input {
  flex: 1;
  border: none;
  outline: none;
  background: transparent;
  font-size: 1rem;
  color: var(--color-text-primary);
  font-family: inherit;
}

.cp-input::placeholder {
  color: var(--color-text-muted);
}

.cp-kbd-hint {
  font-family: inherit;
  font-size: 0.7rem;
  background: var(--color-surface-hover);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.2rem 0.5rem;
  color: var(--color-text-muted);
}

.cp-section {
  flex: 1;
  overflow-y: auto;
  padding: 0.5rem;
}

.cp-section-title {
  font-size: 0.7rem;
  font-weight: 700;
  color: var(--color-text-muted);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  padding: 0.75rem 0.75rem 0.5rem;
}

.cp-quick-actions {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.cp-action-item {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  padding: 0.75rem;
  border-radius: 10px;
  border: none;
  background: none;
  cursor: pointer;
  transition: background 0.15s ease;
  text-align: left;
  width: 100%;
}

.cp-action-item:hover,
.cp-action-item.selected {
  background: var(--color-surface-hover);
}

.cp-action-icon {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.cp-action-icon :deep(svg) {
  width: 20px;
  height: 20px;
}

.cp-action-content {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.cp-action-label {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--color-text-primary);
}

.cp-action-desc {
  font-size: 0.78rem;
  color: var(--color-text-muted);
}

.cp-action-kbd {
  font-family: inherit;
  font-size: 0.7rem;
  background: var(--color-surface-hover);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.2rem 0.5rem;
  color: var(--color-text-muted);
  margin-left: auto;
  flex-shrink: 0;
}

.cp-results {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.cp-result-item {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  padding: 0.75rem;
  border-radius: 10px;
  border: none;
  background: none;
  cursor: pointer;
  transition: background 0.15s ease;
  text-align: left;
  width: 100%;
}

.cp-result-item:hover,
.cp-result-item.selected {
  background: var(--color-surface-hover);
}

.cp-result-icon {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.cp-result-icon.icon-pdf { background: var(--color-pdf-bg); color: var(--color-pdf); }
.cp-result-icon.icon-image { background: var(--color-image-bg); color: var(--color-image); }
.cp-result-icon.icon-word { background: var(--color-word-bg); color: var(--color-word); }
.cp-result-icon.icon-excel { background: var(--color-excel-bg); color: var(--color-excel); }
.cp-result-icon.icon-default { background: var(--color-default-bg); color: var(--color-default); }

.cp-result-body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.cp-result-title {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--color-text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cp-result-meta {
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.cp-result-badge {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 0.15rem 0.4rem;
  background: var(--color-success-light);
  color: var(--color-success);
  border-radius: 4px;
}

.cp-result-arrow {
  color: var(--color-text-muted);
  flex-shrink: 0;
  opacity: 0;
  transition: opacity 0.15s ease;
}

.cp-result-item.selected .cp-result-arrow,
.cp-result-item:hover .cp-result-arrow {
  opacity: 1;
}

.cp-loading {
  display: flex;
  align-items: center;
  gap: 0.875rem;
  justify-content: center;
  padding: 2.5rem;
  color: var(--color-text-muted);
  font-size: 0.9rem;
}

.spinner {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.cp-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 3.5rem 1.5rem;
  color: var(--color-text-muted);
  text-align: center;
}

.cp-empty svg {
  opacity: 0.4;
}

.cp-empty p {
  font-size: 0.95rem;
  margin: 0;
}

.cp-empty strong {
  color: var(--color-text-primary);
}

.cp-empty-hint {
  font-size: 0.8rem;
  opacity: 0.7;
}

.cp-footer {
  border-top: 1px solid var(--color-border);
  padding: 0.75rem 1.25rem;
  display: flex;
  align-items: center;
}

.cp-footer-hint {
  font-size: 0.72rem;
  color: var(--color-text-muted);
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.cp-footer-hint kbd {
  font-family: inherit;
  font-size: 0.68rem;
  background: var(--color-surface-hover);
  border: 1px solid var(--color-border);
  border-radius: 4px;
  padding: 0.1rem 0.35rem;
}

/* Transition */
.command-palette-enter-active {
  transition: opacity 0.2s ease;
}

.command-palette-leave-active {
  transition: opacity 0.15s ease;
}

.command-palette-enter-from,
.command-palette-leave-to {
  opacity: 0;
}

.command-palette-enter-active .cp-dialog {
  animation: scaleIn 0.25s cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes scaleIn {
  from {
    opacity: 0;
    transform: scale(0.96) translateY(-12px);
  }
  to {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}
</style>
