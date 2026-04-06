<template>
  <div class="taxonomy-page">
    <div class="page-header">
      <div>
        <h1 class="page-title">
          Taxonomie des documents
          <span
            class="help-icon"
            title="La taxonomie définit comment vos documents sont classés. Les catégories sont des regroupements de haut niveau (Facture, Contrat...) et les étiquettes sont des labels d'utilisation (urgent, important...). Les éléments système ne peuvent pas être supprimés."
          >ℹ️</span>
        </h1>
        <p class="page-subtitle">
          Gérez les catégories, étiquettes et la structure de classification
        </p>
      </div>
      <button
        class="btn-primary"
        @click="showCreateCategory = true"
      >
        + Nouvelle catégorie
      </button>
    </div>

    <!-- Tabs -->
    <div class="taxonomy-tabs">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        class="tab-btn"
        :class="{ active: activeTab === tab.id }"
        @click="activeTab = tab.id"
      >
        <span>{{ tab.icon }}</span> {{ tab.label }}
      </button>
    </div>

    <!-- ── CATEGORIES TAB ── -->
    <div
      v-if="activeTab === 'categories'"
      class="tab-content"
    >
      <div class="category-grid">
        <div
          v-for="cat in categories"
          :key="cat.id"
          class="category-card"
          :class="{ inactive: !cat.isActive }"
        >
          <div class="cat-header">
            <div
              class="cat-icon"
              :style="{ background: cat.color + '22', color: cat.color }"
            >
              {{ cat.icon }}
            </div>
            <div class="cat-info">
              <h3 class="cat-name">
                {{ cat.name }}
              </h3>
              <p class="cat-desc">
                {{ cat.description || 'Aucune description' }}
              </p>
            </div>
          </div>
          <div class="cat-footer">
            <span class="cat-meta">
              <span
                :title="cat.isSystem ? 'Catégorie système : gérée par le système, ne peut pas être supprimée' : 'Catégorie personnalisée : créée par un administrateur'"
              >{{ cat.isSystem ? '🔒 Système' : 'Personnalisé' }}</span>
              <span
                v-if="categoryTagCounts[cat.name]"
                class="tag-count-badge"
              >#️⃣ {{ categoryTagCounts[cat.name] }}</span>
              <span
                v-if="!cat.isActive"
                class="inactive-badge"
              >Désactivé</span>
            </span>
            <div class="cat-actions">
              <button
                class="btn-icon-sm"
                title="Modifier"
                @click="editCategory(cat)"
              >
                ✏️
              </button>
              <button
                v-if="!cat.isSystem"
                class="btn-icon-sm danger"
                title="Supprimer"
                @click="deleteCategory(cat)"
              >
                🗑️
              </button>
            </div>
          </div>
        </div>

        <button
          class="category-card add-card"
          @click="showCreateCategory = true"
        >
          <div class="add-icon">
            +
          </div>
          <p>Ajouter une catégorie</p>
        </button>
      </div>
    </div>

    <!-- ── TAGS TAB ── -->
    <div
      v-if="activeTab === 'tags'"
      class="tab-content"
    >
      <div class="tags-toolbar">
        <div class="search-input-wrap">
          <svg
            class="search-icon"
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
            v-model="tagSearch"
            type="text"
            placeholder="Rechercher une étiquette…"
            class="search-input-inline"
            @input="debouncedSearchTags"
          >
        </div>
        <select
          v-model="tagCategoryFilter"
          class="filter-select-sm"
          @change="fetchTags"
        >
          <option value="">
            Toutes catégories
          </option>
          <option
            v-for="cat in categories"
            :key="cat.id"
            :value="cat.name"
          >
            {{ cat.icon }} {{ cat.name }}
          </option>
        </select>
        <button
          class="btn-primary btn-sm"
          @click="showCreateTag = true"
        >
          + Nouvelle étiquette
        </button>
        <button
          class="btn-secondary btn-sm"
          @click="showBulkCreateTag = true"
        >
          + Import massif
        </button>
      </div>

      <div class="tags-cloud-list">
        <div
          v-if="tagsLoading"
          class="loading-state"
        >
          <div class="spinner-ring" /> Chargement…
        </div>
        <div
          v-else-if="!tags.length"
          class="empty-state"
        >
          <p>Aucune étiquette trouvée</p>
          <button
            class="btn-primary btn-sm"
            @click="showCreateTag = true"
          >
            Créer la première
          </button>
        </div>
        <div
          v-else
          class="tags-grid"
        >
          <div
            v-for="tag in tags"
            :key="tag.id"
            class="tag-card"
          >
            <div class="tag-header">
              <span
                class="tag-badge"
                :style="{ background: tag.color + '22', color: tag.color }"
              >
                #{{ tag.name }}
              </span>
              <span class="tag-count">{{ tag.usageCount }} utilisation(s)</span>
            </div>
            <p
              v-if="tag.description"
              class="tag-desc"
            >
              {{ tag.description }}
            </p>
            <p
              v-if="tag.category"
              class="tag-category"
            >
              📁 {{ tag.category }}
            </p>
            <div class="tag-footer">
              <span class="tag-meta">
                <span
                  :title="tag.isSystem ? 'Étiquette système : gérée par le système, ne peut pas être supprimée' : 'Étiquette personnalisée : créée par un administrateur'"
                >{{ tag.isSystem ? '🔒 Système' : 'Personnalisé' }}</span>
                <span
                  v-if="!tag.isActive"
                  class="inactive-badge"
                >Désactivée</span>
              </span>
              <div class="tag-actions">
                <button
                  class="btn-icon-xs"
                  title="Modifier"
                  @click="editTag(tag)"
                >
                  ✏️
                </button>
                <button
                  v-if="!tag.isSystem"
                  class="btn-icon-xs danger"
                  title="Supprimer"
                  @click="deleteTag(tag)"
                >
                  🗑️
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Pagination -->
      <div
        v-if="tagsTotalPages > 1"
        class="pagination"
      >
        <button
          v-for="page in tagsTotalPages"
          :key="page"
          class="page-btn"
          :class="{ active: page === tagsPage }"
          @click="tagsPage = page; fetchTags()"
        >
          {{ page }}
        </button>
      </div>
    </div>

    <!-- ── CREATE / EDIT CATEGORY MODAL ── -->
    <div
      v-if="showCreateCategory"
      class="modal-overlay"
      @click.self="closeCategoryModal"
    >
      <div class="modal-content modal-sm">
        <div class="modal-header">
          <h2>{{ editingCategory ? 'Modifier la catégorie' : 'Nouvelle catégorie' }}</h2>
          <button
            class="modal-close"
            @click="closeCategoryModal"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">Nom <span style="color:#ef4444">*</span></label>
            <input
              v-model="categoryForm.name"
              class="form-input"
              placeholder="ex: Facture Fournisseur"
            >
          </div>
          <div class="form-group">
            <label class="form-label">Description</label>
            <input
              v-model="categoryForm.description"
              class="form-input"
              placeholder="Description courte"
            >
          </div>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">Icône (emoji)</label>
              <input
                v-model="categoryForm.icon"
                class="form-input"
                placeholder="📄"
                maxlength="2"
              >
            </div>
            <div class="form-group">
              <label class="form-label">Couleur</label>
              <input
                v-model="categoryForm.color"
                type="color"
                class="form-input color-input"
              >
            </div>
          </div>
          <div
            v-if="editingCategory"
            class="form-group"
          >
            <label class="form-label">
              <input
                v-model="categoryForm.isActive"
                type="checkbox"
              > Active
            </label>
          </div>
        </div>
        <div class="modal-actions">
          <button
            class="cancel-btn"
            @click="closeCategoryModal"
          >
            Annuler
          </button>
          <button
            class="btn-primary"
            @click="saveCategory"
          >
            {{ editingCategory ? 'Enregistrer' : 'Créer' }}
          </button>
        </div>
      </div>
    </div>

    <!-- ── CREATE / EDIT TAG MODAL ── -->
    <div
      v-if="showCreateTag"
      class="modal-overlay"
      @click.self="closeTagModal"
    >
      <div class="modal-content modal-sm">
        <div class="modal-header">
          <h2>{{ editingTag ? 'Modifier l\'étiquette' : 'Nouvelle étiquette' }}</h2>
          <button
            class="modal-close"
            @click="closeTagModal"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">Nom <span style="color:#ef4444">*</span></label>
            <input
              v-model="tagForm.name"
              class="form-input"
              placeholder="ex: urgent, contrat-cadre"
            >
            <small class="form-hint">Minuscules, traits d'union autorisés</small>
          </div>
          <div class="form-group">
            <label class="form-label">Description</label>
            <input
              v-model="tagForm.description"
              class="form-input"
              placeholder="Description courte"
            >
          </div>
          <div class="form-group">
            <label class="form-label">Catégorie</label>
            <select
              v-model="tagForm.category"
              class="form-select"
            >
              <option value="">
                — Aucune —
              </option>
              <option
                v-for="cat in categories"
                :key="cat.id"
                :value="cat.name"
              >
                {{ cat.icon }} {{ cat.name }}
              </option>
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">Couleur</label>
            <input
              v-model="tagForm.color"
              type="color"
              class="form-input color-input"
            >
          </div>
          <div
            v-if="editingTag"
            class="form-group"
          >
            <label class="form-label">
              <input
                v-model="tagForm.isActive"
                type="checkbox"
              > Active
            </label>
          </div>
        </div>
        <div class="modal-actions">
          <button
            class="cancel-btn"
            @click="closeTagModal"
          >
            Annuler
          </button>
          <button
            class="btn-primary"
            @click="saveTag"
          >
            {{ editingTag ? 'Enregistrer' : 'Créer' }}
          </button>
        </div>
      </div>
    </div>

    <!-- ── BULK CREATE TAGS MODAL ── -->
    <div
      v-if="showBulkCreateTag"
      class="modal-overlay"
      @click.self="showBulkCreateTag = false"
    >
      <div class="modal-content modal-sm">
        <div class="modal-header">
          <h2>Import massif d'étiquettes</h2>
          <button
            class="modal-close"
            @click="showBulkCreateTag = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">Étiquettes (une par ligne)</label>
            <textarea
              v-model="bulkTagNames"
              class="form-textarea"
              placeholder="urgent&#10;confidentiel&#10;contrat-fournisseur&#10;…"
              rows="10"
            />
            <small class="form-hint">{{ bulkTagNames.split('\n').filter(Boolean).length }} étiquettes</small>
          </div>
          <div class="form-group">
            <label class="form-label">Catégorie</label>
            <select
              v-model="bulkTagCategory"
              class="form-select"
            >
              <option value="">
                — Aucune —
              </option>
              <option
                v-for="cat in categories"
                :key="cat.id"
                :value="cat.name"
              >
                {{ cat.icon }} {{ cat.name }}
              </option>
            </select>
          </div>
        </div>
        <div class="modal-actions">
          <button
            class="cancel-btn"
            @click="showBulkCreateTag = false"
          >
            Annuler
          </button>
          <button
            class="btn-primary"
            @click="saveBulkTags"
          >
            Créer {{ bulkTagNames.split('\n').filter(Boolean).length }} étiquettes
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { apiFetch } from '@/api.js'

