<template>
  <div class="admin-layout">
    <!-- Sidebar -->
    <aside class="sidebar">
      <div class="sidebar-brand">
        <div class="brand-icon">
          <svg
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
            />
          </svg>
        </div>
        <span class="brand-name">GED Admin</span>
      </div>

      <nav class="sidebar-nav">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          class="nav-item"
          :class="{ active: activeTab === tab.id }"
          @click="activeTab = tab.id"
        >
          <span
            class="nav-icon"
            v-html="tab.icon"
          />
          <span>{{ tab.label }}</span>
        </button>
      </nav>

      <div class="sidebar-footer">
        <div class="admin-badge">
          <div class="admin-avatar">
            {{ userInitials }}
          </div>
          <div class="admin-info">
            <p class="admin-name">
              {{ user?.fullName || user?.username }}
            </p>
            <p class="admin-role-tag">
              Administrateur
            </p>
          </div>
        </div>
        <button
          class="logout-btn"
          title="Se déconnecter"
          @click="logout"
        >
          <svg
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1"
            />
          </svg>
        </button>
      </div>
    </aside>

    <!-- Main content -->
    <main class="admin-main">
      <!-- ── DOCUMENTS TAB ──────────────────────────────────────────────── -->
      <section
        v-if="activeTab === 'documents'"
        class="search-section"
      >
        <AppBreadcrumb :crumbs="documentsBreadcrumbs" />
        <div class="page-header">
          <div>
            <h1 class="page-title">
              Gestion des documents
            </h1>
            <p class="page-subtitle">
              {{ totalResults !== null ? totalResults + ' résultat(s)' : documents.length + ' document(s) dans le système' }}
            </p>
          </div>
        </div>

        <!-- ── Smart Search bar (mirrors User.vue) ── -->
        <div class="search-card">
          <div class="search-bar-wrapper">
            <div
              class="search-input-wrapper"
              style="position:relative"
            >
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
                v-model="docSearch"
                type="text"
                placeholder="Recherche en langage naturel… ex : « contrats 2024 »"
                class="search-input"
                @keyup.enter="handleSearch"
                @input="onSearchInput"
                @keydown.down.prevent="selectSuggestion(1)"
                @keydown.up.prevent="selectSuggestion(-1)"
                @keydown.escape="showAutocomplete = false"
                @blur="onSearchBlur"
              >
              <div
                v-if="showAutocomplete && autocompleteSuggestions.length"
                class="autocomplete-dropdown"
              >
                <div
                  v-for="(sug, i) in autocompleteSuggestions"
                  :key="i"
                  class="autocomplete-item"
                  :class="{ 'ac-active': i === selectedAcIndex }"
                  @mousedown.prevent="applyAutocomplete(sug)"
                >
                  <svg
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    style="width:13px;height:13px;color:#9ca3af;flex-shrink:0"
                  >
                    <path
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      stroke-width="2"
                      d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
                    />
                  </svg>
                  {{ sug }}
                </div>
              </div>
            </div>
            <button
              class="elise-attach-btn"
              :class="{ 'has-attachments': attachedDocIds.length > 0 }"
              @click="openDocPicker"
            >
              📎 {{ attachedDocIds.length ? attachedDocIds.length + ' joint(s)' : 'Joindre' }}
            </button>

            <!-- Doc picker modal (teleported to body) -->
            <teleport to="body">
              <div
                v-if="showDocPicker"
                class="picker-overlay"
                @click.self="showDocPicker = false"
              >
                <div class="picker-modal">
                  <div class="picker-modal-header">
                    <h3 class="picker-modal-title">
                      📎 Joindre des documents à Elise
                    </h3>
                    <button
                      class="picker-modal-close"
                      @click="showDocPicker = false"
                    >
                      ✕
                    </button>
                  </div>

                  <div class="picker-modal-search">
                    <svg
                      fill="none"
                      stroke="currentColor"
                      viewBox="0 0 24 24"
                      style="width:16px;height:16px;color:#9ca3af;flex-shrink:0"
                    >
                      <path
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        stroke-width="2"
                        d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
                      />
                    </svg>
                    <input
                      v-model="pickerSearch"
                      type="text"
                      placeholder="Rechercher un document…"
                      class="picker-modal-search-input"
                    >
                    <button
                      v-if="pickerSearch"
                      class="picker-modal-search-clear"
                      @click="pickerSearch = ''"
                    >
                      ✕
                    </button>
                  </div>

                  <div class="picker-modal-body">
                    <div
                      v-if="pickerLoading"
                      class="picker-modal-loading"
                    >
                      <svg
                        class="spinner"
                        style="width:20px;height:20px"
                        fill="none"
                        viewBox="0 0 24 24"
                      >
                        <circle
                          class="spinner-bg"
                          cx="12"
                          cy="12"
                          r="10"
                          stroke="currentColor"
                          stroke-width="4"
                        />
                        <path
                          class="spinner-path"
                          fill="currentColor"
                          d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                        />
                      </svg>
                      Chargement…
                    </div>
                    <div
                      v-else-if="!filteredPickerDocs.length"
                      class="picker-modal-empty"
                    >
                      Aucun document trouvé
                    </div>
                    <label
                      v-for="doc in filteredPickerDocs"
                      v-else
                      :key="doc.id"
                      class="picker-modal-item"
                      :class="{ selected: attachedDocIds.includes(doc.id) }"
                    >
                      <input
                        v-model="attachedDocIds"
                        type="checkbox"
                        :value="doc.id"
                        style="display:none"
                      >
                      <span class="picker-modal-icon">{{ getFileIcon(doc.contentType) }}</span>
                      <div class="picker-modal-info">
                        <span class="picker-modal-name">{{ doc.title }}</span>
                        <span class="picker-modal-meta">{{ doc.category || '—' }}</span>
                      </div>
                      <span
                        v-if="attachedDocIds.includes(doc.id)"
                        class="picker-modal-check"
                      >✓</span>
                    </label>
                  </div>

                  <div class="picker-modal-footer">
                    <span class="picker-modal-count">{{ attachedDocIds.length }} sélectionné(s)</span>
                    <button
                      class="picker-modal-clear-btn"
                      @click="attachedDocIds = []"
                    >
                      Tout désélectionner
                    </button>
                    <button
                      class="picker-modal-confirm-btn"
                      @click="confirmAttachments"
                    >
                      Confirmer
                    </button>
                  </div>
                </div>
              </div>
            </teleport>
            <button
              :disabled="searchLoading"
              class="search-btn"
              @click="handleSearch"
            >
              <span v-if="!searchLoading">Rechercher</span>
              <span
                v-else
                class="loading-text"
              >
                <svg
                  class="spinner"
                  fill="none"
                  viewBox="0 0 24 24"
                >
                  <circle
                    class="spinner-bg"
                    cx="12"
                    cy="12"
                    r="10"
                    stroke="currentColor"
                    stroke-width="4"
                  />
                  <path
                    class="spinner-path"
                    fill="currentColor"
                    d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                  />
                </svg>
                Recherche…
              </span>
            </button>
            <button
              class="upload-btn"
              @click="showUpload = true"
            >
              <svg
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"
                />
              </svg>
              Importer
            </button>
          </div>

          <!-- NLP interpretation banner -->
          <div
            v-if="nlpInterpretation"
            class="nlp-banner"
          >
            <span class="nlp-icon">🧠</span>
            <span class="nlp-text">Compris comme : <strong>{{ nlpInterpretation }}</strong></span>
            <button
              class="nlp-dismiss"
              @click="nlpInterpretation = null"
            >
              ✕
            </button>
          </div>

          <!-- Show when query is not understood -->
          <div
            v-if="searchError"
            class="search-error-banner"
          >
            {{ searchError }}
          </div>

          <div class="quick-searches">
            <span class="quick-label">Essayez :</span>
            <button
              v-for="s in quickSearches"
              :key="s"
              class="quick-btn"
              @click="docSearch = s; handleSearch()"
            >
              {{ s }}
            </button>
          </div>

          <button
            class="filters-toggle"
            @click="showFilters = !showFilters"
          >
            <svg
              class="toggle-icon"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4"
              />
            </svg>
            {{ showFilters ? 'Masquer' : 'Afficher' }} les filtres avancés
          </button>

          <div
            v-if="showFilters"
            class="filters-panel"
          >
            <div class="filters-grid">
              <div class="filter-group">
                <label class="filter-label">Catégorie</label>
                <select
                  v-model="filters.category"
                  class="filter-select"
                >
                  <option value="">
                    Toutes
                  </option>
                  <option value="Invoice">
                    📄 Facture
                  </option>
                  <option value="Contract">
                    📜 Contrat
                  </option>
                  <option value="Report">
                    📊 Rapport
                  </option>
                  <option value="Letter">
                    ✉️ Courrier
                  </option>
                  <option value="Memo">
                    📝 Mémo
                  </option>
                  <option value="Presentation">
                    📽️ Présentation
                  </option>
                  <option value="Spreadsheet">
                    📈 Tableur
                  </option>
                  <option value="Image">
                    🖼️ Image
                  </option>
                  <option value="Other">
                    📎 Autre
                  </option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">Type de fichier</label>
                <select
                  v-model="filters.contentType"
                  class="filter-select"
                >
                  <option value="">
                    Tous
                  </option>
                  <option value="application/pdf">
                    📄 PDF
                  </option>
                  <option value="application/vnd.openxmlformats-officedocument.wordprocessingml.document">
                    📝 Word
                  </option>
                  <option value="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet">
                    📊 Excel
                  </option>
                  <option value="text/plain">
                    📃 Texte brut
                  </option>
                  <option value="image/jpeg">
                    🖼️ JPEG
                  </option>
                  <option value="image/png">
                    🖼️ PNG
                  </option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">Date début</label>
                <input
                  v-model="filters.dateFrom"
                  type="date"
                  class="filter-input"
                >
              </div>
              <div class="filter-group">
                <label class="filter-label">Date fin</label>
                <input
                  v-model="filters.dateTo"
                  type="date"
                  class="filter-input"
                >
              </div>
              <div class="filter-group">
                <label class="filter-label">Statut OCR</label>
                <select
                  v-model="filters.ocrStatus"
                  class="filter-select"
                >
                  <option value="">
                    Tous
                  </option>
                  <option value="4">
                    ✅ OCR terminé
                  </option>
                  <option value="0">
                    ⏳ En attente
                  </option>
                  <option value="1">
                    🔄 En traitement
                  </option>
                  <option value="5">
                    ❌ Échec OCR
                  </option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">Service</label>
                <select
                  v-model="filters.service"
                  class="filter-select"
                >
                  <option value="">
                    Tous les services
                  </option>
                  <option value="Finance">
                    💼 Finance
                  </option>
                  <option value="RH">
                    👥 Ressources Humaines
                  </option>
                  <option value="Juridique">
                    ⚖️ Juridique
                  </option>
                  <option value="Commercial">
                    📈 Commercial
                  </option>
                  <option value="Informatique">
                    💻 Informatique
                  </option>
                  <option value="Direction">
                    🏢 Direction
                  </option>
                  <option value="Autre">
                    📁 Autre
                  </option>
                </select>
              </div>
            </div>
            <div
              v-if="hasActiveFilters"
              class="filters-reset-row"
            >
              <button
                class="filters-reset-btn"
                @click="resetFilters"
              >
                ✕ Réinitialiser tous les filtres
              </button>
            </div>
          </div>
        </div>

        <!-- RAG answer panel -->
        <div
          v-if="ragMode && (ragAnswer || ragLoading)"
          class="rag-answer-panel"
        >
          <!-- ── Header ───────────────────────────────────────────────── -->
          <div class="rag-answer-header">
            <div class="elise-avatar-row">
              <span class="elise-avatar">✨</span>
              <div>
                <span class="elise-name">Elise</span>
                <span class="elise-subtitle">Assistante documentaire IA</span>
              </div>
            </div>
            <div class="rag-header-actions">
              <span
                v-if="ragSources.length"
                class="rag-source-count"
              >{{ ragSources.length }} source(s)</span>
              <button
                class="rag-close"
                @click="ragAnswer = ''; ragSources = []"
              >
                ✕
              </button>
            </div>
          </div>

          <!-- ── Loading state ────────────────────────────────────────── -->
          <div
            v-if="ragLoading"
            class="rag-thinking"
          >
            <svg
              class="spinner"
              style="width:16px;height:16px"
              fill="none"
              viewBox="0 0 24 24"
            >
              <circle
                class="spinner-bg"
                cx="12"
                cy="12"
                r="10"
                stroke="currentColor"
                stroke-width="4"
              />
              <path
                class="spinner-path"
                fill="currentColor"
                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
              />
            </svg>
            Elise analyse vos documents…
          </div>

          <!-- ── Two-part answer ──────────────────────────────────────── -->
          <template v-else-if="ragAnswer">
            <!-- Part 1: Elise conversational intro -->
            <div class="elise-intro-block">
              <p class="elise-intro-text">
                Voici ce que j'ai trouvé dans vos documents :
              </p>
            </div>

            <!-- Part 2: Actual query result -->
            <div class="elise-result-block">
              <p class="rag-answer-text">
                {{ ragAnswer }}
              </p>
            </div>
          </template>

          <!-- ── Sources ──────────────────────────────────────────────── -->
          <div
            v-if="ragSources.length"
            class="rag-sources-grid"
          >
            <div
              v-for="(src, i) in ragSources"
              :key="i"
              class="rag-source-chip"
            >
              <span class="src-num">{{ i + 1 }}</span>
              <div class="src-body">
                <p class="src-title">
                  {{ src.title }}
                </p>
                <p class="src-meta">
                  <span v-if="src.category">{{ src.category }} · </span>
                  {{ Math.round(src.relevanceScore * 100) }}% pertinent
                </p>
                <p
                  v-if="src.excerpt"
                  class="src-excerpt"
                >
                  {{ src.excerpt }}
                </p>
              </div>
              <button
                class="src-view-btn"
                @click="viewDocument({ id: src.documentId, title: src.title, fileName: src.title, contentType: 'application/pdf', score: src.relevanceScore })"
              >
                Voir
              </button>
            </div>
          </div>
        </div>

        <!-- Results summary -->
        <div
          v-if="searchResults && searchResults.documents?.length > 0"
          class="results-summary"
        >
          <div class="summary-card">
            <span class="summary-count">{{ searchResults.totalResults }}</span>
            <span class="summary-text"> résultat(s)</span>
            <span class="summary-divider">·</span>
            <span class="summary-time">{{ searchResults.searchTimeMs }}ms</span>
          </div>
          <div class="summary-page">
            Page {{ searchResults.page }} / {{ searchResults.totalPages }}
          </div>
        </div>
        
        <!-- List summary (when browsing all documents) -->
        <div
          v-else-if="!searched && documents.length > 0"
          class="results-summary"
        >
          <div class="summary-card">
            <span class="summary-count">{{ documents.length }}</span>
            <span class="summary-text"> document(s) dans le système</span>
          </div>
        </div>

        <!-- Documents grid -->
        <div
          v-if="(searchResults && searchResults.documents?.length > 0) || (!searched && documents.length > 0)"
          class="documents-grid"
        >
          <article
            v-for="doc in (searchResults?.documents || documents)"
            :key="doc.id"
            class="document-card"
          >
            <div class="card-content">
              <div class="doc-info">
                <div class="doc-header">
                  <div class="file-icon-box">
                    <span class="icon-emoji">{{ getFileIcon(doc.contentType) }}</span>
                  </div>
                  <div class="doc-details">
                    <h3 class="doc-title">
                      {{ doc.title }}
                    </h3>
                    <p
                      v-if="doc.description"
                      class="doc-description"
                    >
                      {{ doc.description }}
                    </p>
                    <div
                      v-if="doc.highlights && doc.highlights.length"
                      class="highlights"
                    >
                      <div
                        v-for="(h,i) in doc.highlights.slice(0,2)"
                        :key="i"
                        class="highlight-item"
                        v-html="h"
                      />
                    </div>
                    <div class="metadata-row">
                      <span class="meta-item">{{ doc.fileName }}</span>
                      <span
                        v-if="doc.documentDate"
                        class="meta-item meta-highlight"
                      >📅 {{ formatDate(doc.documentDate) }}</span>
                      <span class="meta-item">{{ formatSize(doc.fileSize) }}</span>
                      <span
                        v-if="doc.category"
                        class="category-badge"
                      >{{ doc.category }}</span>
                      <span
                        class="status-dot"
                        :class="statusClass(doc.status)"
                      >{{ doc.status }}</span>
                      <!-- OCR Badge - shows if OCR text has been extracted -->
                      <template v-if="docPipelineStatuses[doc.id] && docPipelineStatuses[doc.id].status">
                        <!-- Check for both number 4 and string 'Completed' -->
                        <span
                          v-if="(docPipelineStatuses[doc.id].status === 4 || docPipelineStatuses[doc.id].status === 'Completed' || docPipelineStatuses[doc.id].status === 'Complete') && docPipelineStatuses[doc.id].extractedText"
                          class="ocr-badge ocr-badge-done"
                          title="OCR texte extrait"
                        >🔬 OCR</span>
                        <span
                          v-else-if="docPipelineStatuses[doc.id].status === 5 || docPipelineStatuses[doc.id].status === 'Failed'"
                          class="ocr-badge ocr-badge-fail"
                          title="OCR échoué"
                        >⚠️ OCR</span>
                        <span
                          v-else
                          class="ocr-badge ocr-badge-pending"
                          :title="`OCR: ${docPipelineStatuses[doc.id].stageLabel || 'En cours'}`"
                        >⏳ OCR</span>
                      </template>
                      <!-- Fallback: check both isFullyProcessed and docTagsUpdated -->
                      <span
                        v-else-if="docTagsUpdated[doc.id] && doc.isFullyProcessed"
                        class="ocr-badge ocr-badge-done"
                        title="OCR complet"
                      >🔬 OCR</span>
                      <span
                        v-else-if="doc.isOcrProcessed"
                        class="ocr-badge ocr-badge-pending"
                        title="OCR en cours"
                      >⏳ OCR</span>
                      <span
                        v-if="doc.ocrQualityScore !== undefined && doc.ocrQualityScore !== null"
                        class="ocr-quality-badge"
                        :class="doc.ocrQualityScore >= 0.8 ? 'oq-good' : doc.ocrQualityScore >= 0.5 ? 'oq-medium' : 'oq-low'"
                        :title="`Qualité OCR : ${Math.round(doc.ocrQualityScore*100)}%`"
                      >
                        Q: {{ Math.round(doc.ocrQualityScore*100) }}%
                      </span>
                      <span
                        v-if="doc.service"
                        class="service-badge"
                      >{{ doc.service }}</span>
                    </div>
                    <div
                      v-if="doc.tags && doc.tags.length"
                      class="tags-row"
                    >
                      <span
                        v-for="tag in doc.tags.slice(0,5)"
                        :key="tag"
                        class="tag"
                      >#{{ tag }}</span>
                      <span
                        v-if="doc.tags.length > 5"
                        class="tag-more"
                      >+{{ doc.tags.length - 5 }}</span>
                    </div>
                    <!-- Pipeline status indicator (OCR + Tagging + Indexing) -->
                    <div
                      v-if="docPipelineStatuses[doc.id]"
                      class="pipeline-status-indicator"
                      :class="getPipelineDisplay(doc.id).class"
                    >
                      <span class="pipeline-status-dot" />
                      <span class="pipeline-status-text">{{ getPipelineDisplay(doc.id).text }}</span>
                    </div>
                  </div>
                </div>
              </div>
              <div class="doc-actions">
                <div class="score-wrapper">
                  <div class="score-circle">
                    <svg
                      class="circle-svg"
                      viewBox="0 0 100 100"
                    >
                      <circle
                        cx="50"
                        cy="50"
                        r="40"
                        class="circle-bg"
                      />
                      <circle
                        cx="50"
                        cy="50"
                        r="40"
                        class="circle-progress"
                        :style="`stroke-dashoffset: ${251 - 251 * (doc.score || 0)}`"
                      />
                    </svg>
                    <div class="score-text">
                      <span class="score-value">{{ Math.round((doc.score || 0) * 100) }}%</span>
                    </div>
                  </div>
                  <p class="score-label">
                    Pertinence
                  </p>
                </div>
                <!-- Admin actions -->
                <button
                  class="view-btn"
                  title="Voir / Modifier"
                  @click="viewDocument(doc)"
                >
                  <svg
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      stroke-width="2"
                      d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
                    />
                    <path
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      stroke-width="2"
                      d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"
                    />
                  </svg>
                  Voir
                </button>
                <button
                  class="acl-btn"
                  title="Gérer les accès"
                  @click="openAcl(doc)"
                >
                  <svg
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      stroke-width="2"
                      d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
                    />
                  </svg>
                  Accès
                </button>
                <button
                  class="delete-btn"
                  title="Supprimer"
                  @click="deleteDoc(doc)"
                >
                  <svg
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      stroke-width="2"
                      d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
                    />
                  </svg>
                  Supprimer
                </button>
              </div>
            </div>
          </article>
        </div>

        <!-- Pagination -->
        <nav
          v-if="searchResults && searchResults.totalPages > 1"
          class="pagination"
        >
          <button
            v-for="page in paginationPages"
            :key="page"
            :class="['page-btn', { active: page === searchResults.page }]"
            @click="goToPage(page)"
          >
            {{ page }}
          </button>
        </nav>

        <!-- Empty / initial states -->
        <div
          v-else-if="!searchLoading && searched && (!searchResults || searchResults.documents?.length === 0)"
          class="state-box empty-state"
        >
          <div class="state-icon">
            <svg
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
              />
            </svg>
          </div>
          <h3>Aucun document trouvé</h3>
          <p>Essayez d'ajuster votre requête ou vos filtres</p>
          <button
            class="clear-btn"
            @click="docSearch = ''; searchResults = null; searched = false; fetchDocuments()"
          >
            Réinitialiser
          </button>
        </div>

        <div
          v-else-if="!searched && loadingDocs"
          class="state-box loading-state"
        >
          <div class="spinner-ring" /> Chargement…
        </div>

        <div
          v-else-if="!searched && !loadingDocs && documents.length === 0"
          class="state-box empty-state"
        >
          <h3>Aucun document dans le système</h3>
          <p>Importez votre premier document pour commencer</p>
        </div>
      </section>

      <!-- ── USERS TAB ─────────────────────────────────────────────────── -->
      <section v-if="activeTab === 'users'">
        <AppBreadcrumb :crumbs="usersBreadcrumbs" />
        <div class="page-header">
          <div>
            <h1 class="page-title">
              Gestion des utilisateurs
            </h1>
            <p class="page-subtitle">
              {{ users.length }} utilisateur(s) enregistré(s)
            </p>
          </div>
          <button
            class="btn-primary"
            @click="showCreateUser = true"
          >
            + Créer un utilisateur
          </button>
        </div>

        <div class="table-card">
          <div
            v-if="loadingUsers"
            class="loading-state"
          >
            <div class="spinner-ring" /> Chargement…
          </div>
          <table
            v-else
            class="doc-table"
          >
            <thead>
              <tr>
                <th>Utilisateur</th>
                <th>Rôle</th>
                <th>Statut</th>
                <th>Dernière connexion</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="u in users"
                :key="u.id"
              >
                <td>
                  <div class="user-cell">
                    <div class="user-mini-avatar">
                      {{ initials(u) }}
                    </div>
                    <div>
                      <p class="doc-name">
                        {{ u.fullName || u.username }}
                      </p>
                      <p class="doc-filename">
                        {{ u.username }}
                      </p>
                    </div>
                  </div>
                </td>
                <td>
                  <span
                    class="role-tag"
                    :class="roleClass(u.role)"
                  >{{ roleLabel(u.role) }}</span>
                </td>
                <td>
                  <span
                    class="status-dot"
                    :class="u.isActive ? 'active' : 'inactive'"
                  >{{ u.isActive ? 'Actif' : 'Désactivé' }}</span>
                </td>
                <td class="muted">
                  {{ u.lastLoginAt ? formatDate(u.lastLoginAt) : '—' }}
                </td>
                <td>
                  <button
                    v-if="u.isActive"
                    class="btn-icon-sm danger"
                    @click="deactivateUser(u)"
                  >
                    Désactiver
                  </button>
                  <span
                    v-else
                    class="muted"
                  >—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- ── STATISTICS TAB ─────────────────────────────────────────────── -->
      <section
        v-if="activeTab === 'stats'"
        class="stats-section"
      >
        <AppBreadcrumb :crumbs="statsBreadcrumbs" />
        <div class="page-header">
          <div>
            <h1 class="page-title">
              Statistiques système
            </h1>
            <p class="page-subtitle">
              Vue d'ensemble de la GED et de la file OCR
            </p>
          </div>
          <button
            class="btn-secondary"
            :disabled="statsLoading"
            @click="fetchStats"
          >
            <svg
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              style="width:15px;height:15px"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
              />
            </svg>
            Actualiser
          </button>
        </div>

        <!-- KPI cards -->
        <div class="stats-kpi-grid">
          <div class="kpi-card kpi-blue">
            <div class="kpi-icon">
              📄
            </div>
            <div class="kpi-body">
              <p class="kpi-label">
                Documents indexés
              </p>
              <p class="kpi-value">
                {{ statsLoading ? '…' : (stats?.totalDocuments ?? '—') }}
              </p>
            </div>
          </div>
          <div class="kpi-card kpi-green">
            <div class="kpi-icon">
              👥
            </div>
            <div class="kpi-body">
              <p class="kpi-label">
                Utilisateurs actifs
              </p>
              <p class="kpi-value">
                {{ users.filter(u => u.isActive).length }}
              </p>
            </div>
          </div>
          <div class="kpi-card kpi-amber">
            <div class="kpi-icon">
              ⏳
            </div>
            <div class="kpi-body">
              <p class="kpi-label">
                File OCR en cours
              </p>
              <p class="kpi-value">
                {{ ocrQueue.length }}
              </p>
            </div>
          </div>
          <div class="kpi-card kpi-purple">
            <div class="kpi-icon">
              ⚡
            </div>
            <div class="kpi-body">
              <p class="kpi-label">
                Temps de recherche
              </p>
              <p class="kpi-value">
                {{ stats?.searchTimeMs != null ? stats.searchTimeMs + ' ms' : '—' }}
              </p>
            </div>
          </div>
        </div>

        <!-- Reindex panel -->
        <div class="stats-card">
          <h2 class="stats-card-title">
            <svg
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              style="width:16px;height:16px"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
              />
            </svg>
            Ré-indexation OpenSearch
          </h2>
          <p class="stats-card-desc">
            Déclenche un ré-indexation complète de tous les documents dans OpenSearch. Utile après une modification du mapping ou une restauration de données.
          </p>
          <div style="display:flex;align-items:center;gap:1rem;flex-wrap:wrap">
            <button
              :disabled="reindexing"
              class="reindex-btn"
              @click="triggerReindex"
            >
              <svg
                v-if="!reindexing"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                style="width:15px;height:15px"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
                />
              </svg>
              <svg
                v-else
                class="spinner"
                fill="none"
                viewBox="0 0 24 24"
                style="width:15px;height:15px"
              >
                <circle
                  class="spinner-bg"
                  cx="12"
                  cy="12"
                  r="10"
                  stroke="currentColor"
                  stroke-width="4"
                />
                <path
                  class="spinner-path"
                  fill="currentColor"
                  d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                />
              </svg>
              {{ reindexing ? 'Ré-indexation en cours…' : 'Lancer la ré-indexation' }}
            </button>
            <span
              v-if="reindexMsg"
              class="reindex-msg"
            >{{ reindexMsg }}</span>
          </div>
        </div>

        <!-- OCR queue -->
        <div class="stats-card">
          <h2 class="stats-card-title">
            <svg
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              style="width:16px;height:16px"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
              />
            </svg>
            File de traitement OCR
          </h2>
          <div
            v-if="ocrQueue.length === 0"
            class="stats-empty"
          >
            ✅ Aucun document en attente de traitement OCR.
          </div>
          <div
            v-else
            class="ocr-queue-list"
          >
            <div
              v-for="doc in ocrQueue"
              :key="doc.id"
              class="ocr-queue-item"
            >
              <span class="oq-icon">{{ getFileIcon(doc.contentType) }}</span>
              <div class="oq-info">
                <p class="oq-title">
                  {{ doc.title }}
                </p>
                <p class="oq-meta">
                  {{ doc.fileName }} · {{ formatSize(doc.fileSize) }}
                </p>
              </div>
              <span class="oq-status-badge">🔄 En traitement</span>
            </div>
          </div>
        </div>

        <!-- Users breakdown -->
        <div class="stats-card">
          <h2 class="stats-card-title">
            <svg
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              style="width:16px;height:16px"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z"
              />
            </svg>
            Répartition des utilisateurs par rôle
          </h2>
          <div class="role-breakdown">
            <div
              v-for="role in ['Admin','Manager','User','ReadOnly']"
              :key="role"
              class="role-row"
            >
              <span
                class="role-name-pill"
                :class="roleClass(role)"
              >{{ roleLabel(role) }}</span>
              <div class="role-bar-wrap">
                <div class="role-bar">
                  <div
                    class="role-bar-fill"
                    :class="'rfill-'+role.toLowerCase()"
                    :style="`width:${users.length ? Math.round(users.filter(u=>u.role===role).length/users.length*100) : 0}%`"
                  />
                </div>
                <span class="role-count">{{ users.filter(u => u.role === role).length }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Charts row -->
        <div class="stats-charts-row">
          <div class="stats-card stats-chart-card">
            <h2 class="stats-card-title">
              <svg
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                style="width:16px;height:16px"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z"
                />
              </svg>
              Types de documents
            </h2>
            <p class="stats-card-desc">
              Répartition par type de fichier
            </p>
            <ChartWidget
              v-if="documentTypeChartData.length > 0"
              type="donut"
              title="documents"
              :data="documentTypeChartData"
              :line-color="'#3b82f6'"
            />
            <div
              v-else
              class="stats-empty"
            >
              <span v-if="statsLoading">Chargement…</span>
              <span v-else>Aucune donnée disponible</span>
            </div>
          </div>

          <div class="stats-card stats-chart-card">
            <h2 class="stats-card-title">
              <svg
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                style="width:16px;height:16px"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"
                />
              </svg>
              Catégories
            </h2>
            <p class="stats-card-desc">
              Documents par catégorie
            </p>
            <ChartWidget
              v-if="categoryChartData.length > 0"
              type="bar"
              :data="categoryChartData"
            />
            <div
              v-else
              class="stats-empty"
            >
              <span v-if="statsLoading">Chargement…</span>
              <span v-else>Aucune donnée disponible</span>
            </div>
          </div>

          <div class="stats-card stats-recent-card">
            <RecentDocumentsWidget
              :documents="recentDocs"
              :loading="recentDocsLoading"
              title="Documents récents"
              subtitle="Derniers documents ajoutés"
              :show-view-all="false"
              @select="(doc) => { /* TODO: navigate to document */ }"
            />
          </div>
        </div>
      </section>
      <!-- ── ACCESS MANAGEMENT TAB ────────────────────────────────────────── -->
      
      <section
        v-if="activeTab === 'access'"
        class="access-dashboard"
      >
        <AppBreadcrumb :crumbs="accessBreadcrumbs" />
        <div class="page-header">
          <div>
            <h1 class="page-title">
              Gestion des accès
            </h1>
            <p class="page-subtitle">
              {{ accessStats.groups }} groupe(s) ·
              {{ accessStats.activeGrants }} accès actifs ·
              {{ accessStats.expiredGrants }} expirés
            </p>
          </div>
          <button
            class="btn-primary"
            @click="openAccessModal('groups')"
          >
            <svg
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              style="width:18px;height:18px"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z"
              />
            </svg>
            Gérer les accès
          </button>
        </div>

        <!-- KPI rapides -->
        <div class="access-kpi-row">
          <div
            class="access-kpi-card"
            @click="openAccessModal('groups')"
          >
            <div
              class="akpi-icon"
              style="background:#eff6ff; color:#2563eb"
            >
              📦
            </div>
            <div class="akpi-body">
              <p class="akpi-value">
                {{ accessLoading ? '…' : accessStats.groups }}
              </p>
              <p class="akpi-label">
                Groupes de documents
              </p>
            </div>
            <svg
              class="akpi-arrow"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M9 5l7 7-7 7"
              />
            </svg>
          </div>

          <div
            class="access-kpi-card"
            @click="openAccessModal('rights')"
          >
            <div
              class="akpi-icon"
              style="background:#f0fdf4; color:#16a34a"
            >
              🔑
            </div>
            <div class="akpi-body">
              <p class="akpi-value">
                {{ accessLoading ? '…' : accessStats.activeGrants }}
              </p>
              <p class="akpi-label">
                Accès actifs
              </p>
            </div>
            <svg
              class="akpi-arrow"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M9 5l7 7-7 7"
              />
            </svg>
          </div>

          <div
            class="access-kpi-card"
            :class="{ 'akpi-warning': accessStats.expiredGrants > 0 }"
            @click="openAccessModal('rights')"
          >
            <div
              class="akpi-icon"
              style="background:#fff7ed; color:#ea580c"
            >
              ⏰
            </div>
            <div class="akpi-body">
              <p class="akpi-value">
                {{ accessLoading ? '…' : accessStats.expiredGrants }}
              </p>
              <p class="akpi-label">
                Accès expirés
              </p>
            </div>
            <svg
              class="akpi-arrow"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M9 5l7 7-7 7"
              />
            </svg>
          </div>

          <div
            class="access-kpi-card"
            @click="openAccessModal('roles')"
          >
            <div
              class="akpi-icon"
              style="background:#faf5ff; color:#7c3aed"
            >
              👤
            </div>
            <div class="akpi-body">
              <p class="akpi-value">
                {{ accessLoading ? '…' : users.filter(u => u.isActive).length }}
              </p>
              <p class="akpi-label">
                Utilisateurs actifs
              </p>
            </div>
            <svg
              class="akpi-arrow"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M9 5l7 7-7 7"
              />
            </svg>
          </div>
        </div>

        <!-- Tableau des groupes (preview) -->
        <div class="access-preview-card">
          <div class="apc-header">
            <h2 class="apc-title">
              <span>📦</span> Groupes récents
            </h2>
            <button
              class="apc-see-all"
              @click="openAccessModal('groups')"
            >
              Voir tout →
            </button>
          </div>

          <div
            v-if="accessLoading"
            class="apc-loading"
          >
            <div class="spinner-ring" /> Chargement…
          </div>
          <div
            v-else-if="!accessGroups.length"
            class="apc-empty"
          >
            Aucun groupe créé. Cliquez sur "Gérer les accès" pour commencer.
          </div>
          <div
            v-else
            class="apc-groups-list"
          >
            <div
              v-for="g in accessGroups.slice(0, 5)"
              :key="g.id"
              class="apc-group-row"
              @click="openAccessModal('groups')"
            >
              <div
                class="apc-group-icon"
                :style="{ background: (g.color || '#2563eb') + '22', color: g.color || '#2563eb' }"
              >
                {{ g.icon || '📁' }}
              </div>
              <div class="apc-group-info">
                <p class="apc-group-name">
                  {{ g.name }}
                </p>
                <p class="apc-group-meta">
                  <span>{{ g.category || 'Sans catégorie' }}</span>
                  <span class="apc-dot">·</span>
                  <span>{{ g.documentCount }} doc(s)</span>
                  <span class="apc-dot">·</span>
                  <span>{{ g.userCount }} utilisateur(s)</span>
                </p>
              </div>
              <svg
                class="apc-chevron"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M9 5l7 7-7 7"
                />
              </svg>
            </div>
            <div
              v-if="accessGroups.length > 5"
              class="apc-more"
            >
              + {{ accessGroups.length - 5 }} groupe(s) supplémentaire(s)
            </div>
          </div>
        </div>

        <!-- Accès directs expirés (alerte si présents) -->
        <div
          v-if="accessStats.expiredGrants > 0"
          class="access-alert-card"
        >
          <div class="alert-icon">
            ⚠️
          </div>
          <div>
            <p class="alert-title">
              {{ accessStats.expiredGrants }} accès expirés détectés
            </p>
            <p class="alert-desc">
              Ces accès ne sont plus fonctionnels mais restent visibles dans le journal. Vous pouvez les révoquer proprement.
            </p>
          </div>
          <button
            class="btn-primary"
            style="white-space:nowrap"
            @click="openAccessModal('rights')"
          >
            Voir les accès
          </button>
        </div>

        <!-- Répartition des rôles -->
        <div class="access-preview-card">
          <div class="apc-header">
            <h2 class="apc-title">
              <span>👤</span> Répartition des rôles
            </h2>
            <button
              class="apc-see-all"
              @click="openAccessModal('roles')"
            >
              Gérer les rôles →
            </button>
          </div>
          <div class="apc-roles-grid">
            <div
              v-for="role in ['Admin','Manager','User','ReadOnly']"
              :key="role"
              class="apc-role-row"
            >
              <span
                class="role-tag"
                :class="roleClass(role)"
              >{{ roleLabel(role) }}</span>
              <div class="apc-role-bar-wrap">
                <div class="apc-role-bar">
                  <div
                    class="apc-role-fill"
                    :class="'rfill-' + role.toLowerCase()"
                    :style="`width:${users.length ? Math.round(users.filter(u=>u.role===role).length/users.length*100) : 0}%`"
                  />
                </div>
                <span class="apc-role-count">{{ users.filter(u=>u.role===role).length }}</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- ── TAXONOMY TAB ──────────────────────────────────────────────────── -->
      <section
        v-if="activeTab === 'taxonomy'"
        class="taxonomy-section"
      >
        <AppBreadcrumb :crumbs="taxonomyBreadcrumbs" />
        <TaxonomyManager />
      </section>
    </main>

    <!-- ══════════════════════════════════════════════════════════════════
         ADMIN DOCUMENT VIEWER MODAL
         (identical layout to User.vue but with an extra "Modifier" tab in the details pane)
    ══════════════════════════════════════════════════════════════════ -->
    <div
      v-if="showDocumentViewer"
      class="modal-overlay"
      @click.self="closeDocumentViewer"
    >
      <div class="viewer-modal">
        <!-- Header -->
        <div class="viewer-header">
          <div class="viewer-header-left">
            <div class="viewer-file-badge">
              {{ getFileExtension(currentDocument?.fileName) }}
            </div>
            <div class="viewer-title-block">
              <h2 class="viewer-title">
                {{ currentDocument?.title }}
              </h2>
              <p class="viewer-filename">
                <span>{{ getFileIcon(currentDocument?.contentType) }}</span>
                {{ currentDocument?.fileName }}
                <span class="vf-sep">·</span> {{ formatSize(currentDocument?.fileSize) }}
                <span
                  v-if="currentDocument?.category"
                  class="vf-sep"
                >·</span>
                <span
                  v-if="currentDocument?.category"
                  class="vf-cat"
                >{{ currentDocument.category }}</span>
              </p>
            </div>
          </div>
          <div class="viewer-header-actions">
            <!-- Admin action buttons in header -->
            <a
              :href="`/api/documents/${currentDocument?.id}/download`"
              class="hdr-btn hdr-download"
              title="Télécharger"
            >
              <svg
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"
                />
              </svg>
            </a>
            <button
              class="hdr-btn hdr-acl"
              title="Gérer les accès"
              @click="openAcl(currentDocument); closeDocumentViewer()"
            >
              <svg
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
                />
              </svg>
            </button>
            <button
              class="hdr-btn hdr-delete"
              title="Supprimer"
              @click="deleteDocFromViewer"
            >
              <svg
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
                />
              </svg>
            </button>
            <div class="tab-switcher">
              <button
                :class="['tab-btn', { active: viewerTab === 'preview' }]"
                @click="viewerTab = 'preview'"
              >
                <svg
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
                  />
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"
                  />
                </svg>
                Aperçu
              </button>
              <button
                :class="['tab-btn', { active: viewerTab === 'details' }]"
                @click="viewerTab = 'details'"
              >
                <svg
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
                Détails
              </button>
            </div>
            <button
              class="hdr-close"
              @click="closeDocumentViewer"
            >
              <svg
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M6 18L18 6M6 6l12 12"
                />
              </svg>
            </button>
          </div>
        </div>

        <!-- Body: two-column split -->
        <div class="viewer-body">
          <!-- LEFT: File Preview -->
          <div
            class="viewer-preview-pane"
            :class="{ 'tab-hidden': viewerTab !== 'preview' }"
          >
            <div
              v-if="documentLoading"
              class="preview-loading"
            >
              <div class="pulse-ring" />
              <svg
                class="spinner xl"
                fill="none"
                viewBox="0 0 24 24"
              >
                <circle
                  class="spinner-bg"
                  cx="12"
                  cy="12"
                  r="10"
                  stroke="currentColor"
                  stroke-width="4"
                />
                <path
                  class="spinner-path"
                  fill="currentColor"
                  d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                />
              </svg>
              <p>Chargement de l'aperçu…</p>
            </div>

            <div
              v-else-if="isPDF(currentDocument?.contentType)"
              class="pdf-viewer"
            >
              <iframe
                v-if="documentUrl"
                :src="documentUrl + '#toolbar=1&navpanes=0&zoom=page-fit'"
                class="pdf-frame"
                title="PDF Viewer"
              />
            </div>

            <div
              v-else-if="isImage(currentDocument?.contentType)"
              class="image-viewer"
            >
              <a
                :href="documentUrl"
                target="_blank"
              >
                <img
                  :src="documentUrl"
                  :alt="currentDocument?.title"
                  class="document-image"
                >
              </a>
              <p class="image-hint">
                Cliquer pour ouvrir en pleine taille
              </p>
            </div>

            <div
              v-else-if="isText(currentDocument?.contentType)"
              class="text-viewer"
            >
              <div class="text-toolbar">
                <span>{{ documentContent?.split('\n').length }} lignes</span>
                <span>{{ documentContent?.length?.toLocaleString() }} caractères</span>
              </div>
              <pre class="text-content">{{ documentContent }}</pre>
            </div>

            <div
              v-else-if="isOffice(currentDocument?.contentType)"
              class="office-viewer"
            >
              <div class="office-tabs">
                <button
                  :class="['otab', { active: officeMode === 'text' }]"
                  @click="officeMode = 'text'"
                >
                  📄 Texte extrait
                </button>
                <button
                  :class="['otab', { active: officeMode === 'embed' }]"
                  @click="officeMode = 'embed'"
                >
                  🌐 Office Online
                </button>
              </div>
              <div
                v-if="officeMode === 'text'"
                class="office-text-panel"
              >
                <div
                  v-if="documentContent"
                  class="office-text-wrap"
                >
                  <div class="office-text-stats">
                    <span>{{ documentContent.split(/\s+/).filter(Boolean).length.toLocaleString() }} mots</span>
                    <span>{{ documentContent.split('\n').length }} paragraphes</span>
                  </div>
                  <pre class="office-text-content">{{ documentContent }}</pre>
                </div>
                <div
                  v-else
                  class="office-no-text"
                >
                  <div class="ont-icon">
                    {{ getFileIcon(currentDocument?.contentType) }}
                  </div>
                  <p class="ont-title">
                    Aucun texte extrait disponible
                  </p>
                  <p class="ont-sub">
                    L'extraction est peut-être en cours.
                  </p>
                </div>
              </div>
              <div
                v-if="officeMode === 'embed'"
                class="office-embed-panel"
              >
                <div class="office-embed-notice">
                  Office Online nécessite une URL publique.
                  <a
                    href="#"
                    class="office-embed-link"
                  >Ouvrir dans Office Online ↗</a>
                </div>
              </div>
            </div>

            <div
              v-else-if="isAudio(currentDocument?.contentType)"
              class="audio-viewer"
            >
              <div class="audio-art">
                <div class="audio-wave">
                  <span
                    v-for="i in 20"
                    :key="i"
                    class="wave-bar"
                    :style="`animation-delay:${i*0.07}s`"
                  />
                </div>
                <div style="font-size:3rem">
                  🎵
                </div>
                <p style="color:#94a3b8;font-size:.85rem;margin-top:.5rem">
                  {{ currentDocument?.fileName }}
                </p>
              </div>
              <audio
                :src="documentUrl"
                controls
                class="audio-player"
              />
            </div>

            <div
              v-else-if="isVideo(currentDocument?.contentType)"
              class="video-viewer"
            >
              <video
                :src="documentUrl"
                controls
                class="video-player"
              />
            </div>

            <div
              v-else
              class="unsupported-viewer"
            >
              <span style="font-size:5rem">{{ getFileIcon(currentDocument?.contentType) }}</span>
              <h3>Aperçu non disponible</h3>
              <p>Ce type de fichier ne peut pas être affiché dans le navigateur.</p>
              <code>{{ currentDocument?.contentType }}</code>
            </div>
          </div>

          <!-- RIGHT: Details + Admin Edit pane -->
          <div
            class="viewer-details-pane"
            :class="{ 'tab-hidden': viewerTab !== 'details' }"
          >
            <!-- OCR status bar -->
            <div
              v-if="ocrStatus"
              class="ocr-status-bar"
              :class="ocrStatus.status===4?'ocr-done':ocrStatus.status===5?'ocr-fail':ocrStatus.status===2?'ocr-partial':'ocr-pending'"
            >
              <span class="ocr-dot" />
              <span v-if="ocrStatus.status===4">OCR terminé · {{ (ocrStatus.rawTextLength||0).toLocaleString() }} caractères</span>
              <span v-else-if="ocrStatus.status===5">OCR échoué : {{ ocrStatus.errorMessage }}</span>
              <span v-else-if="ocrStatus.status===2">Texte prêt · Amélioration IA en cours…</span>
              <span v-else>{{ ocrStatus.stageLabel ?? 'Traitement OCR…' }}</span>
            </div>

            <!-- ── Admin: Edit section (top of right pane) ── -->
            <div class="detail-section admin-edit-section">
              <h3 class="detail-section-title admin-edit-title">
                <svg
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"
                  />
                </svg>
                Modifier le document
              </h3>

              <div
                v-if="editSuccess"
                class="edit-banner success"
              >
                {{ editSuccess }}
              </div>
              <div
                v-if="editError"
                class="edit-banner error"
              >
                {{ editError }}
              </div>

              <div class="edit-form">
                <div class="edit-field">
                  <label class="edit-label">Titre</label>
                  <input
                    v-model="editData.title"
                    class="edit-input"
                    placeholder="Titre du document"
                  >
                </div>
                <div class="edit-field">
                  <label class="edit-label">Description</label>
                  <textarea
                    v-model="editData.description"
                    class="edit-input edit-textarea"
                    rows="2"
                    placeholder="Description courte…"
                  />
                </div>
                <div class="edit-field">
                  <label class="edit-label">Catégorie</label>
                  <select
                    v-model="editData.category"
                    class="edit-input"
                  >
                    <option value="">
                      — Aucune —
                    </option>
                    <option value="Invoice">
                      📄 Facture
                    </option>
                    <option value="Contract">
                      📜 Contrat
                    </option>
                    <option value="Report">
                      📊 Rapport
                    </option>
                    <option value="Letter">
                      ✉️ Courrier
                    </option>
                    <option value="Memo">
                      📝 Mémo
                    </option>
                    <option value="Presentation">
                      📽️ Présentation
                    </option>
                    <option value="Spreadsheet">
                      📈 Tableur
                    </option>
                    <option value="Image">
                      🖼️ Image
                    </option>
                    <option value="Other">
                      📎 Autre
                    </option>
                  </select>
                </div>
                <div class="edit-field">
                  <label class="edit-label">Date du document</label>
                  <input
                    v-model="editData.documentDate"
                    type="date"
                    class="edit-input"
                  >
                </div>
                <div class="edit-field">
                  <label class="edit-label">Étiquettes <span class="edit-hint">(séparées par des virgules)</span></label>
                  <input
                    v-model="editData.tagsRaw"
                    class="edit-input"
                    placeholder="tag1, tag2, tag3"
                  >
                </div>
                <div class="edit-field">
                  <label class="edit-label">Service</label>
                  <select
                    v-model="editData.service"
                    class="edit-input"
                  >
                    <option value="">
                      — Aucun —
                    </option>
                    <option value="Finance">
                      💼 Finance
                    </option>
                    <option value="RH">
                      👥 Ressources Humaines
                    </option>
                    <option value="Juridique">
                      ⚖️ Juridique
                    </option>
                    <option value="Commercial">
                      📈 Commercial
                    </option>
                    <option value="Informatique">
                      💻 Informatique
                    </option>
                    <option value="Direction">
                      🏢 Direction
                    </option>
                    <option value="Autre">
                      📁 Autre
                    </option>
                  </select>
                </div>
                <button
                  :disabled="savingDoc"
                  class="edit-save-btn"
                  @click="saveDocument"
                >
                  <svg
                    v-if="!savingDoc"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      stroke-width="2"
                      d="M5 13l4 4L19 7"
                    />
                  </svg>
                  <svg
                    v-else
                    class="spinner"
                    style="width:14px;height:14px"
                    fill="none"
                    viewBox="0 0 24 24"
                  >
                    <circle
                      class="spinner-bg"
                      cx="12"
                      cy="12"
                      r="10"
                      stroke="currentColor"
                      stroke-width="4"
                    />
                    <path
                      class="spinner-path"
                      fill="currentColor"
                      d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                    />
                  </svg>
                  {{ savingDoc ? 'Enregistrement…' : 'Enregistrer les modifications' }}
                </button>
              </div>
            </div>

            <!-- ── Informations (read-only) ── -->
            <div class="detail-section">
              <h3 class="detail-section-title">
                <svg
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
                Informations
              </h3>
              <dl class="detail-list">
                <div class="dl-row">
                  <dt>Fichier</dt><dd class="dd-mono">
                    {{ currentDocument?.fileName }}
                  </dd>
                </div>
                <div class="dl-row">
                  <dt>Type</dt><dd>
                    <span class="mime-badge">{{ getFileExtension(currentDocument?.fileName).toUpperCase() }}</span>
                    <span class="mime-text">{{ currentDocument?.contentType }}</span>
                  </dd>
                </div>
                <div class="dl-row">
                  <dt>Taille</dt><dd>{{ formatSize(currentDocument?.fileSize) }}</dd>
                </div>
                <div class="dl-row">
                  <dt>Statut</dt><dd>
                    <span
                      class="status-dot"
                      :class="statusClass(currentDocument?.status)"
                    >{{ currentDocument?.status }}</span>
                  </dd>
                </div>
                <div class="dl-row">
                  <dt>Importé</dt><dd>{{ formatDateLong(currentDocument?.createdAt) }}</dd>
                </div>
                <div
                  v-if="currentDocument?.createdBy"
                  class="dl-row"
                >
                  <dt>Par</dt><dd>{{ currentDocument.createdBy }}</dd>
                </div>
                <div
                  v-if="currentDocument?.modifiedAt"
                  class="dl-row"
                >
                  <dt>Modifié</dt><dd>{{ formatDateLong(currentDocument.modifiedAt) }}</dd>
                </div>
                <div
                  v-if="currentDocument?.id"
                  class="dl-row"
                >
                  <dt>ID</dt><dd
                    class="dd-mono"
                    style="font-size:.68rem"
                  >
                    {{ currentDocument.id }}
                  </dd>
                </div>
              </dl>
            </div>

            <!-- Tags -->
            <div
              v-if="currentDocument?.tags?.length"
              class="detail-section"
            >
              <h3 class="detail-section-title">
                <svg
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"
                  />
                </svg>
                Étiquettes
              </h3>
              <div class="tags-cloud">
                <span
                  v-for="tag in currentDocument.tags"
                  :key="tag"
                  class="tag-cloud-item"
                >#{{ tag }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
    <!-- ── ACCESS MANAGEMENT MODAL ──────────────────────────────────────── -->
    <AccessManagementModal
      v-if="showAccessModal"
      :initial-tab="accessModalTab"
      :initial-groups="accessGroups"
      :initial-access-summary="accessSummaryData"
      @close="showAccessModal = false"
      @saved="onAccessSaved"
    />

    <!-- ── UPLOAD MODAL (Batch) ───────────────────────────────────────────────── -->
    <div
      v-if="showUpload"
      class="modal-overlay"
      @click.self="showUpload = false"
    >
      <div class="modal modal-large">
        <div class="modal-header">
          <h2>Importer des documents (Batch)</h2>
          <button
            class="close-btn"
            @click="showUpload = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div
            v-if="selectedFiles.length === 0"
            class="drop-zone"
            @click="$refs.fileInput.click()"
            @dragover.prevent
            @drop.prevent="onDropMultiple"
          >
            <div class="drop-icon">
              📁
            </div>
            <p class="drop-text">
              Cliquez ou glissez plusieurs fichiers ici
            </p>
            <p class="drop-sub">
              PDF, Word, Excel, Images…
            </p>
            <input
              ref="fileInput"
              type="file"
              class="hidden-input"
              multiple
              @change="onFileSelectMultiple"
            >
          </div>
          <div
            v-else
            class="files-preview-list"
          >
            <div
              v-for="(file, index) in selectedFiles"
              :key="index"
              class="file-preview-item"
            >
              <span class="file-emoji">{{ getFileIcon(file.type) }}</span>
              <div>
                <p class="doc-name">
                  {{ file.name }}
                </p>
                <p class="muted">
                  {{ formatSize(file.size) }}
                </p>
              </div>
              <button
                class="btn-icon-sm danger"
                @click="removeFile(index)"
              >
                ✕
              </button>
            </div>
          </div>

          <div
            v-if="selectedFiles.length > 0"
            class="batch-category-section"
          >
            <p class="batch-info">
              Tous les fichiers utiliseront les mêmes paramètres ci-dessous :
            </p>
            <div class="form-row">
              <label class="form-label">Catégorie *</label>
              <select
                v-model="uploadCategory"
                class="form-input"
              >
                <option value="">
                  — Sélectionner —
                </option>
                <option
                  v-for="c in categories"
                  :key="c"
                  :value="c"
                >
                  {{ c }}
                </option>
              </select>
            </div>
          </div>
          <div class="modal-footer">
            <button
              class="btn-ghost"
              @click="showUpload = false"
            >
              Annuler
            </button>
            <button
              :disabled="selectedFiles.length === 0 || !uploadCategory || uploading"
              class="btn-primary"
              @click="doUploadBatch"
            >
              <span v-if="!uploading">Importer {{ selectedFiles.length }} fichier(s)</span>
              <span v-else>Envoi en cours... {{ uploadProgress }}/{{ selectedFiles.length }}</span>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- ── ACL MODAL ───────────────────────────────────────────────────── -->
    <div
      v-if="showAcl && aclDoc"
      class="modal-overlay"
      @click.self="showAcl = false"
    >
      <div class="modal modal-wide">
        <div class="modal-header">
          <div>
            <h2>Accès au document</h2>
            <p class="modal-subtitle">
              {{ aclDoc.title }}
            </p>
          </div>
          <button
            class="close-btn"
            @click="showAcl = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div
            v-if="aclError"
            class="banner error"
          >
            {{ aclError }}
          </div>
          <div
            v-if="aclSuccess"
            class="banner success"
          >
            {{ aclSuccess }}
          </div>

          <div class="acl-form">
            <h3 class="section-label">
              Accorder l'accès
            </h3>
            <div class="acl-form-row">
              <select
                v-model="grantUserId"
                class="form-input"
              >
                <option value="">
                  — Choisir un utilisateur —
                </option>
                <option
                  v-for="u in nonAdminUsers"
                  :key="u.id"
                  :value="u.id"
                >
                  {{ u.fullName || u.username }} ({{ roleLabel(u.role) }})
                </option>
              </select>
              <select
                v-model="grantPermission"
                class="form-input short"
              >
                <option value="Read">
                  Lecture
                </option>
                <option value="Write">
                  Écriture
                </option>
                <option value="FullControl">
                  Contrôle total
                </option>
              </select>
            </div>
            <div class="acl-form-row">
              <div class="access-type-toggle">
                <button
                  class="toggle-btn"
                  :class="{ active: grantPermanent }"
                  @click="grantPermanent = true"
                >
                  🔓 Accès permanent
                </button>
                <button
                  class="toggle-btn"
                  :class="{ active: !grantPermanent }"
                  @click="grantPermanent = false"
                >
                  ⏱ Accès limité dans le temps
                </button>
              </div>
            </div>
            <div
              v-if="!grantPermanent"
              class="acl-form-row"
            >
              <label class="form-label">Date d'expiration</label>
              <input
                v-model="grantExpiry"
                type="datetime-local"
                class="form-input"
                :min="minExpiry"
              >
            </div>
            <button
              :disabled="!grantUserId || savingAcl"
              class="btn-primary"
              @click="grantAccess"
            >
              {{ savingAcl ? 'Enregistrement…' : 'Accorder l\'accès' }}
            </button>
          </div>

          <div class="acl-list">
            <h3 class="section-label">
              Accès existants
            </h3>
            <div
              v-if="loadingAcl"
              class="loading-state"
            >
              <div class="spinner-ring" />
            </div>
            <div
              v-else-if="aclEntries.length === 0"
              class="empty-acl"
            >
              Aucun accès spécifique accordé.
            </div>
            <div
              v-else
              class="acl-entries"
            >
              <div
                v-for="entry in aclEntries"
                :key="entry.id"
                class="acl-entry"
                :class="{ expired: !entry.isActive }"
              >
                <div class="acl-user-info">
                  <div class="user-mini-avatar sm">
                    {{ (entry.fullName || entry.username).charAt(0).toUpperCase() }}
                  </div>
                  <div>
                    <p class="doc-name">
                      {{ entry.fullName || entry.username }}
                    </p>
                    <p class="muted">
                      {{ entry.username }}
                    </p>
                  </div>
                </div>
                <div class="acl-meta">
                  <span class="perm-badge">{{ permLabel(entry.permission) }}</span>
                  <span
                    v-if="entry.isPermanent"
                    class="perm-permanent"
                  >Permanent</span>
                  <span
                    v-else-if="entry.isActive"
                    class="perm-expiry"
                  >Expire le {{ formatDate(entry.expiresAt) }}</span>
                  <span
                    v-else
                    class="perm-expired"
                  >Expiré</span>
                </div>
                <button
                  class="btn-icon-sm danger"
                  @click="revokeAccess(entry)"
                >
                  Révoquer
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ── CREATE USER MODAL ─────────────────────────────────────────── -->
    <div
      v-if="showCreateUser"
      class="modal-overlay"
      @click.self="showCreateUser = false"
    >
      <div class="modal">
        <div class="modal-header">
          <h2>Créer un utilisateur</h2>
          <button
            class="close-btn"
            @click="showCreateUser = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div
            v-if="userError"
            class="banner error"
          >
            {{ userError }}
          </div>
          <div
            v-if="userSuccess"
            class="banner success"
          >
            {{ userSuccess }}
          </div>
          <div class="form-row">
            <label class="form-label">Nom d'utilisateur *</label>
            <input
              v-model="newUser.username"
              class="form-input"
              placeholder="ex: jean.dupont"
            >
          </div>
          <div class="form-row">
            <label class="form-label">Mot de passe *</label>
            <input
              v-model="newUser.password"
              type="password"
              class="form-input"
              placeholder="Min. 8 caractères"
            >
          </div>
          <div class="form-row">
            <label class="form-label">Nom complet</label>
            <input
              v-model="newUser.fullName"
              class="form-input"
              placeholder="Jean Dupont"
            >
          </div>
          <div class="form-row">
            <label class="form-label">Email</label>
            <input
              v-model="newUser.email"
              type="email"
              class="form-input"
              placeholder="jean@example.com"
            >
          </div>
          <div class="form-row">
            <label class="form-label">Rôle</label>
            <select
              v-model="newUser.role"
              class="form-input"
            >
              <option value="User">
                Utilisateur
              </option>
              <option value="Manager">
                Responsable
              </option>
              <option value="ReadOnly">
                Lecture seule
              </option>
              <option value="Admin">
                Administrateur
              </option>
            </select>
          </div>
          <div class="modal-footer">
            <button
              class="btn-ghost"
              @click="showCreateUser = false"
            >
              Annuler
            </button>
            <button
              :disabled="savingUser"
              class="btn-primary"
              @click="createUser"
            >
              {{ savingUser ? 'Création…' : 'Créer' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import AccessManagementModal from '../components/AccessManagementModal.vue'
import TaxonomyManager from '../components/TaxonomyManager.vue'
import ChartWidget from '../shared/ui/ChartWidget.vue'
import RecentDocumentsWidget from '../shared/components/RecentDocumentsWidget.vue'
import AppBreadcrumb from '../shared/components/AppBreadcrumb.vue'

const statsBreadcrumbs = computed(() => [
  { label: 'GED Admin' },
  { label: 'Statistiques' }
])

const documentsBreadcrumbs = computed(() => [
  { label: 'GED Admin' },
  { label: 'Documents' }
])

const usersBreadcrumbs = computed(() => [
  { label: 'GED Admin' },
  { label: 'Utilisateurs' }
])

const accessBreadcrumbs = computed(() => [
  { label: 'GED Admin' },
  { label: 'Accès' }
])

const taxonomyBreadcrumbs = computed(() => [
  { label: 'GED Admin' },
  { label: 'Taxonomie' }
])

const router = useRouter()

// ── Auth ───────────────────────────────────────────────────────────────────────
const user = computed(() => JSON.parse(localStorage.getItem('ged_user') || '{}'))
const userInitials = computed(() => {
  const n = user.value?.fullName || user.value?.username || '?'
  return n.split(' ').map(c => c[0]).join('').toUpperCase().slice(0, 2)
})
const authHeader = () => ({ 'Authorization': `Bearer ${localStorage.getItem('ged_token')}`, 'Content-Type': 'application/json' })
const logout = () => { localStorage.clear(); router.push('/login') }

// ── Tabs ───────────────────────────────────────────────────────────────────────
const activeTab = ref('documents')
const tabs = [
  { id: 'documents', label: 'Documents',    icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/></svg>' },
  { id: 'users',     label: 'Utilisateurs', icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z"/></svg>' },
  { id: 'access',    label: 'Accès',        icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z"/></svg>' },
  { id: 'stats',     label: 'Statistiques', icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z"/></svg>' },
  { id: 'taxonomy',  label: 'Taxonomie',    icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"/></svg>' },
]

// ── Documents / Search ─────────────────────────────────────────────────────────
const documents      = ref([])
const loadingDocs    = ref(false)
const docSearch      = ref('')
const showFilters    = ref(false)
const searchLoading  = ref(false)
const searched       = ref(false)
const searchResults  = ref(null)
const totalResults   = ref(null)
const docPipelineStatuses = ref({})  // Track pipeline status (OCR + Tagging + Indexing) for each document
const docTagsUpdated = ref({})  // Track which documents have completed full pipeline (tags updated = done)
const docListPollInterval = ref(null)
const filters        = reactive({ category: '', contentType: '', dateFrom: '', dateTo: '', ocrStatus: '', service: '' })
const quickSearches  = ['tous les documents', 'factures', 'contrats 2024', 'PDF récents', 'rapports']
const ragMode       = ref(false)   // toggle: false = normal search, true = RAG
const ragModeForced = ref(false)   // true if user explicitly forced RAG mode
const ragAnswer     = ref('')
const ragSources    = ref([])
const ragLoading    = ref(false)

// ── Contextual RAG detection (Multilingual: Arabic, French, English) ────────────────
const ragTriggerReason = ref(null)

// Check if query contains specific indexed terms (categories, tags, etc.)
const hasIndexedTerms = (query) => {
  const q = query.toLowerCase()
  
  // Categories (French)
  if (/\b(facture|factures|contrat|contrats|rapport|rapports|courrier|mémo|memos|présentation|présentations|tableur|tableurs|image|images|autre)\b/.test(q)) return true
  // Categories (English)
  if (/\b(invoice|invoices|contract|contracts|report|reports|letter|memo|memos|presentation|presentations|spreadsheet|spreadsheets|image|images|other)\b/.test(q)) return true
  // Categories (Arabic)
  if (/\b(عقد|عقود|تقرير|تقارير|صورة|صور)\b/.test(q)) return true
  
  // Services (French)
  if (/\b(finance|rh|ressources humaines|juridique|commercial|informatique|direction|autre)\b/.test(q)) return true
  // Services (English)
  if (/\b(finance|hr|human resources|legal|commercial|it|technology|direction|other)\b/.test(q)) return true
  
  // File extensions
  if (/\b(pdf|doc|docx|xls|xlsx|ppt|pptx|jpg|jpeg|png|txt)\b/.test(q)) return true
  
  // Tags (starts with #)
  if (/#\w+/.test(q)) return true
  
  // Specific date formats (DD/MM/YYYY, YYYY-MM-DD)
  if (/\b\d{1,2}\/\d{1,2}\/\d{2,4}\b/.test(q) || /\b\d{4}-\d{2}-\d{2}\b/.test(q)) return true
  
  // Specific status
  if (/\b(indexé|indexée|indexés|indexées|échoué|échouée|processing|en cours|pending)\b/.test(q)) return true
  
  return false
}

const isQuestion = (query) => {
  if (!query) return false
  const q = query.trim()
  const qlc = q.toLowerCase()
  
  // If query has specific indexed terms, use normal search
  if (hasIndexedTerms(q)) {
    return false
  }
  
  // Explicit question with ? mark
  if (q.includes('?')) {
    ragTriggerReason.value = 'Question détectée'
    return true
  }
  
  // Check for Arabic script presence
  const isArabic = /[\u0600-\u06FF]/.test(q)
  
  // ─── ARABIC ─────────────────────────────────────────────────────────────────────
  if (isArabic) {
    // Arabic interrogatives - clear question words
    if (/\b(ما|من|أين|كيف|لماذا|متى|هل|كم|أي|أيش|ليش|وش|وين|شو|ليش|أيش|ليهما|لمن|لمَن|في أي|على أي|أي من|ما الذي|ما هي|من الذي|من هم|كيف يمكن|ما شأن|ما أمر|ما السبب|هل يمكن|هل يوجد|هل there)\b/.test(q)) {
      ragTriggerReason.value = 'Question arabe détectée'
      return true
    }
    // Arabic AI requests (summarize, explain, etc.)
    if (/\b(لخص|ملخص|فاهم|اشرح|Explain|Summarize|Summarise|احصل على ملخص|دعني اعرف|ما المعلومات)\b/.test(q)) {
      ragTriggerReason.value = 'Demande IA arabe détectée'
      return true
    }
  }
  
  // ─── FRENCH ────────────────────────────────────────────────────────────────────
  // French interrogatives - clear question words only
  if (/\b(qui|que|quoi|où|quand|comment|pourquoi|combien|quel|quelle|quels|quelles|est-ce que|est-ce qu|avez|avez-vous|peut|peux|puis-je|dois-je|saurais-je|veut|vouloir|voudrais|devrait|serait|pouvons|pouvez|est-ce|êtes-vous|êtes tu|suis-je)\b/.test(qlc)) {
    ragTriggerReason.value = 'Question française détectée'
    return true
  }
  
  // French AI requests - explicit AI commands
  if (/\b(résumé|résume|summarize|summarise|explique|explain|décris|raconte|donne-moi|résumer)\b/.test(qlc)) {
    ragTriggerReason.value = 'Demande IA française détectée'
    return true
  }
  
  // ─── ENGLISH ─────────────────────────────────────────────────────────────────
  // English interrogatives - clear question words only
  if (/\b(what|who|where|when|why|how|does|do|can|could|should|would|is|are|was|were|has|have|had|will|shall|might|must|need|needs|need to|needs to|ought)\b/.test(qlc)) {
    ragTriggerReason.value = 'English question detected'
    return true
  }
  
  // English AI requests - explicit AI commands
  if (/\b(summarize|summarise|explain|describe|tell me|give me|analyze|analyse)\b/.test(qlc)) {
    ragTriggerReason.value = 'English AI request detected'
    return true
  }
  
  // ─── Comparison & Analysis queries ───────────────────────────────────────────
  // These need AI understanding
  if (/\b(comparer|comparaison|différence|vs|versus|compare|comparison)\b/.test(qlc)) {
    ragTriggerReason.value = 'Requête comparative détectée'
    return true
  }
  
  // Aggregation queries that need AI
  if (/\b(analyste|analyse|résumé global|summary|overall|overall summary|rapport global)\b/.test(qlc)) {
    ragTriggerReason.value = 'Requête analytique détectée'
    return true
  }
   
  ragTriggerReason.value = null
  return false
}

const toggleRagMode = (fromShortcut = false) => {
  ragMode.value = !ragMode.value
  ragModeForced.value = ragMode.value
  ragAnswer.value = ''
  ragSources.value = []
  if (!ragMode.value) {
    ragTriggerReason.value = null
  } else if (fromShortcut) {
    ragTriggerReason.value = 'Activé via raccourci clavier'
  }
}

// Confirm attachments - automatically triggers RAG mode
const confirmAttachments = () => {
  showDocPicker.value = false
  if (attachedDocIds.value.length > 0) {
    ragMode.value = true
    ragModeForced.value = true
    ragTriggerReason.value = 'Document(s) joint(s) à la conversation'
  }
}

// Keyboard shortcut: Ctrl+Shift+R to toggle RAG mode
const handleKeydown = (e) => {
  if (e.ctrlKey && e.shiftKey && e.key === 'R') {
    e.preventDefault()
    toggleRagMode(true)
  }
}

const attachedDocIds  = ref([])   // IDs of documents pinned for Elise
const showDocPicker   = ref(false)
const pickerSearch    = ref('')
const pickerLoading   = ref(false)
const pickerDocs      = ref([])

const filteredPickerDocs = computed(() =>
  pickerSearch.value.trim()
    ? pickerDocs.value.filter(d => d.title.toLowerCase().includes(pickerSearch.value.toLowerCase()))
    : pickerDocs.value
)

const fetchPickerDocs = async () => {
  pickerLoading.value = true
  try {
    // Use wildcard search to get all documents
    const res = await fetch('/api/search/query', {
      method: 'POST',
      headers: authHeader(),
      body: JSON.stringify({ query: '*', searchType: 3, page: 1, pageSize: 500 })
    })
    if (res.ok) {
      const data = await res.json()
      pickerDocs.value = data.documents || []
    }
  } catch (e) {
    console.error('[Picker] fetchPickerDocs error:', e)
  } finally {
    pickerLoading.value = false
  }
}

const openDocPicker = () => {
  showDocPicker.value = true
  pickerSearch.value = ''
  fetchPickerDocs()   // always refresh the list when opening
}

// ── NLP interpretation ─────────────────────────────────────────────────────────
const nlpInterpretation = ref(null)
const searchError = ref(null)

// ── Autocomplete ───────────────────────────────────────────────────────────────
const showAutocomplete        = ref(false)
const autocompleteSuggestions = ref([])
const selectedAcIndex         = ref(-1)
let _acTimer = null

const onSearchInput = () => {
  selectedAcIndex.value = -1
  clearTimeout(_acTimer)
  if (docSearch.value.trim().length < 2) { showAutocomplete.value = false; autocompleteSuggestions.value = []; return }
  _acTimer = setTimeout(async () => {
    try {
      const r = await fetch(`/api/search/suggestions?q=${encodeURIComponent(docSearch.value)}`, { headers: authHeader() })
      if (r.ok) {
        const data = await r.json()
        autocompleteSuggestions.value = Array.isArray(data) ? data.slice(0, 7) : []
        showAutocomplete.value = autocompleteSuggestions.value.length > 0
      }
    } catch { /* non-fatal */ }
  }, 280)
}

const selectSuggestion = (dir) => {
  if (!showAutocomplete.value) return
  const max = autocompleteSuggestions.value.length - 1
  selectedAcIndex.value = Math.max(-1, Math.min(max, selectedAcIndex.value + dir))
  if (selectedAcIndex.value >= 0) docSearch.value = autocompleteSuggestions.value[selectedAcIndex.value]
}

const applyAutocomplete = (sug) => {
  docSearch.value = sug; showAutocomplete.value = false; handleSearch()
}

// ── Expanded summaries ─────────────────────────────────────────────────────────
const expandedSummaries = ref(new Set())
const toggleSummary = (id) => {
  const s = new Set(expandedSummaries.value); s.has(id) ? s.delete(id) : s.add(id); expandedSummaries.value = s
}

// ── Filter helpers ─────────────────────────────────────────────────────────────
const hasActiveFilters = computed(() =>
  !!(filters.category || filters.contentType || filters.dateFrom || filters.dateTo || filters.ocrStatus || filters.service)
)
const resetFilters = () => {
  Object.assign(filters, { category: '', contentType: '', dateFrom: '', dateTo: '', ocrStatus: '', service: '' })
  if (searched.value) searchDocuments()
}

const paginationPages = computed(() => {
  if (!searchResults.value) return []
  const total = searchResults.value.totalPages, cur = searchResults.value.page
  const start = Math.max(1, cur - 4), end = Math.min(total, start + 9)
  return Array.from({ length: end - start + 1 }, (_, i) => start + i)
})

const fetchDocuments = async (query = '') => {
  loadingDocs.value = true
  try {
    // Use wildcard search to get all documents when query is empty
    const searchType = query.trim() ? 0 : 3  // Natural for user queries, wildcard for empty
    const body = { query: query.trim() || '*', searchType, page: 1, pageSize: 50 }
    const res = await fetch('/api/search/query', {
      method: 'POST',
      headers: authHeader(),
      body: JSON.stringify(body)
    })
    if (res.ok) {
      const data = await res.json()
      documents.value = data.documents || []
    }
  } finally { loadingDocs.value = false }
}

// ── Multilingual query normalizer (mirrors User.vue) ──────────────────────────
const buildSearchBody = (page = 1) => ({
  query:        docSearch.value.trim(),   // raw — backend normalizes
  searchType:   0,                        // Natural
  page,
  pageSize:     20,
  categories:   filters.category    ? [filters.category]    : null,
  contentTypes: filters.contentType ? [filters.contentType] : null,
  fromDate:     filters.dateFrom    || null,
  toDate:       filters.dateTo      || null,
  ocrStatus:    filters.ocrStatus   ? parseInt(filters.ocrStatus) : null,
  service:      filters.service     || null,
  includeOcrContent: true
})

const handleSearch = () => {
  const query = docSearch.value.trim()
  
  // Auto-detect: if not explicitly forced, check if query looks like a question
  if (!ragModeForced.value && query) {
    if (isQuestion(query)) {
      ragMode.value = true
    } else {
      ragMode.value = false
    }
  }
  
  if (ragMode.value) return askRag()
  searchDocuments()
}

const askRag = async () => {
  const query = docSearch.value.trim()
  if (!query) return
  ragAnswer.value  = ''
  ragSources.value = []
  ragLoading.value = true
  searched.value   = true
  searchResults.value     = null
  nlpInterpretation.value = null
  searchError.value       = null
  try {
    const res = await fetch('/api/rag/ask', {
      method: 'POST',
      headers: authHeader(),
      body: JSON.stringify({
        query,
        language: 'fr',
        categories:  filters.category        ? [filters.category] : undefined,
        fromDate:    filters.dateFrom         || undefined,
        toDate:      filters.dateTo           || undefined,
        documentIds: attachedDocIds.value.length ? attachedDocIds.value : undefined,
      })
    })
    if (!res.ok) { searchError.value = `Erreur IA (HTTP ${res.status})`; return }
    const data = await res.json()
    ragAnswer.value  = data.answer  || ''
    ragSources.value = data.sources || []
  } catch {
    searchError.value = 'Impossible de contacter le service IA.'
  } finally {
    ragLoading.value = false
  }
}

const searchDocuments = async () => {
  searchLoading.value     = true
  searched.value          = true
  showAutocomplete.value  = false
  nlpInterpretation.value = null
  searchError.value       = null

  try {
    const res = await fetch('/api/search/query', {
      method:  'POST',
      headers: authHeader(),
      body:    JSON.stringify(buildSearchBody(1))
    })

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }))
      searchError.value   = err.error || 'Erreur de recherche.'
      searchResults.value = null
      totalResults.value  = 0
      return
    }

    const data = await res.json()

    // ── Understood check ──────────────────────────────────────────────────
    if (data.isUnderstood === false) {
      searchResults.value = null
      totalResults.value  = 0
      const lang = data.detectedLanguage
      searchError.value = lang === 'ar'
        ? 'الرجاء إدخال مصطلح بحث صحيح.'
        : lang === 'fr'
          ? 'Veuillez entrer un terme de recherche valide.'
          : 'Please enter a proper search term.'
      return
    }

    searchResults.value = data
    totalResults.value  = data.totalResults

    // ── Fetch OCR status for each document ─────────────────────────────────────
    if (data.documents?.length) {
      console.log('[Search] Fetching pipeline status for', data.documents.length, 'documents')
      for (const doc of data.documents) {
        fetchDocOcrStatus(doc.id).then(status => {
          // Store pipeline status for this document
          docPipelineStatuses.value[doc.id] = status
          console.log('[Search] Pipeline status for', doc.id, ':', status?.status, status?.stageLabel, '| tags:', status?.tags?.length || 0, '| textLen:', status?.extractedText?.length || 0)
          
          // Update isFullyProcessed based on OCR status (status 4 = Completed)
          if (status) {
            doc.isFullyProcessed = status.status === 4
            // Only mark as complete if tags have been added (full pipeline done)
            // Tags are added by OcrWorkerService after OCR + enrichment
            if (status.tags && status.tags.length > 0) {
              console.log('[Search] Full pipeline COMPLETE - tags added for', doc.id, 'count:', status.tags.length)
              docTagsUpdated.value[doc.id] = true
            }
          }
          docPipelineStatuses.value = { ...docPipelineStatuses.value }
        })
      }
    }

    // ── NLP banner ────────────────────────────────────────────────────────
    if (data.nlpSummary) {
      nlpInterpretation.value = data.nlpSummary
    }

  } catch (err) {
    console.error('[Admin Search] Network error:', err)
    alert('Erreur réseau. Vérifiez que le backend est démarré.')
  } finally {
    searchLoading.value = false
  }
}

const goToPage = async (page) => {
  searchLoading.value = true
  try {
    const res = await fetch('/api/search/query', {
      method:  'POST',
      headers: authHeader(),
      body:    JSON.stringify(buildSearchBody(page))
    })
    if (res.ok) {
      searchResults.value = await res.json()
      window.scrollTo({ top: 0, behavior: 'smooth' })
    }
  } finally {
    searchLoading.value = false
  }
}

const deleteDoc = async (doc) => {
  if (!confirm(`Supprimer "${doc.title}" ? Cette action est irréversible.`)) return
  
  const docId = doc.id
  const idx = documents.value.findIndex(d => d.id === docId)
  if (idx !== -1) documents.value.splice(idx, 1)
  
  if (searchResults.value?.documents) {
    const searchIdx = searchResults.value.documents.findIndex(d => d.id === docId)
    if (searchIdx !== -1) searchResults.value.documents.splice(searchIdx, 1)
    searchResults.value.totalResults = (searchResults.value.totalResults || 1) - 1
  }
  
  delete docPipelineStatuses.value[docId]
  
  const res = await fetch(`/api/documents/${docId}`, { method: 'DELETE', headers: authHeader() })
  if (!res.ok) {
    await fetchDocuments()
    alert('Erreur lors de la suppression.')
  }
}

// ── Upload (Batch) ─────────────────────────────────────────────────────────────────────
const showUpload      = ref(false)
const selectedFiles   = ref([])
const uploadTitle     = ref('')
const uploadCategory  = ref('')
const uploading       = ref(false)
const uploadProgress  = ref(0)
const categories      = ['Invoice','Contract','Report','Letter','Memo','Presentation','Spreadsheet','Image','Other']

const onFileSelectMultiple = (e) => {
  const files = Array.from(e.target.files)
  if (files.length > 0) {
    selectedFiles.value = files
  }
}
const onDropMultiple = (e) => {
  const files = Array.from(e.dataTransfer.files)
  if (files.length > 0) {
    selectedFiles.value = files
  }
}
const removeFile = (index) => {
  selectedFiles.value.splice(index, 1)
  const inp = document.querySelector('.hidden-input'); if (inp) inp.value = ''
}
const clearFiles = () => {
  selectedFiles.value = []
  uploadTitle.value = ''
  uploadCategory.value = ''
}

const doUploadBatch = async () => {
  if (selectedFiles.value.length === 0 || !uploadCategory.value) return
  uploading.value = true
  uploadProgress.value = 0
  let successCount = 0
  let errorCount = 0
  
  try {
    for (let i = 0; i < selectedFiles.value.length; i++) {
      const file = selectedFiles.value[i]
      const form = new FormData()
      form.append('file', file)
      form.append('title', file.name.replace(/\.[^/.]+$/, ''))
      form.append('category', uploadCategory.value)
      
      const res = await fetch('/api/documents/upload', {
        method: 'POST',
        headers: { 'Authorization': `Bearer ${localStorage.getItem('ged_token')}` },
        body: form
      })
      
      if (res.ok) {
        successCount++
      } else {
        errorCount++
      }
      uploadProgress.value = i + 1
      await nextTick()
    }
    
    if (successCount > 0) {
      showUpload.value = false
      clearFiles()
      await fetchDocuments()
      alert(`${successCount} document(s) importé(s) avec succès !${errorCount > 0 ? `\n${errorCount} échecs.` : ''}`)
    } else {
      alert('Échec de l\'import de tous les fichiers.')
    }
  } catch { 
    alert("Erreur réseau lors de l'import.") 
  }
  finally { 
    uploading.value = false 
    uploadProgress.value = 0
  }
}

// ── Document Viewer ────────────────────────────────────────────────────────────
const showDocumentViewer = ref(false)
const currentDocument    = ref(null)
const documentUrl        = ref(null)
const documentContent    = ref(null)
const documentLoading    = ref(false)
const viewerTab          = ref('preview')
const officeMode         = ref('text')
const ocrStatus          = ref(null)  // OCR status for CURRENTLY VIEWED document (in modal)
const ocrPollInterval    = ref(null)  // Polling for the viewed document's OCR status

// Edit state (admin-only, in viewer)
const editData    = reactive({ title: '', description: '', category: '', documentDate: '', tagsRaw: '', service: '' })
const savingDoc   = ref(false)
const editSuccess = ref('')
const editError   = ref('')

let _blobUrl = null
const revokeBlobUrl = () => { if (_blobUrl) { URL.revokeObjectURL(_blobUrl); _blobUrl = null } }
const fetchBlobUrl  = async (path, mime) => {
  const res  = await fetch(path, { headers: { 'Authorization': `Bearer ${localStorage.getItem('ged_token')}` } })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  const buf  = await res.arrayBuffer()
  const type = mime || res.headers.get('content-type')?.split(';')[0].trim() || 'application/octet-stream'
  const url  = URL.createObjectURL(new Blob([buf], { type }))
  _blobUrl = url; return url
}

const onSearchBlur = () => {
  window.setTimeout(() => { showAutocomplete.value = false }, 180)
}

const OcrStatus = { Pending: 0, Processing: 1, TextExtracted: 2, LlmCleaning: 3, Completed: 4, Failed: 5 }

// Pipeline Indicator: shows full pipeline status (En attente → En cours → Terminé)
const getPipelineDisplay = (docId) => {
  const status = docPipelineStatuses.value[docId]
  const fullPipelineDone = docTagsUpdated.value[docId]
  
  if (!status) return { text: 'En attente', class: 'status-pending' }
  if (fullPipelineDone) return { text: 'Terminé', class: 'status-completed' }
  if (status.status === 5 || status.status === 'Failed') return { text: 'Échec', class: 'status-failed' }
  
  // Map OCR stageLabel to French
  let stageText = status.stageLabel || 'En cours'
  
  // Convert English stages to French
  if (stageText === 'Queued') stageText = 'En attente'
  else if (stageText === 'Processing') stageText = 'En cours'
  else if (stageText === 'TextExtracted') stageText = 'Texte extrait'
  else if (stageText === 'LlmCleaning') stageText = 'Analyse IA'
  else if (stageText === 'Complete' || stageText === 'Completed') stageText = 'En cours'
  
  return { text: stageText, class: 'status-processing' }
}
const stopOcrPolling  = () => { if (ocrPollInterval.value) { clearInterval(ocrPollInterval.value); ocrPollInterval.value = null } }
const startOcrPolling = (docId) => {
  stopOcrPolling(); let n = 0
  ocrPollInterval.value = setInterval(async () => {
    n++
    try {
      const res = await fetch(`/api/documents/${docId}/ocr-status`, { headers: { 'Authorization': `Bearer ${localStorage.getItem('ged_token')}` } })
      if (!res.ok) { if (n >= 50) stopOcrPolling(); return }
      const d = await res.json(); ocrStatus.value = d
      if (d.status === OcrStatus.TextExtracted && d.extractedText && !documentContent.value) {
        documentContent.value = d.extractedText
      }
      if (d.status === OcrStatus.Completed) {
        stopOcrPolling()
        if (d.extractedText) documentContent.value = d.extractedText
        currentDocument.value = { ...currentDocument.value, tags: d.tags ?? currentDocument.value.tags, description: d.description ?? currentDocument.value.description, documentDate: d.documentDate ?? currentDocument.value.documentDate }
        populateEditData(currentDocument.value)
        return
      }
      if (d.status === OcrStatus.Failed || n >= 50) stopOcrPolling()
    } catch { if (n >= 50) stopOcrPolling() }
  }, 4000)
}

const getOcrStatusLabel = (status) => {
  const labels = { 
    0: 'En attente', 1: 'Traitement OCR', 2: 'Texte extrait', 3: 'Analyse IA', 4: 'Terminé', 5: 'Échec',
    'Pending': 'En attente', 'Processing': 'Traitement OCR', 'TextExtracted': 'Texte extrait', 
    'LlmCleaning': 'Analyse IA', 'Completed': 'Terminé', 'Failed': 'Échec'
  }
  return labels[status] || 'Inconnu'
}

const getOcrStatusClass = (status) => {
  const classes = {
    0: 'status-pending', 1: 'status-processing', 2: 'status-processing', 3: 'status-processing', 4: 'status-completed', 5: 'status-failed',
    'Pending': 'status-pending', 'Processing': 'status-processing', 'TextExtracted': 'status-processing', 
    'LlmCleaning': 'status-processing', 'Completed': 'status-completed', 'Failed': 'status-failed'
  }
  return classes[status] || 'status-pending'
}

const fetchDocOcrStatus = async (docId) => {
  try {
    console.log('[Pipeline Status] Fetching for:', docId)
    const res = await fetch(`/api/documents/${docId}/ocr-status`, { headers: authHeader() })
    if (res.ok) {
      const data = await res.json()
      console.log('[Pipeline Status] Got data for:', docId, data.status, data.stageLabel)
      docPipelineStatuses.value[docId] = data
      return data
    } else {
      console.log('[Pipeline Status] Failed response for:', docId, res.status)
    }
  } catch (e) {
    console.log('[Pipeline Status] Error for:', docId, e)
  }
  return null
}

const startDocListPolling = () => {
  if (docListPollInterval.value) return
  console.log('[Polling] Started document list polling')
  docListPollInterval.value = setInterval(async () => {
    const docsToPoll = searchResults.value?.documents?.length 
      ? searchResults.value.documents 
      : documents.value
    
    if (!docsToPoll.length) return
    
    const pendingDocs = docsToPoll.filter(d => 
      d.status === 'Pending' || d.status === 'Processing' || 
      d.isOcrProcessed === false || d.isFullyProcessed === false
    )
    
    if (!pendingDocs.length) {
      console.log('[Polling] All documents indexed, stopping polling')
      stopDocListPolling()
      return
    }
    
    console.log('[Polling] Checking', pendingDocs.length, 'pending documents')
    let hasUpdates = false
    
    for (const doc of pendingDocs) {
      try {
        const [docRes, ocrRes] = await Promise.all([
          fetch(`/api/documents/${doc.id}`, { headers: authHeader() }),
          fetch(`/api/documents/${doc.id}/ocr-status`, { headers: authHeader() })
        ])
        
        if (docRes.status === 404) {
          console.log('[Polling] Document deleted:', doc.id)
          const idx = documents.value.findIndex(d => d.id === doc.id)
          if (idx !== -1) documents.value.splice(idx, 1)
          if (searchResults.value?.documents) {
            const searchIdx = searchResults.value.documents.findIndex(d => d.id === doc.id)
            if (searchIdx !== -1) {
              searchResults.value.documents.splice(searchIdx, 1)
              searchResults.value.totalResults = Math.max(0, (searchResults.value.totalResults || 1) - 1)
            }
          }
          delete docPipelineStatuses.value[doc.id]
          continue
        }
        
        if (docRes.ok) {
          const freshDoc = await docRes.json()
          const oldStatus = doc.status
          // Preserve isFullyProcessed as it's not in the document endpoint response
          const wasFullyProcessed = doc.isFullyProcessed
          Object.assign(doc, freshDoc)
          doc.isFullyProcessed = wasFullyProcessed
          
          if (oldStatus !== freshDoc.status) {
            console.log(`[Doc Status] ${doc.id}: ${oldStatus} -> ${freshDoc.status}`)
            hasUpdates = true
          }
        }
        
        if (ocrRes.ok) {
          const ocrData = await ocrRes.json()
          const oldTags = docPipelineStatuses.value[doc.id]?.tags ? [...docPipelineStatuses.value[doc.id].tags] : []
          const oldStatus = docPipelineStatuses.value[doc.id]?.status
          const oldStageLabel = docPipelineStatuses.value[doc.id]?.stageLabel
          const wasTagsUpdated = docTagsUpdated.value[doc.id]
          docPipelineStatuses.value[doc.id] = ocrData
          
          // Log pipeline status changes
          if (oldStatus !== undefined && oldStatus !== ocrData.status) {
            console.log(`[Pipeline] ${doc.id}: ${oldStageLabel || oldStatus} -> ${ocrData.stageLabel || ocrData.status} (tags: ${ocrData.tags?.length || 0}, textLen: ${ocrData.extractedText?.length || 0})`)
          }
          
          // Update isFullyProcessed based on OCR status
          // Status 4 = Completed (OCR done, but full pipeline needs extracted text)
          const isFullyProcessed = ocrData.status === 4
          if (doc.isFullyProcessed !== isFullyProcessed) {
            doc.isFullyProcessed = isFullyProcessed
            hasUpdates = true
          }
          
          // Check if tags were added - this is the real pipeline completion marker
          // Tags are added by OcrWorkerService after OCR + enrichment completes
          // Compare old tags count vs new tags count
          const oldTagsCount = oldTags.length
          const newTagsCount = ocrData.tags?.length || 0
          if (newTagsCount > 0 && newTagsCount !== oldTagsCount && !docTagsUpdated.value[doc.id]) {
            Object.assign(doc, { tags: ocrData.tags })
            docTagsUpdated.value[doc.id] = true
            console.log(`[Pipeline] FULLY COMPLETE - ${doc.id}: tags added (was: ${oldTagsCount}, now: ${newTagsCount})`)
            hasUpdates = true
          }
        }
      } catch (e) {
        console.log('[Polling] Error for', doc.id, e)
      }
    }
    
    if (hasUpdates) {
      docPipelineStatuses.value = { ...docPipelineStatuses.value }
      docTagsUpdated.value = { ...docTagsUpdated.value }
    }
  }, 5000)
}

const stopDocListPolling = () => {
  if (docListPollInterval.value) { clearInterval(docListPollInterval.value); docListPollInterval.value = null }
}

const populateEditData = (doc) => {
  editData.title        = doc.title        || ''
  editData.description  = doc.description  || ''
  editData.category     = doc.category     || ''
  editData.documentDate = doc.documentDate ? doc.documentDate.slice(0, 10) : ''
  editData.tagsRaw      = (doc.tags || []).join(', ')
  editData.service      = doc.service      || ''
  editSuccess.value     = ''
  editError.value       = ''
}

const viewDocument = async (doc) => {
  revokeBlobUrl(); stopOcrPolling()
  currentDocument.value = doc
  showDocumentViewer.value = true
  documentLoading.value = true
  documentContent.value = null
  documentUrl.value = null
  ocrStatus.value = null
  viewerTab.value = 'preview'
  officeMode.value = 'text'
  editSuccess.value = ''
  editError.value = ''

  try {
    const r = await fetch(`/api/documents/${doc.id}`, { headers: { 'Authorization': `Bearer ${localStorage.getItem('ged_token')}` } })
    if (r.ok) {
      const fresh = await r.json()
      currentDocument.value = { ...fresh, score: doc.score, highlights: doc.highlights }
    } else if (r.status === 404) {
      documentLoading.value = false
      showDocumentViewer.value = false
      alert(`Ce document n'existe plus dans la base de données (index désynchronisé). Veuillez rafraîchir la recherche.`)
      return
    }
    populateEditData(currentDocument.value)
  } catch { /* non-fatal */ }

  const dlPath  = `/api/documents/${doc.id}/download`
  const ocrPath = `/api/documents/${doc.id}/ocr-status`
  try {
    if (isPDF(doc.contentType) || isImage(doc.contentType) || isAudio(doc.contentType) || isVideo(doc.contentType)) {
      try { documentUrl.value = await fetchBlobUrl(dlPath, doc.contentType) } catch { documentUrl.value = dlPath }
    } else if (isText(doc.contentType)) {
      const r = await fetch(dlPath, { headers: { 'Authorization': `Bearer ${localStorage.getItem('ged_token')}` } })
      documentContent.value = r.ok ? await r.text() : '(impossible de charger le fichier)'
    } else if (isOffice(doc.contentType)) {
      try {
        const r = await fetch(ocrPath, { headers: { 'Authorization': `Bearer ${localStorage.getItem('ged_token')}` } })
        if (r.ok) { const d = await r.json(); ocrStatus.value = d; if (d.extractedText) documentContent.value = d.extractedText }
      } catch { /* non-fatal */ }
    }
    if (isPDF(doc.contentType) || isImage(doc.contentType)) {
      try {
        const r = await fetch(ocrPath, { headers: { 'Authorization': `Bearer ${localStorage.getItem('ged_token')}` } })
        if (r.ok) {
          const d = await r.json(); ocrStatus.value = d
          if ([OcrStatus.Pending, OcrStatus.Processing, OcrStatus.TextExtracted, OcrStatus.LlmCleaning].includes(d.status)) startOcrPolling(doc.id)
        }
      } catch { /* non-fatal */ }
    }
  } catch (e) { console.error('Viewer init error', e) }
  finally { documentLoading.value = false }
}

const closeDocumentViewer = () => {
  revokeBlobUrl(); stopOcrPolling()
  showDocumentViewer.value = false
  currentDocument.value = null
  documentUrl.value = null
  documentContent.value = null
  ocrStatus.value = null
}

const deleteDocFromViewer = async () => {
  if (!currentDocument.value) return
  if (!confirm(`Supprimer "${currentDocument.value.title}" ? Cette action est irréversible.`)) return
  const res = await fetch(`/api/documents/${currentDocument.value.id}`, { method: 'DELETE', headers: authHeader() })
  if (res.ok) {
    closeDocumentViewer()
    if (searched.value) searchDocuments()
    else fetchDocuments()
  } else {
    alert('Erreur lors de la suppression.')
  }
}

const saveDocument = async () => {
  if (!currentDocument.value) return
  savingDoc.value  = true
  editSuccess.value = ''
  editError.value   = ''
  try {
    const tags = editData.tagsRaw
      ? editData.tagsRaw.split(',').map(t => t.trim()).filter(Boolean)
      : []
    const payload = {
      ...currentDocument.value,
      title:        editData.title,
      description:  editData.description || null,
      category:     editData.category    || null,
      documentDate: editData.documentDate ? new Date(editData.documentDate).toISOString() : null,
      service:      editData.service      || null,
      tags
    }
    const res = await fetch(`/api/documents/${currentDocument.value.id}`, {
      method: 'PUT',
      headers: authHeader(),
      body: JSON.stringify(payload)
    })
    if (res.ok) {
      const updated = await res.json()
      currentDocument.value = { ...updated, score: currentDocument.value.score, highlights: currentDocument.value.highlights }
      populateEditData(updated)
      editSuccess.value = '✓ Modifications enregistrées avec succès.'
      setTimeout(() => { editSuccess.value = '' }, 3000)
      // Refresh search results if any
      if (searched.value) searchDocuments()
      else fetchDocuments()
    } else {
      const err = await res.json().catch(() => ({}))
      editError.value = err.error || `Erreur ${res.status}`
    }
  } catch (e) {
    editError.value = 'Erreur réseau lors de la sauvegarde.'
  } finally { savingDoc.value = false }
}

// ── Type guards ────────────────────────────────────────────────────────────────
const isPDF    = (t) => t === 'application/pdf'
const isImage  = (t) => !!t?.startsWith('image/')
const isText   = (t) => t === 'text/plain'
const isAudio  = (t) => !!t?.startsWith('audio/')
const isVideo  = (t) => !!t?.startsWith('video/')
const isOffice = (t) => ['application/msword','application/vnd.openxmlformats-officedocument.wordprocessingml.document','application/vnd.ms-excel','application/vnd.openxmlformats-officedocument.spreadsheetml.sheet','application/vnd.ms-powerpoint','application/vnd.openxmlformats-officedocument.presentationml.presentation'].includes(t)

// ── Users ──────────────────────────────────────────────────────────────────────
const users          = ref([])
const loadingUsers   = ref(false)
const showCreateUser = ref(false)
const showAccessModal = ref(false)
const accessModalTab   = ref('groups') 
const accessLoading    = ref(false)
const accessGroups     = ref([])
const accessSummaryData = ref([])  // Raw access summary for modal
const accessStats      = reactive({ groups: 0, activeGrants: 0, expiredGrants: 0 })
const savingUser     = ref(false)
const userError      = ref('')
const userSuccess    = ref('')
const newUser        = ref({ username: '', password: '', fullName: '', email: '', role: 'User' })

const nonAdminUsers = computed(() => users.value.filter(u => u.role !== 'Admin' && u.isActive))

/** Ouvre le modal sur un onglet précis */
const openAccessModal = (tab = 'groups') => {
  accessModalTab.value = tab
  showAccessModal.value = true
}
/** Appelé quand le modal AccessManagementModal émet @saved (création/révocation) */
const onAccessSaved = () => {
  showAccessModal.value = false  // Close modal to show updated data
  loadAccessDashboard()
}

/** Charge les groupes pour l'aperçu */
const loadGroups = async () => {
  try {
    const gRes = await fetch('/api/groups', { headers: authHeader() })
    if (gRes.ok) {
      const gs = await gRes.json()
      accessGroups.value = gs
      accessStats.groups = gs.length
    }
  } catch (e) {
    console.warn('[Access] Groups error:', e)
  }
}

/** Charge les droits d'accès */
const loadRights = async () => {
  try {
    const rRes = await fetch('/api/groups/users/access-summary', { headers: authHeader() })
    if (rRes.ok) {
      const summary = await rRes.json()
      accessSummaryData.value = summary  // Store raw data for modal
      let active = 0, expired = 0
      for (const u of summary) {
        for (const _ of (u.groups || [])) active++
        for (const d of (u.directGrants || [])) {
          d.isActive ? active++ : expired++
        }
      }
      accessStats.activeGrants = active
      accessStats.expiredGrants = expired
    }
  } catch (e) {
    console.warn('[Access] Summary error:', e)
  }
}

/** Charge les données légères pour l'aperçu de la section Accès */
const loadAccessDashboard = async () => {
  accessLoading.value = true
  await loadGroups()
  await loadRights()
  accessLoading.value = false
}

// Load when switching to access tab - always refresh to ensure data is current
watch(activeTab, async (tab) => {
  if (tab === 'access') {
    await loadAccessDashboard()
  }
})

const fetchUsers = async () => {
  loadingUsers.value = true
  try {
    const res = await fetch('/api/auth/users', { headers: authHeader() })
    if (res.ok) users.value = await res.json()
  } finally { loadingUsers.value = false }
}

const createUser = async () => {
  userError.value = userSuccess.value = ''
  if (!newUser.value.username || !newUser.value.password) {
    userError.value = 'Nom d\'utilisateur et mot de passe obligatoires.'
    return
  }
  savingUser.value = true
  try {
    const res = await fetch('/api/auth/register', {
      method: 'POST', headers: authHeader(), body: JSON.stringify(newUser.value)
    })
    if (res.ok) {
      userSuccess.value = `Utilisateur "${newUser.value.username}" créé.`
      newUser.value = { username: '', password: '', fullName: '', email: '', role: 'User' }
      await fetchUsers()
      setTimeout(() => { showCreateUser.value = false; userSuccess.value = '' }, 1500)
    } else {
      const e = await res.json()
      userError.value = e.error || 'Erreur.'
    }
  } finally { savingUser.value = false }
}

const deactivateUser = async (u) => {
  if (!confirm(`Désactiver "${u.username}" ?`)) return
  const res = await fetch(`/api/auth/users/${u.id}`, { method: 'DELETE', headers: authHeader() })
  if (res.ok) fetchUsers()
}

// ── ACL ────────────────────────────────────────────────────────────────────────
const showAcl         = ref(false)
const aclDoc          = ref(null)
const aclEntries      = ref([])
const loadingAcl      = ref(false)
const savingAcl       = ref(false)
const aclError        = ref('')
const aclSuccess      = ref('')
const grantUserId     = ref('')
const grantPermission = ref('Read')
const grantPermanent  = ref(true)
const grantExpiry     = ref('')

const minExpiry = computed(() => {
  const d = new Date()
  d.setMinutes(d.getMinutes() + 5)
  return d.toISOString().slice(0, 16)
})

const openAcl = async (doc) => {
  aclDoc.value   = doc
  aclError.value = aclSuccess.value = ''
  grantUserId.value = ''
  grantPermission.value = 'Read'
  grantPermanent.value  = true
  grantExpiry.value     = ''
  showAcl.value  = true
  loadingAcl.value = true
  try {
    const res = await fetch(`/api/documents/${doc.id}/acl`, { headers: authHeader() })
    if (res.ok) aclEntries.value = await res.json()
  } finally { loadingAcl.value = false }
}

const grantAccess = async () => {
  aclError.value = aclSuccess.value = ''
  if (!grantUserId.value) return
  savingAcl.value = true
  try {
    const body = {
      userId:     grantUserId.value,
      permission: grantPermission.value,
      expiresAt:  grantPermanent.value ? null : (grantExpiry.value ? new Date(grantExpiry.value).toISOString() : null)
    }
    const res = await fetch(`/api/documents/${aclDoc.value.id}/acl`, {
      method: 'POST', headers: authHeader(), body: JSON.stringify(body)
    })
    if (res.ok) {
      const u = users.value.find(u => u.id === grantUserId.value)
      aclSuccess.value = `Accès accordé à ${u?.fullName || u?.username || 'l\'utilisateur'}.`
      await openAcl(aclDoc.value)
    } else {
      const e = await res.json()
      aclError.value = e.error || 'Erreur.'
    }
  } finally { savingAcl.value = false }
}

const revokeAccess = async (entry) => {
  if (!confirm(`Révoquer l'accès de ${entry.fullName || entry.username} ?`)) return
  const res = await fetch(`/api/documents/${aclDoc.value.id}/acl/${entry.id}`, {
    method: 'DELETE', headers: authHeader()
  })
  if (res.ok) {
    aclSuccess.value = 'Accès révoqué.'
    await openAcl(aclDoc.value)
  }
}

// ── Helpers ────────────────────────────────────────────────────────────────────
const getFileIcon = (ct) => {
  if (!ct) return '📎'
  if (ct.includes('pdf')) return '📄'
  if (ct.includes('word') || ct.includes('document')) return '📝'
  if (ct.includes('sheet') || ct.includes('excel'))   return '📊'
  if (ct.includes('presentation') || ct.includes('powerpoint')) return '📽️'
  if (ct.includes('image')) return '🖼️'
  if (ct.includes('text'))  return '📃'
  if (ct.includes('audio')) return '🎵'
  if (ct.includes('video')) return '🎬'
  return '📎'
}
const getFileExtension = (n) => n ? (n.split('.').pop() || '').toLowerCase() : ''
const formatSize = (bytes) => {
  if (!bytes) return '—'
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / 1048576).toFixed(1) + ' MB'
}
const formatDate = (d) => {
  if (!d) return '—'
  try { return new Date(d).toLocaleDateString('fr-FR') } catch { return d }
}
const formatDateLong = (d) => {
  if (!d) return '—'
  try { return new Date(d).toLocaleString('fr-FR') } catch { return d }
}
const statusClass = (s) => ({ Indexed: 'active', Failed: 'danger', Processing: 'warning', Pending: 'warning' }[s] || 'inactive')
const roleLabel   = (r) => ({ Admin: 'Admin', Manager: 'Responsable', User: 'Utilisateur', ReadOnly: 'Lecture seule' }[r] || r)
const roleClass   = (r) => ({ Admin: 'role-admin', Manager: 'role-manager', User: 'role-user', ReadOnly: 'role-readonly' }[r] || '')
const initials    = (u) => (u.fullName || u.username || '?').split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2)
const permLabel   = (p) => ({ Read: 'Lecture', Write: 'Écriture', Delete: 'Suppression', FullControl: 'Contrôle total' }[p] || p)

// ── Statistics & System overview ───────────────────────────────────────────────
const stats           = ref(null)
const statsLoading    = ref(false)
const reindexing      = ref(false)
const reindexMsg      = ref('')
const ocrQueue        = ref([])
const recentDocs      = ref([])
const recentDocsLoading = ref(false)

// Chart data for document types
const documentTypeChartData = computed(() => {
  if (!stats.value?.documents) return []
  const types = {}
  stats.value.documents.forEach(doc => {
    const ct = doc.contentType || 'other'
    let label = 'Autre'
    if (ct.includes('pdf')) label = 'PDF'
    else if (ct.includes('word') || ct.includes('document')) label = 'Word'
    else if (ct.includes('excel') || ct.includes('sheet')) label = 'Excel'
    else if (ct.includes('image')) label = 'Images'
    types[label] = (types[label] || 0) + 1
  })
  return Object.entries(types).map(([label, value]) => ({
    label,
    value,
    color: label === 'PDF' ? '#ef4444' : label === 'Word' ? '#3b82f6' : label === 'Excel' ? '#16a34a' : label === 'Images' ? '#8b5cf6' : '#6b7280'
  }))
})

// Chart data for categories
const categoryChartData = computed(() => {
  if (!stats.value?.documents) return []
  const cats = {}
  stats.value.documents.forEach(doc => {
    const cat = doc.category || 'Autre'
    cats[cat] = (cats[cat] || 0) + 1
  })
  return Object.entries(cats)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 6)
    .map(([label, value]) => ({
      label,
      value,
      color: 'primary'
    }))
})

const fetchStats = async () => {
  statsLoading.value = true
  try {
    // Use wildcard search (searchType: 3) to get all documents
    const res = await fetch('/api/search/query', {
      method: 'POST', headers: authHeader(),
      body: JSON.stringify({ query: '*', searchType: 3, page: 1, pageSize: 100 })
    })
    if (res.ok) {
      const data = await res.json()
      stats.value = {
        totalDocuments: data.totalResults || 0,
        searchTimeMs:   data.searchTimeMs || 0,
        documents:      data.documents || [],
      }
    } else {
      console.warn('[Admin] fetchStats failed:', res.status)
      stats.value = { totalDocuments: 0, searchTimeMs: 0, documents: [] }
    }
  } catch (e) {
    console.error('[Admin] fetchStats error:', e)
    stats.value = { totalDocuments: 0, searchTimeMs: 0, documents: [] }
  } finally { statsLoading.value = false }
}

const fetchRecentDocs = async () => {
  recentDocsLoading.value = true
  try {
    // Use wildcard search (searchType: 3) with sort
    const res = await fetch('/api/search/query', {
      method: 'POST', headers: authHeader(),
      body: JSON.stringify({ query: '*', searchType: 3, page: 1, pageSize: 10, sortBy: 'CreatedDate', sortDescending: true })
    })
    if (res.ok) {
      const data = await res.json()
      recentDocs.value = data.documents || []
    }
  } catch (e) {
    console.warn('[Admin] fetchRecentDocs error:', e)
    recentDocs.value = []
  } finally { recentDocsLoading.value = false }
}

const triggerReindex = async () => {
  if (!confirm('Lancer une ré-indexation complète ? Cette opération peut prendre plusieurs minutes.')) return
  reindexing.value = true; reindexMsg.value = ''
  try {
    const res = await fetch('/api/search/reindex', { method: 'POST', headers: authHeader() })
    reindexMsg.value = res.ok ? '✅ Ré-indexation lancée avec succès.' : `❌ Erreur : ${res.status}`
  } catch { reindexMsg.value = '❌ Erreur réseau.' }
  finally { reindexing.value = false; setTimeout(() => reindexMsg.value = '', 5000) }
}

onMounted(async () => {
  await fetchDocuments()
  await fetchUsers()
  await fetchStats()
  await fetchRecentDocs()
  await loadGroups()
  await loadRights()
  window.addEventListener('keydown', handleKeydown)
  startDocListPolling()
})

onUnmounted(() => {
  stopDocListPolling()
  stopOcrPolling()
})
</script>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────────────────────── */
.admin-layout { display: flex; min-height: 100vh; background: #f1f5f9; font-family: 'Segoe UI', system-ui, sans-serif; }

/* ── Sidebar ────────────────────────────────────────────────────────────────── */
.sidebar { width: 240px; min-height: 100vh; background: #0f172a; display: flex; flex-direction: column; position: sticky; top: 0; height: 100vh; overflow-y: auto; }
.sidebar-brand { display: flex; align-items: center; gap: .75rem; padding: 1.25rem 1rem; border-bottom: 1px solid #1e293b; }
.brand-icon { width: 36px; height: 36px; background: linear-gradient(135deg, #3b82f6, #8b5cf6); border-radius: 10px; display: flex; align-items: center; justify-content: center; }
.brand-icon svg { width: 20px; height: 20px; color: white; }
.brand-name { font-weight: 700; font-size: 1rem; color: white; }
.sidebar-nav { flex: 1; padding: 1rem .75rem; display: flex; flex-direction: column; gap: .25rem; }
.nav-item { display: flex; align-items: center; gap: .625rem; padding: .625rem .875rem; border-radius: 8px; background: none; border: none; color: #94a3b8; font-size: .875rem; cursor: pointer; transition: all .15s; text-align: left; }
.nav-item:hover { background: #1e293b; color: #e2e8f0; }
.nav-item.active { background: #1d4ed8; color: white; font-weight: 600; }
.nav-icon { width: 18px; height: 18px; flex-shrink: 0; display: flex; align-items: center; }
.nav-icon :deep(svg) { width: 18px; height: 18px; }
.sidebar-footer { padding: .875rem; border-top: 1px solid #1e293b; display: flex; align-items: center; gap: .5rem; }
.admin-badge { display: flex; align-items: center; gap: .625rem; flex: 1; overflow: hidden; }
.admin-avatar { width: 34px; height: 34px; background: linear-gradient(135deg, #3b82f6, #8b5cf6); border-radius: 50%; color: white; font-size: .75rem; font-weight: 700; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
.admin-name { font-size: .8rem; font-weight: 600; color: #e2e8f0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.admin-role-tag { font-size: .7rem; color: #60a5fa; }
.logout-btn { background: none; border: none; color: #64748b; cursor: pointer; padding: .375rem; border-radius: 6px; transition: all .15s; flex-shrink: 0; }
.logout-btn:hover { background: #1e293b; color: #f87171; }
.logout-btn svg { width: 18px; height: 18px; }

/* ── Main ───────────────────────────────────────────────────────────────────── */
.admin-main { flex: 1; padding: 1.5rem 2rem; overflow-y: auto; min-width: 0; }
.search-section { display: flex; flex-direction: column; gap: 1.25rem; }

/* ── Page header ────────────────────────────────────────────────────────────── */
.page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: .5rem; }
.page-title { font-size: 1.5rem; font-weight: 700; color: #0f172a; }
.page-subtitle { font-size: .875rem; color: #64748b; margin-top: .2rem; }

/* ── Search card ─────────────────────────────────────────────────────────────── */
.search-card { background: white; border-radius: 14px; box-shadow: 0 4px 16px rgba(0,0,0,.06); padding: 1.5rem; border: 1px solid #e5e7eb; }
.search-bar-wrapper { display: flex; gap: .625rem; flex-wrap: wrap; }
.search-input-wrapper { flex: 1; min-width: 200px; position: relative; display: flex; align-items: center; }
.search-icon { position: absolute; left: .875rem; width: 20px; height: 20px; color: #9ca3af; pointer-events: none; }
.search-input { width: 100%; padding: .8rem 1rem .8rem 2.75rem; font-size: .9rem; border: 2px solid #e5e7eb; border-radius: 9px; outline: none; transition: all .2s; }
.search-input:focus { border-color: #3b82f6; box-shadow: 0 0 0 3px rgba(59,130,246,.1); }
.search-btn { padding: .8rem 1.5rem; background: linear-gradient(135deg, #2563eb, #4f46e5); color: white; border: none; border-radius: 9px; font-weight: 600; cursor: pointer; transition: all .2s; white-space: nowrap; }
.search-btn:hover:not(:disabled) { box-shadow: 0 6px 14px rgba(37,99,235,.3); transform: translateY(-1px); }
.search-btn:disabled { opacity: .5; cursor: not-allowed; }
.upload-btn { display: flex; align-items: center; gap: .4rem; padding: .8rem 1.1rem; background: #f0fdf4; color: #16a34a; border: 1.5px solid #86efac; border-radius: 9px; font-weight: 600; font-size: .875rem; cursor: pointer; transition: all .2s; white-space: nowrap; }
.upload-btn svg { width: 16px; height: 16px; }
.upload-btn:hover { background: #dcfce7; }
.loading-text { display: flex; align-items: center; gap: .5rem; }
.quick-searches { margin-top: .75rem; display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; }
.quick-label { font-size: .78rem; color: #6b7280; }
.quick-btn { padding: .28rem .65rem; font-size: .78rem; background: #f3f4f6; color: #374151; border: none; border-radius: 7px; cursor: pointer; transition: all .15s; }
.quick-btn:hover { background: #dbeafe; color: #1d4ed8; }
.filters-toggle { margin-top: .75rem; display: inline-flex; align-items: center; gap: .25rem; color: #2563eb; background: none; border: none; font-size: .8rem; font-weight: 500; cursor: pointer; }
.toggle-icon { width: 14px; height: 14px; }
.filters-panel { margin-top: 1rem; padding-top: 1rem; border-top: 1px solid #f3f4f6; }
.filters-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: .75rem; }
.filter-group { display: flex; flex-direction: column; gap: .3rem; }
.filter-label { font-size: .75rem; font-weight: 600; color: #6b7280; }
.filter-select, .filter-input { padding: .5rem .75rem; border: 1.5px solid #e5e7eb; border-radius: 7px; font-size: .82rem; outline: none; transition: border-color .2s; background: white; }
.filter-select:focus, .filter-input:focus { border-color: #3b82f6; }

/* ── Results summary ─────────────────────────────────────────────────────────── */
.results-summary { display: flex; align-items: center; justify-content: space-between; }
.summary-card { display: flex; align-items: center; gap: .4rem; font-size: .85rem; color: #374151; }
.summary-count { font-size: 1.1rem; font-weight: 700; color: #1d4ed8; }
.summary-divider { color: #d1d5db; }
.summary-time { color: #9ca3af; font-size: .78rem; }
.summary-page { font-size: .78rem; color: #9ca3af; }

/* ── Documents grid ──────────────────────────────────────────────────────────── */
.documents-grid { display: flex; flex-direction: column; gap: .75rem; }
.document-card { background: white; border-radius: 12px; border: 1px solid #e5e7eb; box-shadow: 0 1px 4px rgba(0,0,0,.05); transition: all .2s; }
.document-card:hover { border-color: #bfdbfe; box-shadow: 0 4px 16px rgba(37,99,235,.08); }
.card-content { display: flex; align-items: flex-start; gap: 1rem; padding: 1rem 1.25rem; }
.doc-info { flex: 1; min-width: 0; }
.doc-header { display: flex; gap: .875rem; align-items: flex-start; }
.file-icon-box { width: 42px; height: 42px; background: #f0f9ff; border: 1px solid #bae6fd; border-radius: 10px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
.icon-emoji { font-size: 1.4rem; }
.doc-details { flex: 1; min-width: 0; }
.doc-title { font-size: .95rem; font-weight: 700; color: #111827; margin-bottom: .2rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.doc-description { font-size: .8rem; color: #6b7280; margin-bottom: .35rem; }
.highlights { display: flex; flex-direction: column; gap: .2rem; margin-bottom: .35rem; }
.highlight-item { font-size: .76rem; background: #fef9c3; border-left: 3px solid #fbbf24; padding: .2rem .5rem; border-radius: 0 4px 4px 0; color: #374151; font-style: italic; }
.metadata-row { display: flex; flex-wrap: wrap; gap: .4rem; align-items: center; margin-bottom: .3rem; }
.meta-item { font-size: .75rem; color: #9ca3af; }
.meta-highlight { color: #d97706; font-weight: 600; }
.category-badge { font-size: .72rem; font-weight: 600; background: linear-gradient(135deg, #dbeafe, #e0e7ff); color: #1d4ed8; padding: .15rem .5rem; border-radius: 9999px; }
.tags-row { display: flex; flex-wrap: wrap; gap: .3rem; }
.tag { font-size: .72rem; color: #0369a1; background: #f0f9ff; border: 1px solid #bae6fd; padding: .1rem .4rem; border-radius: 5px; }
.tag-more { font-size: .72rem; color: #9ca3af; }

/* ── Card actions ────────────────────────────────────────────────────────────── */
.doc-actions { display: flex; flex-direction: column; align-items: flex-end; gap: .5rem; flex-shrink: 0; }
.score-wrapper { text-align: center; }
.score-circle { position: relative; width: 52px; height: 52px; }
.circle-svg { width: 100%; height: 100%; transform: rotate(-90deg); }
.circle-bg { fill: none; stroke: #e5e7eb; stroke-width: 12; }
.circle-progress { fill: none; stroke: #2563eb; stroke-width: 12; stroke-dasharray: 251; stroke-linecap: round; transition: stroke-dashoffset .8s ease; }
.score-text { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; }
.score-value { font-size: .65rem; font-weight: 800; color: #1d4ed8; }
.score-label { font-size: .62rem; color: #9ca3af; margin-top: .15rem; }
.view-btn { display: flex; align-items: center; gap: .35rem; padding: .45rem .875rem; background: #eff6ff; color: #1d4ed8; border: 1.5px solid #bfdbfe; border-radius: 8px; font-size: .78rem; font-weight: 600; cursor: pointer; transition: all .15s; white-space: nowrap; }
.view-btn svg { width: 14px; height: 14px; }
.view-btn:hover { background: #dbeafe; border-color: #93c5fd; }
.acl-btn { display: flex; align-items: center; gap: .35rem; padding: .45rem .875rem; background: #faf5ff; color: #7c3aed; border: 1.5px solid #ddd6fe; border-radius: 8px; font-size: .78rem; font-weight: 600; cursor: pointer; transition: all .15s; white-space: nowrap; }
.acl-btn svg { width: 14px; height: 14px; }
.acl-btn:hover { background: #ede9fe; }
.delete-btn { display: flex; align-items: center; gap: .35rem; padding: .45rem .875rem; background: #fef2f2; color: #dc2626; border: 1.5px solid #fecaca; border-radius: 8px; font-size: .78rem; font-weight: 600; cursor: pointer; transition: all .15s; white-space: nowrap; }
.delete-btn svg { width: 14px; height: 14px; }
.delete-btn:hover { background: #fee2e2; }

/* ── Pagination ──────────────────────────────────────────────────────────────── */
.pagination { display: flex; justify-content: center; gap: .375rem; flex-wrap: wrap; }
.page-btn { padding: .4rem .75rem; border-radius: 7px; border: 1.5px solid #e5e7eb; background: white; font-size: .82rem; cursor: pointer; transition: all .15s; }
.page-btn:hover { border-color: #3b82f6; color: #1d4ed8; }
.page-btn.active { background: #1d4ed8; color: white; border-color: #1d4ed8; font-weight: 700; }

/* ── State boxes ─────────────────────────────────────────────────────────────── */
.state-box { display: flex; flex-direction: column; align-items: center; gap: .75rem; padding: 4rem 2rem; background: white; border-radius: 14px; border: 1px solid #e5e7eb; text-align: center; color: #6b7280; }
.state-box h3 { font-size: 1.1rem; font-weight: 700; color: #111827; }
.state-icon { width: 56px; height: 56px; border-radius: 50%; background: #f3f4f6; display: flex; align-items: center; justify-content: center; }
.state-icon svg { width: 28px; height: 28px; color: #9ca3af; }
.clear-btn { padding: .5rem 1.25rem; background: #eff6ff; color: #1d4ed8; border: none; border-radius: 8px; font-size: .85rem; font-weight: 600; cursor: pointer; }

/* ── Buttons ────────────────────────────────────────────────────────────────── */
.btn-primary { display: flex; align-items: center; gap: .5rem; padding: .6rem 1.25rem; background: linear-gradient(135deg, #2563eb, #4f46e5); color: white; border: none; border-radius: 10px; font-size: .875rem; font-weight: 600; cursor: pointer; transition: all .2s; }
.btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 12px rgba(37,99,235,.4); }
.btn-primary:disabled { opacity: .6; cursor: not-allowed; }
.btn-primary svg { width: 18px; height: 18px; }
.btn-ghost { padding: .6rem 1.25rem; background: white; border: 1px solid #e2e8f0; border-radius: 10px; font-size: .875rem; cursor: pointer; color: #374151; transition: background .15s; }
.btn-ghost:hover { background: #f8fafc; }
.btn-icon-sm { display: inline-flex; align-items: center; justify-content: center; padding: .3rem .6rem; border-radius: 6px; font-size: .8rem; cursor: pointer; border: none; background: #f1f5f9; color: #374151; transition: all .15s; text-decoration: none; }
.btn-icon-sm:hover { background: #e2e8f0; }
.btn-icon-sm.danger { background: #fef2f2; color: #dc2626; }
.btn-icon-sm.danger:hover { background: #fee2e2; }

/* ── User/doc table (for Users tab) ─────────────────────────────────────────── */
.table-card { background: white; border-radius: 14px; border: 1px solid #e2e8f0; overflow: hidden; }
.doc-table { width: 100%; border-collapse: collapse; font-size: .875rem; }
.doc-table th { background: #f8fafc; padding: .75rem 1rem; text-align: left; font-size: .75rem; font-weight: 700; color: #64748b; text-transform: uppercase; letter-spacing: .05em; border-bottom: 1px solid #e2e8f0; }
.doc-table td { padding: .875rem 1rem; border-bottom: 1px solid #f1f5f9; vertical-align: middle; }
.doc-table tr:last-child td { border-bottom: none; }
.doc-table tr:hover td { background: #fafafa; }
.doc-cell { display: flex; align-items: center; gap: .625rem; }
.doc-name { font-weight: 600; color: #111827; }
.doc-filename { font-size: .8rem; color: #9ca3af; margin-top: .1rem; }
.user-cell { display: flex; align-items: center; gap: .625rem; }
.user-mini-avatar { width: 32px; height: 32px; background: linear-gradient(135deg, #2563eb, #8b5cf6); border-radius: 50%; color: white; font-size: .75rem; font-weight: 700; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
.user-mini-avatar.sm { width: 28px; height: 28px; font-size: .7rem; }
.role-tag { font-size: .75rem; font-weight: 600; padding: .2rem .625rem; border-radius: 9999px; }
.role-admin    { background: #fef3c7; color: #92400e; }
.role-manager  { background: #dbeafe; color: #1d4ed8; }
.role-user     { background: #d1fae5; color: #065f46; }
.role-readonly { background: #f1f5f9; color: #6b7280; }
.status-dot { display: inline-flex; align-items: center; font-size: .75rem; font-weight: 600; padding: .2rem .625rem; border-radius: 9999px; }
.status-dot.active   { background: #d1fae5; color: #065f46; }
.status-dot.danger   { background: #fef2f2; color: #b91c1c; }
.status-dot.warning  { background: #fef3c7; color: #92400e; }
.status-dot.inactive { background: #f1f5f9; color: #64748b; }
.muted { color: #6b7280; font-size: .85rem; }

/* ── Viewer Modal ────────────────────────────────────────────────────────────── */
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,.55); backdrop-filter: blur(4px); z-index: 500; display: flex; align-items: center; justify-content: center; padding: 1rem; }
.viewer-modal { background: white; border-radius: 18px; box-shadow: 0 30px 60px -12px rgba(0,0,0,.3); width: 100%; max-width: 1100px; height: 88vh; display: flex; flex-direction: column; overflow: hidden; }

/* Viewer header */
.viewer-header { display: flex; align-items: center; justify-content: space-between; padding: .875rem 1.25rem; border-bottom: 1px solid #e5e7eb; flex-shrink: 0; gap: 1rem; }
.viewer-header-left { display: flex; align-items: center; gap: .75rem; min-width: 0; }
.viewer-file-badge { background: linear-gradient(135deg, #dbeafe, #e0e7ff); color: #1d4ed8; font-size: .65rem; font-weight: 800; padding: .2rem .5rem; border-radius: 5px; text-transform: uppercase; flex-shrink: 0; }
.viewer-title-block { min-width: 0; }
.viewer-title { font-size: 1rem; font-weight: 700; color: #111827; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 400px; }
.viewer-filename { font-size: .75rem; color: #9ca3af; display: flex; align-items: center; gap: .3rem; flex-wrap: wrap; margin-top: .1rem; }
.vf-sep  { color: #d1d5db; }
.vf-cat  { background: #f0fdf4; color: #16a34a; font-weight: 600; padding: .1rem .4rem; border-radius: 5px; font-size: .7rem; }
.viewer-header-actions { display: flex; align-items: center; gap: .4rem; flex-shrink: 0; }

/* Header icon buttons */
.hdr-btn { display: flex; align-items: center; justify-content: center; width: 32px; height: 32px; border-radius: 7px; border: 1.5px solid #e5e7eb; background: white; cursor: pointer; transition: all .15s; color: #374151; text-decoration: none; }
.hdr-btn svg { width: 15px; height: 15px; }
.hdr-btn:hover { background: #f3f4f6; }
.hdr-download:hover { border-color: #93c5fd; color: #1d4ed8; background: #eff6ff; }
.hdr-acl:hover { border-color: #ddd6fe; color: #7c3aed; background: #faf5ff; }
.hdr-delete:hover { border-color: #fca5a5; color: #dc2626; background: #fef2f2; }

.tab-switcher { display: flex; gap: .25rem; background: #f1f5f9; border-radius: 8px; padding: .25rem; }
.tab-btn { display: flex; align-items: center; gap: .35rem; padding: .35rem .75rem; border-radius: 6px; border: none; background: none; font-size: .78rem; cursor: pointer; color: #64748b; transition: all .15s; }
.tab-btn svg { width: 14px; height: 14px; }
.tab-btn.active { background: white; color: #111827; font-weight: 600; box-shadow: 0 1px 3px rgba(0,0,0,.1); }
.hdr-close { display: flex; align-items: center; justify-content: center; width: 30px; height: 30px; background: none; border: none; color: #9ca3af; cursor: pointer; border-radius: 6px; transition: all .15s; }
.hdr-close svg { width: 18px; height: 18px; }
.hdr-close:hover { background: #fef2f2; color: #ef4444; }

/* Viewer body */
.viewer-body { flex: 1; display: grid; grid-template-columns: 1fr 320px; overflow: hidden; }
.viewer-preview-pane { border-right: 1px solid #e5e7eb; display: flex; flex-direction: column; overflow: hidden; position: relative; }
.preview-loading { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1rem; color: #9ca3af; font-size: .875rem; }
.pulse-ring { position: absolute; width: 60px; height: 60px; border-radius: 50%; border: 3px solid #bfdbfe; animation: pulse-out 1.5s ease-out infinite; }
@keyframes pulse-out { 0% { transform: scale(1); opacity: .8; } 100% { transform: scale(2); opacity: 0; } }
.pdf-frame { flex: 1; width: 100%; height: 100%; border: none; }
.pdf-viewer { flex: 1; display: flex; flex-direction: column; }
.image-viewer { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: .5rem; overflow: auto; padding: 1rem; background: #f8fafc; }
.document-image { max-width: 100%; max-height: 70vh; border-radius: 8px; box-shadow: 0 4px 16px rgba(0,0,0,.12); }
.image-hint { font-size: .75rem; color: #9ca3af; }
.text-viewer { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.text-toolbar { display: flex; gap: 1rem; padding: .5rem 1rem; background: #f8fafc; border-bottom: 1px solid #e5e7eb; font-size: .75rem; color: #6b7280; flex-shrink: 0; }
.text-content { flex: 1; overflow: auto; padding: 1rem; font-family: 'Fira Code', 'Consolas', monospace; font-size: .78rem; line-height: 1.7; color: #1e293b; background: #f8fafc; white-space: pre-wrap; word-break: break-all; margin: 0; }
.office-viewer { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.office-tabs { display: flex; gap: .5rem; padding: .5rem 1rem; border-bottom: 1px solid #e5e7eb; flex-shrink: 0; }
.otab { padding: .3rem .75rem; border-radius: 7px; border: 1.5px solid transparent; background: none; font-size: .78rem; cursor: pointer; color: #6b7280; transition: all .15s; }
.otab.active { border-color: #3b82f6; background: #eff6ff; color: #1d4ed8; font-weight: 600; }
.office-text-panel { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.office-text-wrap { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.office-text-stats { display: flex; gap: 1rem; padding: .5rem 1rem; font-size: .73rem; color: #9ca3af; border-bottom: 1px solid #e5e7eb; flex-shrink: 0; }
.office-text-content { flex: 1; overflow: auto; padding: 1rem; font-family: monospace; font-size: .78rem; line-height: 1.7; color: #1e293b; background: #f8fafc; white-space: pre-wrap; margin: 0; }
.office-no-text { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: .5rem; color: #9ca3af; text-align: center; padding: 2rem; }
.ont-icon { font-size: 3rem; }
.ont-title { font-size: .95rem; font-weight: 600; color: #374151; }
.ont-sub { font-size: .8rem; }
.office-embed-panel { flex: 1; padding: 1.5rem; display: flex; align-items: center; justify-content: center; }
.office-embed-notice { background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 9px; padding: 1.25rem; font-size: .83rem; color: #1d4ed8; display: flex; flex-direction: column; gap: .5rem; max-width: 360px; text-align: center; }
.office-embed-link { font-weight: 700; color: #1d4ed8; }
.audio-viewer { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2rem; padding: 2rem; background: linear-gradient(135deg, #0f172a, #1e293b); }
.audio-art { text-align: center; }
.audio-wave { display: flex; align-items: center; justify-content: center; gap: 3px; height: 70px; margin-bottom: 1rem; }
.wave-bar { width: 4px; border-radius: 2px; background: linear-gradient(to top, #3b82f6, #818cf8); height: 40%; animation: wave 1.2s ease-in-out infinite alternate; }
@keyframes wave { 0% { transform: scaleY(.3) } 100% { transform: scaleY(1) } }
.audio-player { width: 100%; max-width: 400px; border-radius: 9px; }
.video-viewer { flex: 1; display: flex; align-items: center; justify-content: center; background: #000; }
.video-player { max-width: 100%; max-height: 100%; }
.unsupported-viewer { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: .75rem; padding: 2rem; text-align: center; }
.unsupported-viewer h3 { font-size: 1.2rem; font-weight: 700; color: #111827; }
.unsupported-viewer p  { color: #6b7280; }
.unsupported-viewer code { font-size: .75rem; font-family: monospace; background: #f3f4f6; padding: .3rem .65rem; border-radius: 5px; color: #374151; }

/* Details pane */
.viewer-details-pane { overflow-y: auto; display: flex; flex-direction: column; background: #fafafa; }

/* OCR status bar */
.ocr-status-bar { display: flex; align-items: center; gap: .5rem; padding: .55rem 1rem; font-size: .76rem; font-weight: 600; flex-shrink: 0; }
.ocr-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
.ocr-done    { background: #d1fae5; color: #065f46; border-bottom: 1px solid #a7f3d0; }
.ocr-done    .ocr-dot { background: #10b981; }
.ocr-fail    { background: #fee2e2; color: #991b1b; border-bottom: 1px solid #fca5a5; }
.ocr-fail    .ocr-dot { background: #ef4444; }
.ocr-pending { background: #fef3c7; color: #92400e; border-bottom: 1px solid #fde68a; }
.ocr-pending .ocr-dot { background: #f59e0b; animation: blink 1s ease infinite; }
.ocr-partial { background: #eff6ff; color: #1d4ed8; border-bottom: 1px solid #bfdbfe; }
.ocr-partial .ocr-dot { background: #3b82f6; animation: blink 1.2s ease infinite; }
@keyframes blink { 0%,100% { opacity: 1 } 50% { opacity: .3 } }

/* Detail sections */
.detail-section { padding: .875rem 1rem; border-bottom: 1px solid #f0f0f0; }
.detail-section:last-child { border-bottom: none; }
.detail-section-title { display: flex; align-items: center; gap: .4rem; font-size: .75rem; font-weight: 800; color: #374151; text-transform: uppercase; letter-spacing: .05em; margin-bottom: .7rem; }
.detail-section-title svg { width: 13px; height: 13px; color: #6b7280; }
.detail-list { display: flex; flex-direction: column; gap: .45rem; }
.dl-row { display: grid; grid-template-columns: 80px 1fr; gap: .45rem; align-items: start; }
.dl-row dt { font-size: .73rem; font-weight: 600; color: #9ca3af; padding-top: .1rem; }
.dl-row dd { font-size: .8rem; color: #111827; word-break: break-all; }
.dd-mono   { font-family: monospace; font-size: .73rem; color: #374151; }
.dd-accent { font-weight: 600; color: #047857; }
.mime-badge { display: inline-block; background: #eff6ff; color: #1d4ed8; font-size: .65rem; font-weight: 800; padding: .1rem .4rem; border-radius: 4px; text-transform: uppercase; margin-right: .3rem; }
.mime-text  { font-size: .7rem; color: #6b7280; font-family: monospace; }
.tags-cloud { display: flex; flex-wrap: wrap; gap: .35rem; }
.tag-cloud-item { padding: .22rem .55rem; background: #f0f9ff; border: 1px solid #bae6fd; color: #0369a1; font-size: .73rem; font-weight: 600; border-radius: 6px; }

/* ── Admin Edit section in viewer ─────────────────────────────────────────── */
.admin-edit-section { background: #fffbeb; border-bottom: 2px solid #fde68a !important; }
.admin-edit-title { color: #92400e !important; }
.admin-edit-title svg { color: #d97706 !important; }
.edit-form { display: flex; flex-direction: column; gap: .6rem; }
.edit-field { display: flex; flex-direction: column; gap: .25rem; }
.edit-label { font-size: .73rem; font-weight: 600; color: #374151; }
.edit-hint { font-weight: 400; color: #9ca3af; font-size: .68rem; }
.edit-input { width: 100%; padding: .45rem .625rem; border: 1.5px solid #e2e8f0; border-radius: 7px; font-size: .8rem; outline: none; transition: border-color .2s; background: white; font-family: inherit; }
.edit-input:focus { border-color: #f59e0b; box-shadow: 0 0 0 2px rgba(245,158,11,.1); }
.edit-textarea { resize: vertical; min-height: 52px; }
.edit-save-btn { display: flex; align-items: center; justify-content: center; gap: .4rem; padding: .55rem 1rem; background: linear-gradient(135deg, #d97706, #b45309); color: white; border: none; border-radius: 8px; font-size: .8rem; font-weight: 700; cursor: pointer; transition: all .2s; margin-top: .25rem; }
.edit-save-btn:hover:not(:disabled) { box-shadow: 0 4px 12px rgba(180,83,9,.35); transform: translateY(-1px); }
.edit-save-btn:disabled { opacity: .6; cursor: not-allowed; }
.edit-save-btn svg { width: 14px; height: 14px; }
.edit-banner { padding: .5rem .75rem; border-radius: 7px; font-size: .78rem; margin-bottom: .25rem; }
.edit-banner.success { background: #f0fdf4; color: #166534; border: 1px solid #bbf7d0; }
.edit-banner.error   { background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; }

/* ── Mobile tab switching ────────────────────────────────────────────────────── */
@media (min-width: 900px) { .tab-hidden { display: flex !important; } .tab-switcher { display: none; } }
@media (max-width: 899px) {
  .viewer-body { grid-template-columns: 1fr; }
  .viewer-preview-pane { border-right: none; border-bottom: 1px solid #e5e7eb; height: 55vh; }
  .viewer-details-pane { height: auto; max-height: 35vh; }
  .tab-hidden { display: none !important; }
}

/* ── Upload modal ────────────────────────────────────────────────────────────── */
.modal { background: white; border-radius: 18px; box-shadow: 0 25px 50px -12px rgba(0,0,0,.2); width: 100%; max-width: 520px; max-height: 90vh; overflow-y: auto; }
.modal-large { max-width: 56rem; }

.files-preview-list { display: flex; flex-direction: column; gap: .5rem; max-height: 300px; overflow-y: auto; }
.file-preview-item { display: flex; align-items: center; gap: .75rem; padding: .6rem .75rem; background: #f9fafb; border-radius: 8px; border: 1px solid #e5e7eb; }
.file-preview-item .file-emoji { font-size: 1.5rem; }
.file-preview-item .doc-name { font-weight: 500; color: #1f2937; font-size: .875rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.file-preview-item .muted { font-size: .75rem; color: #6b7280; }

.batch-category-section { margin-top: 1rem; padding-top: 1rem; border-top: 1px solid #e5e7eb; }
.batch-info { font-size: .8rem; color: #6b7280; margin-bottom: .75rem; }
.modal-wide { max-width: 680px; }
.modal-header { display: flex; align-items: flex-start; justify-content: space-between; padding: 1.25rem 1.5rem; border-bottom: 1px solid #f1f5f9; }
.modal-header h2 { font-size: 1.125rem; font-weight: 700; color: #111827; }
.modal-subtitle { font-size: .8rem; color: #6b7280; margin-top: .2rem; }
.close-btn { background: none; border: none; font-size: 1.125rem; color: #9ca3af; cursor: pointer; padding: .25rem; }
.modal-body { padding: 1.25rem 1.5rem; display: flex; flex-direction: column; gap: 1rem; }
.modal-footer { display: flex; justify-content: flex-end; gap: .75rem; padding-top: .5rem; }
.form-row { display: flex; flex-direction: column; gap: .375rem; }
.form-label { font-size: .8rem; font-weight: 600; color: #374151; }
.form-input { padding: .625rem .875rem; border: 1.5px solid #e2e8f0; border-radius: 8px; font-size: .875rem; outline: none; transition: border-color .2s; }
.form-input:focus { border-color: #3b82f6; }
.form-input.short { max-width: 160px; }
.drop-zone { border: 2px dashed #cbd5e1; border-radius: 12px; padding: 2.5rem; text-align: center; cursor: pointer; transition: all .2s; }
.drop-zone:hover { border-color: #3b82f6; background: #eff6ff; }
.drop-icon { font-size: 2.5rem; margin-bottom: .5rem; }
.drop-text { font-weight: 600; color: #374151; }
.drop-sub { font-size: .8rem; color: #9ca3af; }
.hidden-input { display: none; }
.file-preview { display: flex; align-items: center; gap: .875rem; padding: .875rem; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px; }
.file-emoji { font-size: 1.75rem; }

/* ── ACL ─────────────────────────────────────────────────────────────────────── */
.section-label { font-size: .875rem; font-weight: 700; color: #374151; margin-bottom: .75rem; }
.acl-form { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 1rem; display: flex; flex-direction: column; gap: .75rem; }
.acl-form-row { display: flex; gap: .625rem; flex-wrap: wrap; }
.access-type-toggle { display: flex; gap: .5rem; }
.toggle-btn { padding: .4rem .875rem; border: 1.5px solid #e2e8f0; border-radius: 8px; background: white; font-size: .8rem; cursor: pointer; transition: all .15s; color: #374151; }
.toggle-btn.active { border-color: #3b82f6; background: #eff6ff; color: #1d4ed8; font-weight: 600; }
.acl-list { margin-top: .5rem; }
.acl-entries { display: flex; flex-direction: column; gap: .625rem; }
.acl-entry { display: flex; align-items: center; gap: .875rem; padding: .75rem; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px; }
.acl-entry.expired { opacity: .6; }
.acl-user-info { display: flex; align-items: center; gap: .5rem; flex: 1; }
.acl-meta { display: flex; align-items: center; gap: .5rem; flex-wrap: wrap; }
.perm-badge     { font-size: .75rem; font-weight: 600; background: #dbeafe; color: #1d4ed8; padding: .15rem .5rem; border-radius: 9999px; }
.perm-permanent { font-size: .75rem; background: #d1fae5; color: #065f46; padding: .15rem .5rem; border-radius: 9999px; }
.perm-expiry    { font-size: .75rem; background: #fef3c7; color: #92400e; padding: .15rem .5rem; border-radius: 9999px; }
.perm-expired   { font-size: .75rem; background: #fef2f2; color: #b91c1c; padding: .15rem .5rem; border-radius: 9999px; }
.empty-acl { font-size: .875rem; color: #9ca3af; text-align: center; padding: 1rem; background: #f8fafc; border-radius: 10px; }

/* ── Misc ────────────────────────────────────────────────────────────────────── */
.loading-state { display: flex; align-items: center; gap: .75rem; padding: 2rem; justify-content: center; color: #9ca3af; }
.spinner-ring { width: 24px; height: 24px; border: 3px solid #e2e8f0; border-top-color: #3b82f6; border-radius: 50%; animation: spin .7s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
.banner { padding: .75rem 1rem; border-radius: 8px; font-size: .875rem; }
.banner.error   { background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; }
.banner.success { background: #f0fdf4; color: #166534; border: 1px solid #bbf7d0; }
.rag-redirect { background: white; border-radius: 14px; border: 1px solid #e2e8f0; padding: 3rem; text-align: center; display: flex; flex-direction: column; align-items: center; gap: 1.5rem; color: #6b7280; }
.spinner { animation: spin 1s linear infinite; }
.spinner.xl { width: 38px; height: 38px; }
.spinner-bg   { opacity: .25; }
.spinner-path { opacity: .75; }

/* ── Autocomplete ── */
.autocomplete-dropdown { position:absolute; top:calc(100% + 4px); left:0; right:0; background:white; border:1.5px solid #e5e7eb; border-radius:10px; box-shadow:0 8px 24px rgba(0,0,0,.12); z-index:100; overflow:hidden; }
.autocomplete-item { display:flex; align-items:center; gap:.5rem; padding:.6rem 1rem; font-size:.85rem; color:#374151; cursor:pointer; transition:background .1s; }
.autocomplete-item:hover,.ac-active { background:#eff6ff; color:#1d4ed8; }

/* ── NLP banner ── */
.nlp-banner { display:flex; align-items:center; gap:.5rem; margin-top:.75rem; padding:.5rem .9rem; background:linear-gradient(135deg,#f0f9ff,#e0f2fe); border:1px solid #bae6fd; border-radius:8px; font-size:.8rem; color:#0369a1; }
.nlp-icon { font-size:1rem; flex-shrink:0; }
.nlp-text { flex:1; }
.nlp-text strong { font-weight:700; }
.nlp-dismiss { background:none; border:none; color:#0369a1; cursor:pointer; font-size:.85rem; padding:.1rem .3rem; border-radius:4px; opacity:.6; }
.nlp-dismiss:hover { opacity:1; background:rgba(3,105,161,.1); }

.search-error-banner {    display: flex;    align-items: center;    justify-content: center;    margin-top: 2rem;    padding: 1rem 1.5rem;    background: #fef2f2;    border: 1px solid #fecaca;    border-radius: 8px;    color: #b91c1c;    font-size: 0.95rem;    gap: 0.5rem;  }
.search-error-banner::before { content: '⚠️  '; }

/* ── OCR / quality / service badges on cards ── */
.ocr-badge { display:inline-flex; align-items:center; gap:.2rem; padding:.14rem .42rem; border-radius:5px; font-size:.68rem; font-weight:700; }
.ocr-badge-done    { background:#d1fae5; color:#065f46; border:1px solid #6ee7b7; }
.ocr-badge-pending { background:#fef3c7; color:#92400e; border:1px solid #fde68a; }
.ocr-badge-fail    { background:#fee2e2; color:#dc2626; border:1px solid #fecaca; }
.ocr-quality-badge { padding:.14rem .42rem; border-radius:5px; font-size:.66rem; font-weight:700; }
.oq-good   { background:#d1fae5; color:#065f46; }
.oq-medium { background:#fef3c7; color:#92400e; }
.oq-low    { background:#fee2e2; color:#991b1b; }
.service-badge { padding:.14rem .42rem; background:#f3e8ff; color:#6b21a8; border-radius:5px; font-size:.68rem; font-weight:600; }

/* ── Auto-summary on cards ── */
.doc-summary-row { margin-top:.45rem; }
.summary-toggle-btn { background:none; border:none; color:#2563eb; font-size:.74rem; font-weight:600; cursor:pointer; padding:0; }
.summary-toggle-btn:hover { text-decoration:underline; }
.doc-summary-text { margin-top:.4rem; font-size:.78rem; color:#374151; line-height:1.6; background:#f8fafc; border-left:3px solid #3b82f6; padding:.45rem .65rem; border-radius:0 6px 6px 0; }
.ocr-progress-bar { height:20px; background:#f1f5f9; border-radius:10px; position:relative; overflow:hidden; margin:6px 0; border:1px solid #e2e8f0; }
.ocr-progress-fill { height:100%; background:linear-gradient(90deg,#0ea5e9,#38bdf8); border-radius:10px; transition:width .6s ease-out; position:relative; }
.ocr-progress-fill::before { content:''; position:absolute; top:0; left:0; right:0; bottom:0; background:linear-gradient(90deg,transparent,rgba(255,255,255,0.25),transparent); animation:shimmer 1.5s infinite; }
@keyframes shimmer { 0% { transform:translateX(-100%); } 100% { transform:translateX(100%); } }
.ocr-progress-label { position:absolute; top:50%; left:50%; transform:translate(-50%,-50%); font-size:.65rem; font-weight:600; color:#0c4a6e; letter-spacing:0.2px; }

/* OCR Status Indicator (replaces progress bar) */
.pipeline-status-indicator { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border-radius: 12px; font-size: 0.7rem; font-weight: 600; margin: 4px 0; }
.pipeline-status-indicator.status-pending { background: #f1f5f9; color: #64748b; }
.pipeline-status-indicator.status-pending .pipeline-status-dot { background: #94a3b8; }
.pipeline-status-indicator.status-processing { background: #fef3c7; color: #b45309; }
.pipeline-status-indicator.status-processing .pipeline-status-dot { background: #f59e0b; animation: pulse 1.5s infinite; }
.pipeline-status-indicator.status-completed { background: #dcfce7; color: #15803d; }
.pipeline-status-indicator.status-completed .pipeline-status-dot { background: #22c55e; }
.pipeline-status-indicator.status-failed { background: #fee2e2; color: #dc2626; }
.pipeline-status-indicator.status-failed .pipeline-status-dot { background: #ef4444; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }

/* ── Filters reset ── */
.filters-reset-row { margin-top:.75rem; display:flex; justify-content:flex-end; }
.filters-reset-btn { background:none; border:1px solid #fca5a5; color:#dc2626; border-radius:7px; padding:.28rem .75rem; font-size:.76rem; font-weight:600; cursor:pointer; transition:all .15s; }
.filters-reset-btn:hover { background:#fee2e2; }

/* ── Access tab overview ─────────────────────────────────────────────── */
.access-overview-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 1rem; margin-top: .5rem; }
.access-card { display: flex; align-items: center; gap: 1rem; background: white; border: 1px solid #e5e7eb; border-radius: 14px; padding: 1.25rem 1.5rem; cursor: pointer; transition: all .15s; }
.access-card:hover { border-color: #bfdbfe; box-shadow: 0 4px 14px rgba(37,99,235,.1); transform: translateY(-1px); }
.access-card-icon { width: 48px; height: 48px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 1.5rem; flex-shrink: 0; }
.access-card-title { font-weight: 700; color: #111827; font-size: .95rem; }
.access-card-desc  { font-size: .78rem; color: #6b7280; margin-top: .15rem; }
.access-card-arrow { width: 18px; height: 18px; color: #9ca3af; margin-left: auto; flex-shrink: 0; }

/* ── Access Dashboard ─────────────────────────────────────────────────────── */
.access-dashboard { display: flex; flex-direction: column; gap: 1.5rem; }

/* KPI row */
.access-kpi-row { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; }
.access-kpi-card {
  display: flex; align-items: center; gap: 1rem;
  background: white; border: 1px solid #e5e7eb; border-radius: 14px;
  padding: 1rem 1.25rem; cursor: pointer;
  transition: all 0.15s; box-shadow: 0 1px 3px rgba(0,0,0,.04);
}
.access-kpi-card:hover { border-color: #93c5fd; box-shadow: 0 4px 12px rgba(37,99,235,.08); transform: translateY(-1px); }
.access-kpi-card.akpi-warning { border-color: #fed7aa; }
.akpi-icon { width: 40px; height: 40px; border-radius: 10px; display: flex; align-items: center; justify-content: center; font-size: 1.2rem; flex-shrink: 0; }
.akpi-body { flex: 1; }
.akpi-value { font-size: 1.5rem; font-weight: 800; color: #111827; line-height: 1; }
.akpi-label { font-size: 0.78rem; color: #6b7280; margin-top: 0.15rem; }
.akpi-arrow { width: 16px; height: 16px; color: #9ca3af; flex-shrink: 0; }
/* Preview card */
.access-preview-card {
  background: white; border: 1px solid #e5e7eb; border-radius: 14px;
  padding: 1.25rem 1.5rem; box-shadow: 0 1px 3px rgba(0,0,0,.04);
}
.apc-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 1rem; }
.apc-title { font-size: 0.95rem; font-weight: 700; color: #111827; display: flex; align-items: center; gap: 0.5rem; }
.apc-see-all { font-size: 0.8rem; color: #2563eb; background: none; border: none; cursor: pointer; font-weight: 600; }
.apc-see-all:hover { text-decoration: underline; }
.apc-loading { color: #9ca3af; font-size: 0.875rem; display: flex; align-items: center; gap: 0.5rem; padding: 1rem 0; }
.apc-empty { color: #9ca3af; font-size: 0.875rem; text-align: center; padding: 1.5rem 0; }
/* Group rows */
.apc-groups-list { display: flex; flex-direction: column; gap: 0.5rem; }
.apc-group-row {
  display: flex; align-items: center; gap: 0.875rem;
  padding: 0.65rem 0.85rem; border-radius: 10px;
  border: 1px solid #f3f4f6; cursor: pointer;
  transition: all 0.12s;
}
.apc-group-row:hover { background: #f8faff; border-color: #dbeafe; }
.apc-group-icon { width: 34px; height: 34px; border-radius: 8px; display: flex; align-items: center; justify-content: center; font-size: 1rem; flex-shrink: 0; }
.apc-group-info { flex: 1; min-width: 0; }
.apc-group-name { font-size: 0.875rem; font-weight: 600; color: #111827; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.apc-group-meta { font-size: 0.75rem; color: #6b7280; display: flex; gap: 0.3rem; flex-wrap: wrap; }
.apc-dot { color: #d1d5db; }
.apc-chevron { width: 14px; height: 14px; color: #9ca3af; flex-shrink: 0; }
.apc-more { font-size: 0.78rem; color: #6b7280; text-align: center; padding: 0.5rem 0 0; }

/* Alert card */
.access-alert-card {
  display: flex; align-items: center; gap: 1rem;
  background: #fff7ed; border: 1px solid #fed7aa; border-radius: 14px;
  padding: 1rem 1.25rem;
}
.alert-icon { font-size: 1.5rem; flex-shrink: 0; }
.alert-title { font-size: 0.9rem; font-weight: 700; color: #9a3412; }
.alert-desc  { font-size: 0.8rem; color: #c2410c; margin-top: 0.15rem; }

/* Roles */
.apc-roles-grid { display: flex; flex-direction: column; gap: 0.75rem; }
.apc-role-row { display: flex; align-items: center; gap: 1rem; }
.apc-role-bar-wrap { display: flex; align-items: center; gap: 0.5rem; flex: 1; }
.apc-role-bar { flex: 1; height: 6px; background: #f3f4f6; border-radius: 99px; overflow: hidden; }
.apc-role-fill { height: 100%; border-radius: 99px; transition: width 0.4s; }
.rfill-admin    { background: #f59e0b; }
.rfill-manager  { background: #3b82f6; }
.rfill-user     { background: #22c55e; }
.rfill-readonly { background: #94a3b8; }
.apc-role-count { font-size: 0.8rem; font-weight: 600; color: #374151; min-width: 24px; text-align: right; }

/* ── Stats tab ── */
.stats-section { display:flex; flex-direction:column; gap:1.25rem; }
.btn-secondary { display:inline-flex; align-items:center; gap:.4rem; padding:.55rem 1.1rem; background:white; border:1.5px solid #e5e7eb; color:#374151; border-radius:9px; font-size:.85rem; font-weight:600; cursor:pointer; transition:all .15s; }
.btn-secondary:hover:not(:disabled) { border-color:#3b82f6; color:#1d4ed8; }
.btn-secondary:disabled { opacity:.5; cursor:not-allowed; }
.stats-kpi-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:1rem; }
.kpi-card { background:white; border-radius:12px; border:1px solid #e5e7eb; padding:1.25rem 1.5rem; display:flex; align-items:center; gap:1rem; box-shadow:0 2px 8px rgba(0,0,0,.05); }
.kpi-icon { font-size:2rem; }
.kpi-label { font-size:.75rem; color:#6b7280; font-weight:600; margin-bottom:.2rem; }
.kpi-value { font-size:1.75rem; font-weight:800; }
.kpi-blue   .kpi-value { color:#2563eb; }
.kpi-green  .kpi-value { color:#059669; }
.kpi-amber  .kpi-value { color:#d97706; }
.kpi-purple .kpi-value { color:#7c3aed; }
.stats-card { background:white; border-radius:12px; border:1px solid #e5e7eb; padding:1.5rem; box-shadow:0 2px 8px rgba(0,0,0,.05); }
.stats-card-title { display:flex; align-items:center; gap:.5rem; font-size:.9rem; font-weight:700; color:#111827; margin-bottom:.5rem; }
.stats-card-desc { font-size:.82rem; color:#6b7280; margin-bottom:1rem; line-height:1.6; }
.stats-empty { font-size:.85rem; color:#6b7280; padding:.75rem 0; }
.reindex-btn { display:inline-flex; align-items:center; gap:.5rem; padding:.65rem 1.3rem; background:linear-gradient(135deg,#0f172a,#1e293b); color:white; border:none; border-radius:9px; font-weight:600; font-size:.875rem; cursor:pointer; transition:all .2s; }
.reindex-btn:hover:not(:disabled) { box-shadow:0 6px 14px rgba(15,23,42,.3); }
.reindex-btn:disabled { opacity:.6; cursor:not-allowed; }
.reindex-msg { font-size:.83rem; font-weight:600; color:#059669; }
.ocr-queue-list { display:flex; flex-direction:column; gap:.5rem; margin-top:.75rem; }
.ocr-queue-item { display:flex; align-items:center; gap:.75rem; padding:.6rem .875rem; background:#fffbeb; border:1px solid #fde68a; border-radius:9px; }
.oq-icon { font-size:1.25rem; flex-shrink:0; }
.oq-info { flex:1; min-width:0; }
.oq-title { font-size:.83rem; font-weight:600; color:#111827; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.oq-meta  { font-size:.73rem; color:#6b7280; }
.oq-status-badge { font-size:.73rem; font-weight:700; background:#fef3c7; color:#92400e; padding:.2rem .55rem; border-radius:5px; white-space:nowrap; }
.role-breakdown { display:flex; flex-direction:column; gap:.75rem; margin-top:.75rem; }
.role-row { display:flex; align-items:center; gap:1rem; }
.role-name-pill { min-width:110px; font-size:.73rem; font-weight:700; padding:.2rem .55rem; border-radius:9999px; text-align:center; }
.role-bar-wrap { flex:1; display:flex; align-items:center; gap:.75rem; }
.role-bar { flex:1; height:8px; background:#f1f5f9; border-radius:4px; overflow:hidden; }
.role-bar-fill { height:100%; border-radius:4px; transition:width .6s ease; }
.rfill-admin   { background:linear-gradient(90deg,#f59e0b,#d97706); }
.rfill-manager { background:linear-gradient(90deg,#3b82f6,#2563eb); }
.rfill-user    { background:linear-gradient(90deg,#10b981,#059669); }
.rfill-readonly { background:#94a3b8; }
.role-count { font-size:.8rem; font-weight:700; color:#374151; min-width:24px; text-align:right; }

/* ── Stats charts row ── */
.stats-charts-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 1.25rem; }
.stats-chart-card { min-height: 320px; display: flex; flex-direction: column; }
.stats-recent-card { min-height: 320px; display: flex; flex-direction: column; overflow: hidden; }
.stats-recent-card :deep(.recent-docs-widget) { flex: 1; display: flex; flex-direction: column; }
.stats-recent-card :deep(.widget-list) { flex: 1; overflow-y: auto; max-height: 320px; }

/* ── RAG toggle button ── */
.rag-trigger-badge { font-size:.65rem; padding:.15rem .4rem; background:rgba(255,255,255,0.2); border-radius:4px; font-weight:500; max-width:120px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
/* ── Elise answer panel identity ─────────────────────────────── */
.rag-answer-panel { background:white; border:2px solid #e8edff; border-radius:14px; padding:0; overflow:hidden; }

.rag-answer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: .875rem 1.25rem;
  background: linear-gradient(135deg, #1a2b4a 0%, #2563eb 100%);
  color: white;
}

.elise-avatar-row { display:flex; align-items:center; gap:.75rem; }

.elise-avatar {
  width: 36px;
  height: 36px;
  background: rgba(255,255,255,0.15);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.1rem;
  flex-shrink: 0;
  border: 2px solid rgba(255,255,255,0.3);
}

.elise-name { font-weight:700; font-size:.95rem; display:block; letter-spacing:.01em; }
.elise-subtitle { font-size:.7rem; opacity:.75; display:block; margin-top:1px; }

.rag-header-actions { display:flex; align-items:center; gap:.75rem; }

.rag-source-count {
  font-size:.72rem;
  background: rgba(255,255,255,0.2);
  color: white;
  padding:.2rem .65rem;
  border-radius:999px;
  font-weight:600;
}

.rag-close {
  background: rgba(255,255,255,0.15);
  border: none;
  color: white;
  width: 26px;
  height: 26px;
  border-radius: 50%;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: .8rem;
  transition: background .2s;
}
.rag-close:hover { background: rgba(255,255,255,0.3); }

/* Part 1 — Elise conversational intro */
.elise-intro-block {
  padding: .875rem 1.25rem .5rem;
  border-bottom: 1px solid #f0f4ff;
  background: #f8faff;
}

.elise-intro-text {
  font-size: .875rem;
  color: #2563eb;
  font-weight: 600;
  margin: 0;
  display: flex;
  align-items: center;
  gap: .4rem;
}

.elise-intro-text::before {
  content: '—';
  opacity: .5;
}

/* Part 2 — Actual query result */
.elise-result-block {
  padding: .875rem 1.25rem 1rem;
}

.rag-answer-text { margin:0; font-size:.9rem; color:#1f2937; line-height:1.7; white-space:pre-wrap; }

.rag-thinking {
  display: flex;
  align-items: center;
  gap: .6rem;
  padding: 1rem 1.25rem;
  font-size: .875rem;
  color: #6b7280;
}
.elise-attach-wrapper { position: relative; }
.elise-attach-btn { padding: .65rem .9rem; border: 2px dashed #a5b4fc; border-radius: 9px; background: #f5f3ff; color: #4f46e5; font-size: .82rem; font-weight: 600; cursor: pointer; white-space: nowrap; transition: all .2s; }
.elise-attach-btn:hover { background: #ede9fe; border-color: #6366f1; }
.elise-attach-btn.has-attachments { background: #e0e7ff; border-color: #4f46e5; border-style: solid; }
.elise-doc-picker { position: absolute; top: calc(100% + 8px); left: 0; z-index: 100; background: white; border: 1.5px solid #e0e7ff; border-radius: 12px; padding: 1rem; width: 320px; box-shadow: 0 8px 24px rgba(0,0,0,.1); }
.picker-label { font-size: .78rem; font-weight: 600; color: #6b7280; margin: 0 0 .6rem; }
.picker-list { max-height: 240px; overflow-y: auto; display: flex; flex-direction: column; gap: .3rem; }
.picker-item { display: flex; align-items: center; gap: .5rem; padding: .45rem .5rem; border-radius: 7px; cursor: pointer; font-size: .85rem; color: #1f2937; }
.picker-item:hover { background: #f5f3ff; }
.picker-item input { accent-color: #4f46e5; width: 15px; height: 15px; flex-shrink: 0; }
.picker-icon { font-size: 1rem; flex-shrink: 0; }
.picker-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.picker-empty { font-size: .82rem; color: #9ca3af; text-align: center; padding: .5rem; }
.picker-close-btn { margin-top: .75rem; width: 100%; padding: .4rem; border: 1px solid #e5e7eb; border-radius: 7px; background: white; color: #6b7280; font-size: .8rem; cursor: pointer; }
.picker-close-btn:hover { background: #f9fafb; }

/* Sources grid (unchanged layout, minor polish) */
.rag-sources-grid { display:flex; flex-direction:column; gap:.5rem; padding:.75rem 1.25rem 1.25rem; }
.rag-badge { background:linear-gradient(135deg,#2563eb,#4f46e5); color:white; font-size:.72rem; font-weight:700; padding:.2rem .75rem; border-radius:999px; }
.rag-source-count { font-size:.8rem; color:#6b7280; }
.rag-close { margin-left:auto; background:none; border:none; cursor:pointer; color:#9ca3af; font-size:1rem; }
.rag-thinking { display:flex; align-items:center; gap:.5rem; color:#9ca3af; font-size:.85rem; }
.rag-answer-text { color:#111827; line-height:1.75; white-space:pre-wrap; font-size:.9rem; }
.rag-sources-grid { margin-top:1rem; display:flex; flex-direction:column; gap:.5rem; border-top:1px solid #e0e7ff; padding-top:.875rem; }
.rag-source-chip { display:flex; align-items:flex-start; gap:.75rem; background:#f8faff; border:1px solid #e0e7ff; border-radius:10px; padding:.75rem; }
.src-num { width:22px; height:22px; flex-shrink:0; background:linear-gradient(135deg,#2563eb,#4f46e5); color:white; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:.68rem; font-weight:700; margin-top:.1rem; }
.src-body { flex:1; min-width:0; }
.src-title { font-size:.83rem; font-weight:600; color:#111827; }
.src-meta { font-size:.73rem; color:#6b7280; margin-top:.1rem; }
.src-excerpt { font-size:.75rem; color:#6b7280; font-style:italic; margin-top:.25rem; line-height:1.5; }
.src-view-btn { flex-shrink:0; padding:.3rem .65rem; background:#eff6ff; color:#1d4ed8; border:1px solid #bfdbfe; border-radius:7px; font-size:.75rem; font-weight:600; cursor:pointer; white-space:nowrap; }
.src-view-btn:hover { background:#dbeafe; }

/* ── Elise doc-picker modal (teleport) ──────────────────────────────────────── */
.picker-overlay { position: fixed; inset: 0; background: rgba(0,0,0,.45); z-index: 1000; display: flex; align-items: center; justify-content: center; }
.picker-modal { background: white; border-radius: 16px; width: 480px; max-width: 95vw; max-height: 80vh; display: flex; flex-direction: column; box-shadow: 0 20px 60px rgba(0,0,0,.2); overflow: hidden; }
.picker-modal-header { display: flex; align-items: center; justify-content: space-between; padding: 1.25rem 1.5rem; border-bottom: 1px solid #f0f0f0; }
.picker-modal-title { font-size: 1rem; font-weight: 700; color: #1f2937; margin: 0; }
.picker-modal-close { background: none; border: none; cursor: pointer; font-size: 1.1rem; color: #6b7280; padding: .25rem; border-radius: 6px; }
.picker-modal-close:hover { background: #f3f4f6; }
.picker-modal-search { display: flex; align-items: center; gap: .5rem; padding: .75rem 1.5rem; border-bottom: 1px solid #f0f0f0; }
.picker-modal-search-input { flex: 1; border: none; outline: none; font-size: .9rem; color: #374151; }
.picker-modal-search-clear { background: none; border: none; cursor: pointer; color: #9ca3af; font-size: .85rem; padding: 0 .25rem; }
.picker-modal-body { flex: 1; overflow-y: auto; padding: .75rem 1rem; display: flex; flex-direction: column; gap: .35rem; }
.picker-modal-empty { text-align: center; color: #9ca3af; font-size: .9rem; padding: 2rem 0; }
.picker-modal-item { display: flex; align-items: center; gap: .75rem; padding: .65rem .75rem; border-radius: 10px; cursor: pointer; border: 2px solid transparent; transition: all .15s; }
.picker-modal-item:hover { background: #f5f3ff; }
.picker-modal-item.selected { background: #ede9fe; border-color: #8b5cf6; }
.picker-modal-icon { font-size: 1.3rem; flex-shrink: 0; }
.picker-modal-info { flex: 1; min-width: 0; }
.picker-modal-name { display: block; font-size: .9rem; font-weight: 500; color: #1f2937; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.picker-modal-meta { display: block; font-size: .78rem; color: #9ca3af; }
.picker-modal-check { color: #7c3aed; font-weight: 700; font-size: 1rem; flex-shrink: 0; }
.picker-modal-footer { display: flex; align-items: center; gap: .75rem; padding: 1rem 1.5rem; border-top: 1px solid #f0f0f0; background: #fafafa; }
.picker-modal-count { font-size: .85rem; color: #6b7280; flex: 1; }
.picker-modal-clear-btn { padding: .45rem .9rem; border-radius: 8px; border: 1.5px solid #e5e7eb; background: white; color: #374151; font-size: .85rem; cursor: pointer; }
.picker-modal-clear-btn:hover { background: #f3f4f6; }
.picker-modal-confirm-btn { padding: .45rem 1.1rem; border-radius: 8px; border: none; background: #7c3aed; color: white; font-size: .85rem; font-weight: 600; cursor: pointer; }
.picker-modal-confirm-btn:hover { background: #6d28d9; }
.picker-modal-loading { display: flex; align-items: center; gap: .75rem; justify-content: center; padding: 2rem 0; color: #9ca3af; font-size: .9rem; }
</style>


================================================================================