const tabs = [
  { id: 'categories', label: 'Catégories', icon: '📁' },
  { id: 'tags', label: 'Étiquettes', icon: '#️⃣' }
]

const activeTab = ref('categories')

// ── Categories ────────────────────────────────────────────────────────────────
const categories = ref([])
const showCreateCategory = ref(false)
const editingCategory = ref(null)
const categoryForm = ref({ name: '', description: '', icon: '📁', color: '#6366f1', isActive: true })

const fetchCategories = async () => {
  try {
    const data = await apiFetch('/api/taxonomy/categories')
    categories.value = data || []
  } catch (e) { console.error('[Taxonomy] fetchCategories:', e) }
}

const categoryTagCounts = computed(() => {
  const counts = {}
  tags.value.forEach(t => {
    if (t.category) {
      counts[t.category] = (counts[t.category] || 0) + 1
    }
  })
  return counts
})

const saveCategory = async () => {
  if (!categoryForm.value.name?.trim()) return
  try {
    if (editingCategory.value) {
      await apiFetch(`/api/taxonomy/categories/${editingCategory.value.id}`, {
        method: 'PUT',
        body: JSON.stringify(categoryForm.value)
      })
    } else {
      await apiFetch('/api/taxonomy/categories', {
        method: 'POST',
        body: JSON.stringify(categoryForm.value)
      })
    }
    await fetchCategories()
    closeCategoryModal()
  } catch (e) { console.error('[Taxonomy] saveCategory:', e) }
}

const editCategory = (cat) => {
  editingCategory.value = cat
  categoryForm.value = { ...cat }
  showCreateCategory.value = true
}

const deleteCategory = async (cat) => {
  if (!confirm(`Supprimer la catégorie "${cat.name}" ?`)) return
  try {
    await apiFetch(`/api/taxonomy/categories/${cat.id}`, { method: 'DELETE' })
    await fetchCategories()
  } catch (e) { console.error('[Taxonomy] deleteCategory:', e) }
}

const closeCategoryModal = () => {
  showCreateCategory.value = false
  editingCategory.value = null
  categoryForm.value = { name: '', description: '', icon: '📁', color: '#6366f1', isActive: true }
}

// ── Tags ───────────────────────────────────────────────────────────────────────
const tags = ref([])
const tagsLoading = ref(false)
const tagSearch = ref('')
const tagCategoryFilter = ref('')
const tagsPage = ref(1)
const tagsTotalPages = ref(1)
const showCreateTag = ref(false)
const showBulkCreateTag = ref(false)
const editingTag = ref(null)
const tagForm = ref({ name: '', description: '', category: '', color: '#64748b', isActive: true })
const bulkTagNames = ref('')
const bulkTagCategory = ref('')

let _tagSearchTimer = null
const debouncedSearchTags = () => {
  clearTimeout(_tagSearchTimer)
  _tagSearchTimer = setTimeout(() => { tagsPage.value = 1; fetchTags() }, 300)
}

const fetchTags = async () => {
  tagsLoading.value = true
  try {
    const params = new URLSearchParams({
      page: tagsPage.value,
      pageSize: 50,
      ...(tagSearch.value && { search: tagSearch.value }),
      ...(tagCategoryFilter.value && { category: tagCategoryFilter.value })
    })
    const data = await apiFetch(`/api/taxonomy/tags?${params}`)
    if (data) {
      tags.value = data.tags || []
      tagsTotalPages.value = data.totalPages || 1
    }
  } catch (e) { console.error('[Taxonomy] fetchTags:', e) }
  finally { tagsLoading.value = false }
}

const saveTag = async () => {
  if (!tagForm.value.name?.trim()) return
  try {
    if (editingTag.value) {
      await apiFetch(`/api/taxonomy/tags/${editingTag.value.id}`, {
        method: 'PUT',
        body: JSON.stringify(tagForm.value)
      })
    } else {
      await apiFetch('/api/taxonomy/tags', {
        method: 'POST',
        body: JSON.stringify(tagForm.value)
      })
    }
    await fetchTags()
    closeTagModal()
  } catch (e) { console.error('[Taxonomy] saveTag:', e) }
}

const saveBulkTags = async () => {
  const names = bulkTagNames.value.split('\n').map(n => n.trim()).filter(Boolean)
  if (!names.length) return
  try {
    await apiFetch('/api/taxonomy/tags/bulk', {
      method: 'POST',
      body: JSON.stringify({ names, category: bulkTagCategory.value || undefined })
    })
    await fetchTags()
    showBulkCreateTag.value = false
    bulkTagNames.value = ''
    bulkTagCategory.value = ''
  } catch (e) { console.error('[Taxonomy] saveBulkTags:', e) }
}

const editTag = (tag) => {
  editingTag.value = tag
  tagForm.value = { ...tag }
  showCreateTag.value = true
}

const deleteTag = async (tag) => {
  if (!confirm(`Supprimer l'étiquette "#${tag.name}" ?`)) return
  try {
    await apiFetch(`/api/taxonomy/tags/${tag.id}`, { method: 'DELETE' })
    await fetchTags()
  } catch (e) { console.error('[Taxonomy] deleteTag:', e) }
}

const closeTagModal = () => {
  showCreateTag.value = false
  editingTag.value = null
  tagForm.value = { name: '', description: '', category: '', color: '#64748b', isActive: true }
}

onMounted(() => {
  fetchCategories()
  fetchTags()
})
</script>

<style scoped>
.taxonomy-page { padding: 1.5rem; }
.page-title { display: flex; align-items: center; gap: 0.5rem; }
.help-icon { cursor: help; font-size: 1rem; opacity: 0.6; }
.help-icon:hover { opacity: 1; }
.taxonomy-tabs { display: flex; gap: 0.5rem; margin-bottom: 1.5rem; border-bottom: 1px solid #e5e7eb; padding-bottom: 0.5rem; }
.tab-btn { padding: 0.5rem 1rem; border: none; background: none; cursor: pointer; border-radius: 6px; font-size: 0.9rem; color: #6b7280; transition: all 0.15s; }
.tab-btn:hover { background: #f3f4f6; color: #374151; }
.tab-btn.active { background: #eff6ff; color: #2563eb; font-weight: 600; }
.category-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 1rem; }
.category-card, .tag-card { background: white; border: 1px solid #e5e7eb; border-radius: 10px; padding: 1rem; transition: box-shadow 0.15s; }
.category-card:hover, .tag-card:hover { box-shadow: 0 4px 12px rgba(0,0,0,0.08); }
.category-card.inactive { opacity: 0.6; }
.add-card { display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 120px; border-style: dashed; cursor: pointer; color: #9ca3af; }
.add-card:hover { border-color: #2563eb; color: #2563eb; background: #eff6ff; }
.add-icon { font-size: 2rem; margin-bottom: 0.25rem; }
.cat-header { display: flex; gap: 0.75rem; align-items: flex-start; margin-bottom: 0.75rem; }
.cat-icon { width: 48px; height: 48px; border-radius: 10px; display: flex; align-items: center; justify-content: center; font-size: 1.5rem; flex-shrink: 0; }
.cat-name { font-weight: 600; font-size: 1rem; margin: 0; }
.cat-desc { font-size: 0.8rem; color: #6b7280; margin: 0.2rem 0 0; }
.cat-footer { display: flex; justify-content: space-between; align-items: center; padding-top: 0.75rem; border-top: 1px solid #f3f4f6; }
.cat-meta, .tag-meta { font-size: 0.75rem; color: #9ca3af; }
.inactive-badge { background: #fef3c7; color: #d97706; padding: 0.1rem 0.4rem; border-radius: 4px; margin-left: 0.25rem; font-size: 0.7rem; }
.tag-count-badge { background: #eff6ff; color: #2563eb; padding: 0.1rem 0.4rem; border-radius: 4px; margin-left: 0.5rem; font-size: 0.7rem; }
.cat-actions, .tag-actions { display: flex; gap: 0.25rem; }
.tags-toolbar { display: flex; gap: 0.75rem; align-items: center; margin-bottom: 1rem; flex-wrap: wrap; }
.search-input-wrap { position: relative; flex: 1; min-width: 200px; }
.search-icon { position: absolute; left: 10px; top: 50%; transform: translateY(-50%); width: 16px; height: 16px; color: #9ca3af; }
.search-input-inline { width: 100%; padding: 0.5rem 0.75rem 0.5rem 2rem; border: 1px solid #e5e7eb; border-radius: 6px; font-size: 0.9rem; }
.filter-select-sm { padding: 0.5rem 0.75rem; border: 1px solid #e5e7eb; border-radius: 6px; font-size: 0.85rem; }
.btn-sm { padding: 0.4rem 0.75rem; font-size: 0.85rem; }
.tags-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 0.75rem; }
.tag-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem; }
.tag-badge { padding: 0.25rem 0.6rem; border-radius: 6px; font-size: 0.85rem; font-weight: 600; }
.tag-count { font-size: 0.75rem; color: #9ca3af; }
.tag-desc { font-size: 0.8rem; color: #6b7280; margin: 0.25rem 0; }
.tag-category { font-size: 0.75rem; color: #9ca3af; margin: 0.25rem 0 0; }
.tag-footer { display: flex; justify-content: space-between; align-items: center; padding-top: 0.5rem; border-top: 1px solid #f3f4f6; margin-top: 0.5rem; }
.modal-sm { max-width: 480px; }
.form-group { margin-bottom: 1rem; }
.form-label { display: block; font-size: 0.85rem; font-weight: 600; margin-bottom: 0.3rem; color: #374151; }
.form-input, .form-select, .form-textarea { width: 100%; padding: 0.5rem 0.75rem; border: 1px solid #e5e7eb; border-radius: 6px; font-size: 0.9rem; }
.form-textarea { resize: vertical; }
.color-input { height: 38px; padding: 2px; cursor: pointer; }
.form-hint { font-size: 0.75rem; color: #9ca3af; margin-top: 0.2rem; display: block; }
.form-row { display: flex; gap: 0.75rem; }
.form-row .form-group { flex: 1; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 1000; }
.modal-content { background: white; border-radius: 12px; width: 90%; max-height: 90vh; overflow-y: auto; }
.modal-header { display: flex; justify-content: space-between; align-items: center; padding: 1.25rem 1.5rem; border-bottom: 1px solid #e5e7eb; }
.modal-header h2 { margin: 0; font-size: 1.1rem; }
.modal-close { background: none; border: none; font-size: 1.2rem; cursor: pointer; color: #9ca3af; padding: 0; }
.modal-body { padding: 1.25rem 1.5rem; }
.modal-actions { display: flex; justify-content: flex-end; gap: 0.75rem; padding: 1rem 1.5rem; border-top: 1px solid #e5e7eb; }
.btn-primary { background: #2563eb; color: white; border: none; border-radius: 6px; padding: 0.5rem 1rem; font-size: 0.9rem; cursor: pointer; font-weight: 600; }
.btn-primary:hover { background: #1d4ed8; }
.btn-primary:disabled { background: #9ca3af; cursor: not-allowed; }
.btn-secondary { background: white; color: #374151; border: 1px solid #e5e7eb; border-radius: 6px; padding: 0.5rem 1rem; font-size: 0.9rem; cursor: pointer; }
.btn-secondary:hover { background: #f9fafb; }
.cancel-btn { background: white; color: #6b7280; border: 1px solid #e5e7eb; border-radius: 6px; padding: 0.5rem 1rem; font-size: 0.9rem; cursor: pointer; }
.btn-icon-sm { background: none; border: none; cursor: pointer; font-size: 1rem; padding: 2px; }
.btn-icon-xs { background: none; border: none; cursor: pointer; font-size: 0.9rem; padding: 2px; }
.danger { color: #ef4444; }
.spinner-ring { display: inline-block; width: 20px; height: 20px; border: 2px solid #e5e7eb; border-top-color: #2563eb; border-radius: 50%; animation: spin 0.8s linear infinite; margin-right: 0.5rem; }
@keyframes spin { to { transform: rotate(360deg); } }
.loading-state, .empty-state { display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 3rem; gap: 0.5rem; color: #9ca3af; font-size: 0.9rem; }
.pagination { display: flex; justify-content: center; gap: 0.25rem; margin-top: 1.5rem; }
.page-btn { padding: 0.35rem 0.7rem; border: 1px solid #e5e7eb; border-radius: 4px; background: white; cursor: pointer; font-size: 0.85rem; }
.page-btn.active { background: #2563eb; color: white; border-color: #2563eb; }
</style>
