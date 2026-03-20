<template>
  <div class="user-layout">
    <!-- ── Sidebar ─────────────────────────────────────────────────────── -->
    <aside class="sidebar">
      <div class="sidebar-brand">
        <div class="brand-icon">
          <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
              d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
          </svg>
        </div>
        <span class="brand-name">GED Elise</span>
      </div>

      <nav class="sidebar-nav">
        <button v-for="tab in tabs" :key="tab.id"
          @click="activeTab = tab.id"
          class="nav-item" :class="{ active: activeTab === tab.id }">
          <span class="nav-icon" v-html="tab.icon"></span>
          <span>{{ tab.label }}</span>
        </button>
      </nav>

      <div class="sidebar-footer">
        <div class="user-badge">
          <div class="user-avatar">{{ userInitials }}</div>
          <div class="user-info">
            <p class="user-name">{{ user?.fullName || user?.username }}</p>
            <p class="user-role-tag" :class="roleTagClass">{{ roleLabel(user?.role) }}</p>
          </div>
        </div>
        <button @click="logout" class="logout-btn" title="Se déconnecter">
          <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
              d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1"/>
          </svg>
        </button>
      </div>
    </aside>

    <!-- ── Main ───────────────────────────────────────────────────────── -->
    <main class="user-main">

      <!-- ══════════════════════════════════════════════════════════════
           SEARCH TAB — full smart search merged from SearchView
      ══════════════════════════════════════════════════════════════ -->
      <section v-if="activeTab === 'search'" class="search-section">

        <div class="search-card">
          <div class="search-bar-wrapper">
            <div class="search-input-wrapper" style="position:relative">
              <svg class="search-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                  d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>
              </svg>
              <input v-model="searchQuery"
                @keyup.enter="handleSearch"
                @input="onSearchInput"
                @keydown.down.prevent="selectSuggestion(1)"
                @keydown.up.prevent="selectSuggestion(-1)"
                @keydown.escape="showAutocomplete = false"
                @blur="onSearchBlur"
                type="text"
                placeholder="Recherche en langage naturel… ex : « factures du mois dernier »"
                class="search-input"/>
              <!-- Autocomplete dropdown -->
              <div v-if="showAutocomplete && autocompleteSuggestions.length" class="autocomplete-dropdown">
                <div
                  v-for="(sug, i) in autocompleteSuggestions" :key="i"
                  class="autocomplete-item"
                  :class="{ 'ac-active': i === selectedAcIndex }"
                  @mousedown.prevent="applyAutocomplete(sug)">
                  <svg fill="none" stroke="currentColor" viewBox="0 0 24 24" style="width:13px;height:13px;color:#9ca3af;flex-shrink:0">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>
                  </svg>
                  {{ sug }}
                </div>
              </div>
            </div>
            <button @click="openDocPicker" class="elise-attach-btn" :class="{ 'has-attachments': attachedDocIds.length > 0 }">
              📎 {{ attachedDocIds.length ? attachedDocIds.length + ' joint(s)' : 'Joindre' }}
            </button>

            <!-- Doc picker modal -->
            <teleport to="body">
              <div v-if="showDocPicker" class="picker-overlay" @click.self="showDocPicker = false">
                <div class="picker-modal">
                  <div class="picker-modal-header">
                    <h3 class="picker-modal-title">📎 Joindre des documents à Elise</h3>
                    <button @click="showDocPicker = false" class="picker-modal-close">✕</button>
                  </div>

                  <div class="picker-modal-search">
                    <svg fill="none" stroke="currentColor" viewBox="0 0 24 24" style="width:16px;height:16px;color:#9ca3af;flex-shrink:0">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>
                    </svg>
                    <input v-model="pickerSearch" type="text" placeholder="Rechercher un document…" class="picker-modal-search-input" />
                    <button v-if="pickerSearch" @click="pickerSearch = ''" class="picker-modal-search-clear">✕</button>
                  </div>

                  <div class="picker-modal-body">
                    <div v-if="pickerLoading" class="picker-modal-loading">
                      <svg class="spinner" style="width:20px;height:20px" fill="none" viewBox="0 0 24 24">
                        <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
                        <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
                      </svg>
                      Chargement…
                    </div>
                    <div v-else-if="!filteredPickerDocs.length" class="picker-modal-empty">
                      Aucun document trouvé
                    </div>
                    <label v-else v-for="doc in filteredPickerDocs" :key="doc.id" class="picker-modal-item" :class="{ selected: attachedDocIds.includes(doc.id) }">
                      <input type="checkbox" :value="doc.id" v-model="attachedDocIds" style="display:none" />
                      <span class="picker-modal-icon">{{ getFileIcon(doc.contentType) }}</span>
                      <div class="picker-modal-info">
                        <span class="picker-modal-name">{{ doc.title }}</span>
                        <span class="picker-modal-meta">{{ doc.category || '—' }}</span>
                      </div>
                      <span v-if="attachedDocIds.includes(doc.id)" class="picker-modal-check">✓</span>
                    </label>
                  </div>

                  <div class="picker-modal-footer">
                    <span class="picker-modal-count">{{ attachedDocIds.length }} sélectionné(s)</span>
                    <button @click="attachedDocIds = []" class="picker-modal-clear-btn">Tout désélectionner</button>
                    <button @click="confirmAttachments" class="picker-modal-confirm-btn">Confirmer</button>
                  </div>
                </div>
              </div>
            </teleport>

            <button @click="handleSearch" :disabled="searchLoading || ragLoading || !searchQuery.trim()" class="search-btn">
              <span v-if="!searchLoading">Rechercher</span>
              <span v-else class="loading-text">
                <svg class="spinner" fill="none" viewBox="0 0 24 24">
                  <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
                  <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
                </svg>
                Recherche…
              </span>
            </button>
            <button v-if="canUpload" @click="showUploadModal = true" class="upload-btn">
              <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                  d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/>
              </svg>
              Importer
            </button>
          </div>

          <!-- NLP query interpretation banner -->
          <div v-if="nlpInterpretation" class="nlp-banner">
            <span class="nlp-icon">🧠</span>
            <span class="nlp-text">Compris comme : <strong>{{ nlpInterpretation }}</strong></span>
            <button class="nlp-dismiss" @click="nlpInterpretation = null">✕</button>
          </div>
          <!-- Show when query is not understood -->
          <div v-if="searchError" class="search-error-banner">
            {{ searchError }}
          </div>

          <div class="quick-searches">
            <span class="quick-label">Essayez :</span>
            <button v-for="s in quickSearches" :key="s" @click="searchQuery = s; handleSearch()" class="quick-btn">{{ s }}</button>
          </div>

          <button @click="showFilters = !showFilters" class="filters-toggle">
            <svg class="toggle-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4"/>
            </svg>
            {{ showFilters ? 'Masquer' : 'Afficher' }} les filtres avancés
          </button>

          <div v-if="showFilters" class="filters-panel">
            <div class="filters-grid">
              <div class="filter-group">
                <label class="filter-label">Catégorie</label>
                <select v-model="filters.category" class="filter-select">
                  <option value="">Toutes</option>
                  <option value="Invoice">📄 Facture</option>
                  <option value="Contract">📜 Contrat</option>
                  <option value="Report">📊 Rapport</option>
                  <option value="Letter">✉️ Courrier</option>
                  <option value="Memo">📝 Mémo</option>
                  <option value="Presentation">📽️ Présentation</option>
                  <option value="Spreadsheet">📈 Tableur</option>
                  <option value="Image">🖼️ Image</option>
                  <option value="Other">📎 Autre</option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">Type de fichier</label>
                <select v-model="filters.contentType" class="filter-select">
                  <option value="">Tous</option>
                  <option value="application/pdf">📄 PDF</option>
                  <option value="application/vnd.openxmlformats-officedocument.wordprocessingml.document">📝 Word</option>
                  <option value="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet">📊 Excel</option>
                  <option value="text/plain">📃 Texte brut</option>
                  <option value="image/jpeg">🖼️ JPEG</option>
                  <option value="image/png">🖼️ PNG</option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">Date début</label>
                <input v-model="filters.dateFrom" type="date" class="filter-input"/>
              </div>
              <div class="filter-group">
                <label class="filter-label">Date fin</label>
                <input v-model="filters.dateTo" type="date" class="filter-input"/>
              </div>
              <div class="filter-group">
                <label class="filter-label">Statut OCR</label>
                <select v-model="filters.ocrStatus" class="filter-select">
                  <option value="">Tous</option>
                  <option value="4">✅ OCR terminé</option>
                  <option value="0">⏳ En attente</option>
                  <option value="1">🔄 En traitement</option>
                  <option value="5">❌ Échec OCR</option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">Service</label>
                <select v-model="filters.service" class="filter-select">
                  <option value="">Tous les services</option>
                  <option value="Finance">💼 Finance</option>
                  <option value="RH">👥 Ressources Humaines</option>
                  <option value="Juridique">⚖️ Juridique</option>
                  <option value="Commercial">📈 Commercial</option>
                  <option value="Informatique">💻 Informatique</option>
                  <option value="Direction">🏢 Direction</option>
                  <option value="Autre">📁 Autre</option>
                </select>
              </div>
            </div>
            <div v-if="hasActiveFilters" class="filters-reset-row">
              <button @click="resetFilters" class="filters-reset-btn">
                ✕ Réinitialiser tous les filtres
              </button>
            </div>
          </div>
        </div>
        <!-- RAG answer panel -->
        <div v-if="ragMode && (ragAnswer || ragLoading || ragHistory.length > 0)" class="rag-answer-panel">
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
              <button v-if="ragHistory.length > 0" @click="ragHistory = []; ragAnswer = ''; ragSources = []" class="rag-clear-btn" title="Nouvelle conversation">🗑 Nouvelle</button>
              <span v-if="ragSources.length" class="rag-source-count">{{ ragSources.length }} source(s)</span>
              <button @click="ragAnswer = ''; ragSources = []; ragHistory = []" class="rag-close">✕</button>
            </div>
          </div>

          <!-- ── Conversation history ───────────────────────────────────── -->
          <div class="rag-conversation">
            <div v-for="(msg, i) in ragHistory" :key="i" :class="['rag-msg', msg.role === 'user' ? 'rag-msg-user' : 'rag-msg-elise']">
              <div class="rag-msg-avatar">{{ msg.role === 'user' ? userInitials : '✨' }}</div>
              <div class="rag-msg-bubble">
                <p class="rag-msg-text">{{ msg.content }}</p>
              </div>
            </div>
          </div>

          <!-- ── Loading indicator ─────────────────────────────────────── -->
          <div v-if="ragLoading" class="rag-thinking">
            <svg class="spinner" style="width:16px;height:16px" fill="none" viewBox="0 0 24 24">
              <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
              <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
            </svg>
            Elise analyse vos documents…
          </div>

          <!-- ── Sources ──────────────────────────────────────────────── -->
          <div v-if="ragSources.length" class="rag-sources-grid">
            <div v-for="(src, i) in ragSources" :key="i" class="rag-source-chip">
              <span class="src-num">{{ i + 1 }}</span>
              <div class="src-body">
                <p class="src-title">{{ src.title }}</p>
                <p class="src-meta">
                  <span v-if="src.category">{{ src.category }} · </span>
                  {{ Math.round(src.relevanceScore * 100) }}% pertinent
                </p>
                <p v-if="src.excerpt" class="src-excerpt">{{ src.excerpt }}</p>
              </div>
              <button @click="viewDocument({ id: src.documentId, title: src.title, fileName: src.title, contentType: 'application/pdf', score: src.relevanceScore })" class="src-view-btn">Voir</button>
            </div>
          </div>
        </div>
        <!-- Results summary -->
        <div v-if="searchResults && searchResults.documents?.length > 0" class="results-summary">
          <div class="summary-card">
            <span class="summary-count">{{ searchResults.totalResults }}</span>
            <span class="summary-text"> résultat(s)</span>
            <span class="summary-divider">·</span>
            <span class="summary-time">{{ searchResults.searchTimeMs }}ms</span>
          </div>
          <div class="summary-page">Page {{ searchResults.page }} / {{ searchResults.totalPages }}</div>
        </div>

        <!-- Documents grid -->
        <div v-if="searchResults && searchResults.documents?.length > 0" class="documents-grid">
          <article v-for="doc in searchResults.documents" :key="doc.id" class="document-card">
            <div class="card-content">
              <div class="doc-info">
                <div class="doc-header">
                  <div class="file-icon-box">
                    <span class="icon-emoji">{{ getFileIcon(doc.contentType) }}</span>
                  </div>
                  <div class="doc-details">
                    <h3 class="doc-title">{{ doc.title }}</h3>
                    <p v-if="doc.description" class="doc-description">{{ doc.description }}</p>
                    <div v-if="doc.highlights && doc.highlights.length" class="highlights">
                      <div v-for="(h,i) in doc.highlights.slice(0,2)" :key="i" class="highlight-item" v-html="h"></div>
                    </div>
                    <div class="metadata-row">
                      <span class="meta-item">{{ doc.fileName }}</span>
                      <span v-if="doc.documentDate" class="meta-item meta-highlight">📅 {{ formatDate(doc.documentDate) }}</span>
                      <span class="meta-item">{{ formatFileSize(doc.fileSize) }}</span>
                      <span v-if="doc.category" class="category-badge">{{ doc.category }}</span>
                      <!-- OCR badge -->
                      <span v-if="doc.isOcrProcessed" class="ocr-badge ocr-badge-done" title="Contenu indexé via OCR">🔬 OCR</span>
                      <span v-else-if="doc.ocrStatus !== undefined && doc.ocrStatus < 4" class="ocr-badge ocr-badge-pending" title="Traitement OCR en cours">⏳ OCR</span>
                      <!-- OCR quality indicator -->
                      <span v-if="doc.ocrQualityScore !== undefined && doc.ocrQualityScore !== null"
                        class="ocr-quality-badge"
                        :class="doc.ocrQualityScore >= 0.8 ? 'oq-good' : doc.ocrQualityScore >= 0.5 ? 'oq-medium' : 'oq-low'"
                        :title="`Qualité OCR : ${Math.round(doc.ocrQualityScore*100)}%`">
                        Qualité {{ Math.round(doc.ocrQualityScore*100) }}%
                      </span>
                      <!-- Service badge -->
                      <span v-if="doc.service" class="service-badge">{{ doc.service }}</span>
                    </div>
                    <div v-if="doc.tags && doc.tags.length" class="tags-row">
                      <span v-for="tag in doc.tags.slice(0,5)" :key="tag" class="tag">#{{ tag }}</span>
                      <span v-if="doc.tags.length > 5" class="tag-more">+{{ doc.tags.length - 5 }}</span>
                    </div>
                    <!-- Auto-generated summary -->
                    <div v-if="doc.summary || doc.description" class="doc-summary-row">
                      <button @click.stop="toggleSummary(doc.id)" class="summary-toggle-btn">
                        {{ expandedSummaries.has(doc.id) ? '▲ Masquer le résumé' : '▼ Résumé automatique' }}
                      </button>
                      <div v-if="expandedSummaries.has(doc.id)" class="doc-summary-text">
                        {{ doc.summary || doc.description }}
                      </div>
                    </div>
                  </div>
                </div>
              </div>
              <div class="doc-actions">
                <div class="score-wrapper">
                  <div class="score-circle">
                    <svg class="circle-svg" viewBox="0 0 100 100">
                      <circle cx="50" cy="50" r="40" class="circle-bg"/>
                      <circle cx="50" cy="50" r="40" class="circle-progress"
                        :style="`stroke-dashoffset: ${251 - 251 * doc.score}`"/>
                    </svg>
                    <div class="score-text">
                      <span class="score-value">{{ Math.round(doc.score * 100) }}%</span>
                    </div>
                  </div>
                  <p class="score-label">Pertinence</p>
                </div>
                <button @click="viewDocument(doc)" class="view-btn">
                  <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"/>
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"/>
                  </svg>
                  Voir
                </button>
                <button v-if="canDelete" @click="deleteDoc(doc)" class="delete-btn" title="Supprimer">🗑</button>
              </div>
            </div>
          </article>
        </div>

        <!-- Pagination -->
        <nav v-if="searchResults && searchResults.totalPages > 1" class="pagination">
          <button v-for="page in paginationPages" :key="page"
            @click="goToPage(page)" :class="['page-btn', { active: page === searchResults.page }]">
            {{ page }}
          </button>
        </nav>

        <!-- Empty state -->
        <div v-else-if="!searchLoading && searched && (!searchResults || searchResults.documents.length === 0)" class="empty-state">
          <div class="state-icon empty-icon">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>
            </svg>
          </div>
          <h3>Aucun document trouvé</h3>
          <p>Essayez d'ajuster votre requête ou vos filtres</p>
          <button @click="searchQuery = ''; searchResults = null; searched = false" class="clear-btn">Effacer</button>
        </div>

        <!-- Initial state -->
        <div v-else-if="!searched" class="initial-state">
          <div class="state-icon initial-icon">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>
            </svg>
          </div>
          <h3>Recherchez vos documents</h3>
          <p>Utilisez le langage naturel pour trouver ce dont vous avez besoin</p>
        </div>
      </section>

    </main>

    <!-- ══════════════════════════════════════════════════════════════════
         DOCUMENT VIEWER MODAL (full viewer — PDF / Image / Text / Office / Audio / Video)
    ══════════════════════════════════════════════════════════════════ -->
    <div v-if="showDocumentViewer" class="modal-overlay" @click.self="closeDocumentViewer">
      <div class="viewer-modal">

        <!-- Header -->
        <div class="viewer-header">
          <div class="viewer-header-left">
            <div class="viewer-file-badge">{{ getFileExtension(currentDocument?.fileName) }}</div>
            <div class="viewer-title-block">
              <h2 class="viewer-title">{{ currentDocument?.title }}</h2>
              <p class="viewer-filename">
                <span>{{ getFileIcon(currentDocument?.contentType) }}</span>
                {{ currentDocument?.fileName }}
                <span class="vf-sep">·</span> {{ formatFileSize(currentDocument?.fileSize) }}
                <span v-if="currentDocument?.category" class="vf-sep">·</span>
                <span v-if="currentDocument?.category" class="vf-cat">{{ currentDocument.category }}</span>
              </p>
            </div>
          </div>
          <div class="viewer-header-actions">
            <div class="tab-switcher">
              <button :class="['tab-btn', { active: viewerTab === 'preview' }]" @click="viewerTab = 'preview'">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"/>
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"/>
                </svg>
                Aperçu
              </button>
              <button :class="['tab-btn', { active: viewerTab === 'details' }]" @click="viewerTab = 'details'">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>
                </svg>
                Détails
              </button>
            </div>
            <button @click="closeDocumentViewer" class="hdr-close">
              <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
              </svg>
            </button>
          </div>
        </div>

        <!-- Body: two-column split -->
        <div class="viewer-body">

          <!-- LEFT: File Preview -->
          <div class="viewer-preview-pane" :class="{ 'tab-hidden': viewerTab !== 'preview' }">
            <div v-if="documentLoading" class="preview-loading">
              <div class="pulse-ring"></div>
              <svg class="spinner xl" fill="none" viewBox="0 0 24 24">
                <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
                <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
              </svg>
              <p>Chargement de l'aperçu…</p>
            </div>

            <div v-else-if="isPDF(currentDocument?.contentType)" class="pdf-viewer">
              <iframe v-if="documentUrl" :src="documentUrl + '#toolbar=1&navpanes=0&zoom=page-fit'"
                class="pdf-frame" title="PDF Viewer"/>
            </div>

            <div v-else-if="isImage(currentDocument?.contentType)" class="image-viewer">
              <a :href="documentUrl" target="_blank">
                <img :src="documentUrl" :alt="currentDocument?.title" class="document-image"/>
              </a>
              <p class="image-hint">Cliquer pour ouvrir en pleine taille</p>
            </div>

            <div v-else-if="isText(currentDocument?.contentType)" class="text-viewer">
              <div class="text-toolbar">
                <span>{{ documentContent?.split('\n').length }} lignes</span>
                <span>{{ documentContent?.length?.toLocaleString() }} caractères</span>
              </div>
              <pre class="text-content">{{ documentContent }}</pre>
            </div>

            <div v-else-if="isOffice(currentDocument?.contentType)" class="office-viewer">
              <div class="office-tabs">
                <button :class="['otab', { active: officeMode === 'text' }]" @click="officeMode = 'text'">📄 Texte extrait</button>
                <button :class="['otab', { active: officeMode === 'embed' }]" @click="officeMode = 'embed'">🌐 Office Online</button>
              </div>
              <div v-if="officeMode === 'text'" class="office-text-panel">
                <div v-if="documentContent" class="office-text-wrap">
                  <div class="office-text-stats">
                    <span>{{ documentContent.split(/\s+/).filter(Boolean).length.toLocaleString() }} mots</span>
                    <span>{{ documentContent.split('\n').length }} paragraphes</span>
                  </div>
                  <pre class="office-text-content">{{ documentContent }}</pre>
                </div>
                <div v-else class="office-no-text">
                  <div class="ont-icon">{{ getFileIcon(currentDocument?.contentType) }}</div>
                  <p class="ont-title">Aucun texte extrait disponible</p>
                  <p class="ont-sub">L'extraction est peut-être en cours, ou ce fichier ne contient pas de texte sélectionnable.</p>
                </div>
              </div>
              <div v-if="officeMode === 'embed'" class="office-embed-panel">
                <div class="office-embed-notice">
                  Office Online nécessite une URL publique.
                  <a href="#" class="office-embed-link">Ouvrir dans Office Online ↗</a>
                </div>
              </div>
            </div>

            <div v-else-if="isAudio(currentDocument?.contentType)" class="audio-viewer">
              <div class="audio-art">
                <div class="audio-wave">
                  <span v-for="i in 20" :key="i" class="wave-bar" :style="`animation-delay:${i*0.07}s`"></span>
                </div>
                <div style="font-size:3rem">🎵</div>
                <p style="color:#94a3b8;font-size:.85rem;margin-top:.5rem">{{ currentDocument?.fileName }}</p>
              </div>
              <audio :src="documentUrl" controls class="audio-player"/>
            </div>

            <div v-else-if="isVideo(currentDocument?.contentType)" class="video-viewer">
              <video :src="documentUrl" controls class="video-player"/>
            </div>

            <div v-else class="unsupported-viewer">
              <span style="font-size:5rem">{{ getFileIcon(currentDocument?.contentType) }}</span>
              <h3>Aperçu non disponible</h3>
              <p>Ce type de fichier ne peut pas être affiché dans le navigateur.</p>
              <code>{{ currentDocument?.contentType }}</code>
            </div>
          </div>

          <!-- RIGHT: Details pane -->
          <div class="viewer-details-pane" :class="{ 'tab-hidden': viewerTab !== 'details' }">

            <div v-if="ocrStatus" class="ocr-status-bar"
              :class="ocrStatus.status===4?'ocr-done':ocrStatus.status===5?'ocr-fail':ocrStatus.status===2?'ocr-partial':'ocr-pending'">
              <span class="ocr-dot"></span>
              <span v-if="ocrStatus.status===4">OCR terminé · {{ (ocrStatus.rawTextLength||0).toLocaleString() }} caractères</span>
              <span v-else-if="ocrStatus.status===5">OCR échoué : {{ ocrStatus.errorMessage }}</span>
              <span v-else-if="ocrStatus.status===2">Texte prêt · Amélioration IA en cours…</span>
              <span v-else>{{ ocrStatus.stageLabel ?? 'Traitement OCR…' }}</span>
            </div>

            <div class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>
                </svg>
                Informations
              </h3>
              <dl class="detail-list">
                <div class="dl-row"><dt>Fichier</dt><dd class="dd-mono">{{ currentDocument?.fileName }}</dd></div>
                <div class="dl-row"><dt>Type</dt><dd>
                  <span class="mime-badge">{{ getFileExtension(currentDocument?.fileName).toUpperCase() }}</span>
                  <span class="mime-text">{{ currentDocument?.contentType }}</span>
                </dd></div>
                <div class="dl-row"><dt>Taille</dt><dd>{{ formatFileSize(currentDocument?.fileSize) }}</dd></div>
                <div class="dl-row" v-if="currentDocument?.category"><dt>Catégorie</dt><dd><span class="cat-pill">{{ currentDocument.category }}</span></dd></div>
                <div class="dl-row" v-if="currentDocument?.documentDate"><dt>Date doc.</dt><dd class="dd-accent">📅 {{ formatDate(currentDocument.documentDate) }}</dd></div>
                <div class="dl-row"><dt>Importé</dt><dd>{{ formatDateLong(currentDocument?.createdAt) }}</dd></div>
                <div class="dl-row" v-if="currentDocument?.score !== undefined"><dt>Pertinence</dt><dd>
                  <div class="relevance-bar-wrap">
                    <div class="relevance-bar"><div class="relevance-fill" :style="`width:${Math.round((currentDocument.score||0)*100)}%`"></div></div>
                    <span class="relevance-pct">{{ Math.round((currentDocument.score||0)*100) }}%</span>
                  </div>
                </dd></div>
              </dl>
            </div>

            <div v-if="currentDocument?.tags?.length" class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"/>
                </svg>
                Étiquettes
              </h3>
              <div class="tags-cloud">
                <button v-for="tag in currentDocument.tags" :key="tag" @click="searchByTag(tag)" class="tag-cloud-item">#{{ tag }}</button>
              </div>
            </div>

            <div v-if="currentDocument?.description || currentDocument?.highlights?.length" class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
                </svg>
                Extrait
              </h3>
              <p v-if="currentDocument.description" class="detail-desc">{{ currentDocument.description }}</p>
              <div v-if="currentDocument.highlights?.length" class="detail-highlights">
                <div v-for="(h,i) in currentDocument.highlights" :key="i" class="detail-highlight-item" v-html="h"></div>
              </div>
            </div>

            <div class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10"/>
                </svg>
                Documents similaires
              </h3>
              <div v-if="suggestionsLoading" class="suggestions-loading">
                <svg class="spinner sm" fill="none" viewBox="0 0 24 24">
                  <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
                  <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
                </svg>
                Recherche…
              </div>
              <div v-else-if="!suggestions.length" class="suggestions-empty">Aucun document similaire trouvé</div>
              <div v-else class="suggestions-list">
                <div v-for="sug in suggestions" :key="sug.documentId" class="suggestion-card" @click="openSuggestion(sug)">
                  <div class="sug-icon">📄</div>
                  <div class="sug-info">
                    <p class="sug-title">{{ sug.title }}</p>
                    <p class="sug-reason">{{ sug.reason }}</p>
                  </div>
                  <div class="sug-score-ring" :style="`--pct:${Math.round(sug.similarityScore*100)}`">
                    <span>{{ Math.round(sug.similarityScore*100) }}%</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ══════════════════════════════════════════════════════════════════
          UPLOAD MODAL (Batch Import)
    ══════════════════════════════════════════════════════════════════ -->
    <div v-if="showUploadModal" class="modal-overlay" @click.self="closeUploadModal">
      <div class="modal-content modal-large">
        <div class="modal-header">
          <h2>Importer des documents (Batch)</h2>
          <button @click="closeUploadModal" class="modal-close">✕</button>
        </div>
        <div class="modal-body-upload">
          <div class="upload-area batch-upload-area" @click="$refs.fileInput.click()" @dragover.prevent @drop.prevent="onDropMultiple">
            <input ref="fileInput" type="file" @change="handleFileSelectMultiple" class="file-input" multiple
              accept=".pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png,.tiff,.txt"/>
            <div v-if="selectedFiles.length === 0" class="upload-prompt">
              <svg class="upload-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                  d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/>
              </svg>
              <p>Cliquez ou glissez plusieurs fichiers ici</p>
              <p class="upload-hint">PDF, Word, Excel, Images (max 100 Mo par fichier)</p>
            </div>
            <div v-else class="files-preview-list">
              <div v-for="(file, index) in selectedFiles" :key="index" class="file-preview-item">
                <span class="file-icon">{{ getFileIcon(file.type) }}</span>
                <div class="file-info">
                  <p class="file-name">{{ file.name }}</p>
                  <p class="file-size">{{ formatFileSize(file.size) }}</p>
                </div>
                <button @click.stop="removeFile(index)" class="file-remove-btn">✕</button>
              </div>
            </div>
          </div>
          
          <div v-if="selectedFiles.length > 0" class="batch-category-section">
            <p class="batch-info">Tous les fichiers utiliseront les mêmes paramètres ci-dessous :</p>
            <div class="form-group">
              <label class="form-label">Catégorie <span style="color:#ef4444">*</span></label>
              <select v-model="uploadData.category" class="filter-select" :class="{ 'input-error': !uploadData.category }">
                <option value="">— Sélectionner —</option>
                <option value="Invoice">📄 Facture</option>
                <option value="Contract">📜 Contrat</option>
                <option value="Report">📊 Rapport</option>
                <option value="Letter">✉️ Courrier</option>
                <option value="Memo">📝 Mémo</option>
                <option value="Presentation">📽️ Présentation</option>
                <option value="Spreadsheet">📈 Tableur</option>
                <option value="Image">🖼️ Image</option>
                <option value="Other">📎 Autre</option>
              </select>
            </div>
          </div>

          <div class="modal-actions">
            <button @click="uploadDocuments" :disabled="selectedFiles.length === 0 || uploading || !uploadData.category" class="upload-submit">
              <span v-if="!uploading">Importer {{ selectedFiles.length }} fichier(s)</span>
              <span v-else class="loading-text">
                <svg class="spinner" fill="none" viewBox="0 0 24 24">
                  <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
                  <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
                </svg>
                Envoi en cours... {{ uploadProgress }}/{{ selectedFiles.length }}
              </span>
            </button>
            <button @click="closeUploadModal" class="cancel-btn">Annuler</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, nextTick, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { format } from 'date-fns'
import { logger } from '../logger.js'

const router = useRouter()

const onSearchBlur = () => {
  window.setTimeout(() => { showAutocomplete.value = false }, 180)
}

// ── Auth ───────────────────────────────────────────────────────────────────────
const user = computed(() => JSON.parse(localStorage.getItem('ged_user') || '{}'))
const userInitials = computed(() => {
  const n = user.value?.fullName || user.value?.username || '?'
  return n.split(' ').map(c => c[0]).join('').toUpperCase().slice(0, 2)
})
const authHeaders = () => {
  const t = localStorage.getItem('ged_token')
  return t ? { Authorization: `Bearer ${t}` } : {}
}
const logout = () => { localStorage.clear(); router.push('/login') }

onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
})

// ── Role permissions ───────────────────────────────────────────────────────────
const canUpload = computed(() => ['Manager', 'User'].includes(user.value?.role))
const canDelete = computed(() => user.value?.role === 'Manager')

// ── Sidebar tabs ───────────────────────────────────────────────────────────────
const activeTab = ref('search')
const tabs = [
  { id: 'search', label: 'Recherche',    icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/></svg>' },
]

// ── Role display ───────────────────────────────────────────────────────────────
const roleLabel = (r) => ({ Admin:'Administrateur', Manager:'Responsable', User:'Utilisateur', ReadOnly:'Lecture seule' }[r] || r || '')
const roleTagClass = computed(() => ({ Admin:'tag-admin', Manager:'tag-manager', User:'tag-user', ReadOnly:'tag-readonly' }[user.value?.role] || ''))

// ── Search state ───────────────────────────────────────────────────────────────
const searchQuery   = ref('')
const showFilters   = ref(false)
const searchLoading = ref(false)
const searched      = ref(false)
const searchResults = ref(null)
const filters       = reactive({ category:'', contentType:'', dateFrom:'', dateTo:'', ocrStatus:'', service:'' })
const quickSearches = ['tous les documents', 'factures', 'contrats 2024', 'PDF récents']
const ragMode       = ref(false)   // toggle: false = normal search, true = RAG
const ragModeForced = ref(false)   // true if user explicitly forced RAG mode
const ragAnswer     = ref('')
const ragSources    = ref([])
const ragLoading    = ref(false)
const ragHistory    = ref([])       // conversation memory: { role, content, sources }
const ragAbortCtrl  = ref(null)    // abort controller for streaming
const showDocPicker    = ref(false)
const attachedDocIds   = ref([])
const pickerDocs       = ref([])
const pickerSearch     = ref('')
const pickerLoading    = ref(false)

const filteredPickerDocs = computed(() =>
  pickerSearch.value.trim()
    ? pickerDocs.value.filter(d => d.title.toLowerCase().includes(pickerSearch.value.toLowerCase()))
    : pickerDocs.value
)

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
  
  // Specific date formats
  if (/\b\d{1,2}\/\d{1,2}\/\d{2,4}\b/.test(q) || /\b\d{4}-\d{2}-\d{2}\b/.test(q)) return true
  
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
  
  // Explicit question patterns
  if (q.includes('?')) {
    ragTriggerReason.value = 'Question détectée'
    return true
  }
  
  // Check for Arabic script presence
  const isArabic = /[\u0600-\u06FF]/.test(q)
  
  // ─── ARABIC ─────────────────────────────────────────────────────────────────────
  if (isArabic) {
    // Arabic interrogatives - clear question words
    if (/\b(ما|من|أين|كيف|لماذا|متى|هل|كم|أي|أيش|ليش|وش|وين|شو|ليش|أيش|ليهما|لمن|لمَن|في أي|على أي|أي من|ما الذي|ما هي|من الذي|من هم|كيف يمكن|ما شأن|ما أمر|ما السبب|هل يمكن|هل يوجد)\b/.test(q)) {
      ragTriggerReason.value = 'Question arabe détectée'
      return true
    }
    // Arabic AI requests (summarize, explain, etc.)
    if (/\b(لخص|ملخص|فاهم|اشرح|Explain|Summarize|Summarise|احصل على ملخص|دعني اعرف|ما المعلومات)\b/.test(q)) {
      ragTriggerReason.value = 'Demande IA arabe détectée'
      return true
    }
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
  if (ragMode.value && !ragModeForced.value) {
    ragHistory.value = []
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

const fetchPickerDocs = async () => {
  pickerLoading.value = true
  try {
    // Empty query returns all indexed documents via MatchAll query
    const res = await fetch('/api/search/query', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({ query: '', searchType: 0, page: 1, pageSize: 500 })
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
  if (!pickerDocs.value.length) fetchPickerDocs()
}

// ── NLP interpretation ─────────────────────────────────────────────────────────
const nlpInterpretation = ref(null)
const searchError = ref(null)

// ── Autocomplete ───────────────────────────────────────────────────────────────
const showAutocomplete       = ref(false)
const autocompleteSuggestions = ref([])
const selectedAcIndex        = ref(-1)
let _acTimer = null

const onSearchInput = () => {
  selectedAcIndex.value = -1
  clearTimeout(_acTimer)
  if (searchQuery.value.trim().length < 2) { showAutocomplete.value = false; autocompleteSuggestions.value = []; return }
  _acTimer = setTimeout(async () => {
    try {
      const r = await fetch(`/api/search/suggestions?q=${encodeURIComponent(searchQuery.value)}`, { headers: authHeaders() })
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
  if (selectedAcIndex.value >= 0) searchQuery.value = autocompleteSuggestions.value[selectedAcIndex.value]
}

const applyAutocomplete = (sug) => {
  searchQuery.value = sug; showAutocomplete.value = false; handleSearch()
}

// ── Expanded summaries set ─────────────────────────────────────────────────────
const expandedSummaries = ref(new Set())
const toggleSummary = (id) => {
  const s = new Set(expandedSummaries.value)
  s.has(id) ? s.delete(id) : s.add(id)
  expandedSummaries.value = s
}

// ── Filter helpers ─────────────────────────────────────────────────────────────
const hasActiveFilters = computed(() =>
  !!(filters.category || filters.contentType || filters.dateFrom || filters.dateTo || filters.ocrStatus || filters.service)
)
const resetFilters = () => {
  Object.assign(filters, { category:'', contentType:'', dateFrom:'', dateTo:'', ocrStatus:'', service:'' })
  if (searched.value) handleSearch()
}

// ── Viewer state ───────────────────────────────────────────────────────────────
const showDocumentViewer = ref(false)
const currentDocument    = ref(null)
const documentUrl        = ref(null)
const documentContent    = ref(null)
const documentLoading    = ref(false)
const viewerTab          = ref('preview')
const officeMode         = ref('text')
const ocrStatus          = ref(null)
const ocrPollInterval    = ref(null)
const suggestions        = ref([])
const suggestionsLoading = ref(false)
const suggestionsCache   = new Map()

// ── Upload state (Batch) ───────────────────────────────────────────────────────
const showUploadModal = ref(false)
const selectedFiles    = ref([])
const uploading       = ref(false)
const uploadProgress  = ref(0)
const uploadData      = reactive({ title:'', category:'' })

// ── Computed ───────────────────────────────────────────────────────────────────
const paginationPages = computed(() => {
  if (!searchResults.value) return []
  const total = searchResults.value.totalPages, cur = searchResults.value.page
  const start = Math.max(1, cur - 4), end = Math.min(total, start + 9)
  return Array.from({ length: end - start + 1 }, (_, i) => start + i)
})

// ── Type guards ────────────────────────────────────────────────────────────────
const isPDF    = (t) => t === 'application/pdf'
const isImage  = (t) => !!t?.startsWith('image/')
const isText   = (t) => t === 'text/plain'
const isAudio  = (t) => !!t?.startsWith('audio/')
const isVideo  = (t) => !!t?.startsWith('video/')
const isOffice = (t) => ['application/msword','application/vnd.openxmlformats-officedocument.wordprocessingml.document','application/vnd.ms-excel','application/vnd.openxmlformats-officedocument.spreadsheetml.sheet','application/vnd.ms-powerpoint','application/vnd.openxmlformats-officedocument.presentationml.presentation'].includes(t)

// ── Helpers ────────────────────────────────────────────────────────────────────
const getFileIcon = (ct) => {
  if (!ct) return '📎'
  if (ct.includes('pdf')) return '📄'
  if (ct.includes('word')||ct.includes('document')) return '📝'
  if (ct.includes('sheet')||ct.includes('excel'))   return '📊'
  if (ct.includes('presentation')||ct.includes('powerpoint')) return '📽️'
  if (ct.includes('image')) return '🖼️'
  if (ct.includes('text'))  return '📃'
  if (ct.includes('audio')) return '🎵'
  if (ct.includes('video')) return '🎬'
  return '📎'
}
const getFileExtension = (n) => n ? (n.split('.').pop() || '').toLowerCase() : ''
const formatDate     = (d) => { try { return format(new Date(d),'dd/MM/yyyy') } catch { return d } }
const formatDateLong = (d) => { try { return format(new Date(d),'dd/MM/yyyy · HH:mm') } catch { return d } }
const formatFileSize = (b) => {
  if (!b) return '—'
  if (b < 1024) return b + ' B'
  if (b < 1048576) return (b/1024).toFixed(1) + ' KB'
  return (b/1048576).toFixed(1) + ' MB'
}

// ── Blob URL management ────────────────────────────────────────────────────────
let _blobUrl = null
const revokeBlobUrl = () => { if (_blobUrl) { URL.revokeObjectURL(_blobUrl); _blobUrl = null } }
const fetchBlobUrl = async (path, mime) => {
  const res  = await fetch(path, { headers: authHeaders() })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  const buf  = await res.arrayBuffer()
  const type = mime || res.headers.get('content-type')?.split(';')[0].trim() || 'application/octet-stream'
  const url  = URL.createObjectURL(new Blob([buf], { type }))
  _blobUrl = url; return url
}

// ── OCR polling ────────────────────────────────────────────────────────────────
const OcrStatus  = { Pending:0, Processing:1, TextExtracted:2, LlmCleaning:3, Completed:4, Failed:5 }
const stopOcrPolling = () => { if (ocrPollInterval.value) { clearInterval(ocrPollInterval.value); ocrPollInterval.value = null } }
const startOcrPolling = (docId) => {
  stopOcrPolling(); let n = 0
  ocrPollInterval.value = setInterval(async () => {
    n++
    try {
      const res = await fetch(`/api/documents/${docId}/ocr-status`, { headers: authHeaders() })
      if (res.status === 401) { stopOcrPolling(); logout(); return }
      if (!res.ok) { if (n >= 50) stopOcrPolling(); return }
      const d = await res.json(); ocrStatus.value = d
      if (d.status === OcrStatus.TextExtracted && d.extractedText && !documentContent.value) {
        documentContent.value = d.extractedText; await nextTick()
      }
      if (d.status === OcrStatus.Completed) {
        stopOcrPolling()
        if (d.extractedText) documentContent.value = d.extractedText
        currentDocument.value = { ...currentDocument.value, tags: d.tags ?? currentDocument.value.tags, description: d.description ?? currentDocument.value.description, documentDate: d.documentDate ?? currentDocument.value.documentDate }
        suggestionsCache.delete(docId); fetchSuggestions(docId)
        return
      }
      if (d.status === OcrStatus.Failed || n >= 50) stopOcrPolling()
    } catch { if (n >= 50) stopOcrPolling() }
  }, 4000)
}

// ── Document viewer ────────────────────────────────────────────────────────────
const viewDocument = async (doc) => {
  revokeBlobUrl(); stopOcrPolling()
  currentDocument.value = doc; showDocumentViewer.value = true
  documentLoading.value = true; documentContent.value = null
  documentUrl.value = null; ocrStatus.value = null
  suggestions.value = []; viewerTab.value = 'preview'; officeMode.value = 'text'

  try {
    const r = await fetch(`/api/documents/${doc.id}`, { headers: authHeaders() })
    if (r.ok) { const fresh = await r.json(); currentDocument.value = { ...fresh, score: doc.score, highlights: doc.highlights } }
  } catch { /* non-fatal */ }

  const dlPath  = `/api/documents/${doc.id}/download`
  const ocrPath = `/api/documents/${doc.id}/ocr-status`
  try {
    if (isPDF(doc.contentType)||isImage(doc.contentType)||isAudio(doc.contentType)||isVideo(doc.contentType)) {
      try { documentUrl.value = await fetchBlobUrl(dlPath, doc.contentType) } catch { documentUrl.value = dlPath }
    } else if (isText(doc.contentType)) {
      const r = await fetch(dlPath, { headers: authHeaders() })
      documentContent.value = r.ok ? await r.text() : '(impossible de charger le fichier)'
    } else if (isOffice(doc.contentType)) {
      try {
        const r = await fetch(ocrPath, { headers: authHeaders() })
        if (r.ok) { const d = await r.json(); ocrStatus.value = d; if (d.extractedText) documentContent.value = d.extractedText }
      } catch { /* non-fatal */ }
    }
    if (isPDF(doc.contentType)||isImage(doc.contentType)) {
      try {
        const r = await fetch(ocrPath, { headers: authHeaders() })
        if (r.ok) {
          const d = await r.json(); ocrStatus.value = d
          if ([OcrStatus.Pending,OcrStatus.Processing,OcrStatus.TextExtracted,OcrStatus.LlmCleaning].includes(d.status)) startOcrPolling(doc.id)
        }
      } catch { /* non-fatal */ }
    }
    fetchSuggestions(doc.id)
  } catch (e) { logger.error('view','Viewer init error',e) }
  finally { documentLoading.value = false }
}

const fetchSuggestions = async (docId) => {
  if (suggestionsCache.has(docId)) { suggestions.value = suggestionsCache.get(docId); return }
  suggestionsLoading.value = true
  try {
    const r = await fetch(`/api/search/suggestions/${docId}?count=5`, { headers: authHeaders() })
    if (r.ok) { const raw = (await r.json()).filter(s => s.documentId !== docId); suggestions.value = raw; suggestionsCache.set(docId, raw) }
  } catch { /* non-fatal */ } finally { suggestionsLoading.value = false }
}

const openSuggestion = (sug) => {
  closeDocumentViewer()
  setTimeout(() => viewDocument({ id:sug.documentId, title:sug.title, fileName:sug.title, contentType:'application/pdf', score:sug.similarityScore }), 150)
}

const closeDocumentViewer = () => {
  revokeBlobUrl(); stopOcrPolling()
  showDocumentViewer.value = false; currentDocument.value = null
  documentUrl.value = null; documentContent.value = null; ocrStatus.value = null; suggestions.value = []
}

const searchByTag = (tag) => { closeDocumentViewer(); searchQuery.value = tag; handleSearch() }

const deleteDoc = async (doc) => {
  if (!confirm(`Supprimer "${doc.title}" ? Cette action est irréversible.`)) return
  const r = await fetch(`/api/documents/${doc.id}`, { method:'DELETE', headers:{ ...authHeaders(), 'Content-Type':'application/json' } })
  if (r.ok) {
    window.__gedNotify?.(`Document supprimé : ${doc.title}`, 'success', '🗑️')
    closeDocumentViewer(); handleSearch()
  }
  else window.__gedNotify?.('Erreur lors de la suppression.', 'error', '❌')
}



// ── Smart search with NLP ──────────────────────────────────────────────────────
const mapFileType = (t) => ({ pdf:'application/pdf', doc:'application/msword', docx:'application/vnd.openxmlformats-officedocument.wordprocessingml.document', xls:'application/vnd.ms-excel', xlsx:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', jpg:'image/jpeg', jpeg:'image/jpeg', png:'image/png', txt:'text/plain' }[t?.toLowerCase()] || null)

const buildSearchBody = (page = 1) => ({
  query:        searchQuery.value.trim(),   // raw query — backend handles all normalization
  searchType:   0,                          // Natural
  page,
  pageSize:     20,
  categories:   filters.category    ? [filters.category]    : null,
  contentTypes: filters.contentType ? [filters.contentType] : null,
  fromDate:     filters.dateFrom    || null,
  toDate:       filters.dateTo      || null,
  documentIds: attachedDocIds.value.length ? attachedDocIds.value : undefined,
  includeOcrContent: true
})

const handleSearch = () => {
  const query = searchQuery.value.trim()
  
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
  const query = searchQuery.value.trim()
  if (!query) return

  // Cancel any in-flight stream
  if (ragAbortCtrl.value) { ragAbortCtrl.value.abort(); ragAbortCtrl.value = null }

  // Push user message to history
  ragHistory.value.push({ role: 'user', content: query })

  ragAnswer.value  = ''
  ragSources.value = []
  ragLoading.value = true
  searched.value   = true
  searchResults.value     = null
  nlpInterpretation.value = null
  searchError.value       = null

  // Push empty assistant placeholder
  const assistantMsg = { role: 'assistant', content: '', sources: [] }
  ragHistory.value.push(assistantMsg)

  try {
    ragAbortCtrl.value = new AbortController()
    const res = await fetch('/api/rag/ask/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({
        query,
        language: 'fr',
        categories:  filters.category        ? [filters.category] : undefined,
        fromDate:    filters.dateFrom         || undefined,
        toDate:      filters.dateTo           || undefined,
        documentIds: attachedDocIds.value.length ? attachedDocIds.value : undefined,
      }),
      signal: ragAbortCtrl.value.signal
    })

    if (!res.ok) {
      searchError.value = `Erreur IA (HTTP ${res.status})`
      assistantMsg.content = `Erreur: HTTP ${res.status}`
      return
    }

    const reader = res.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() ?? ''

      for (const line of lines) {
        if (!line.startsWith('data: ')) continue
        try {
          const msg = JSON.parse(line.slice(6))
          if (msg.error) {
            assistantMsg.content += `[Erreur] ${msg.error}`
          } else if (msg.done) {
            // Final message contains sources
          } else if (msg.token) {
            assistantMsg.content += msg.token
            ragAnswer.value = assistantMsg.content
          }
        } catch { /* malformed JSON, skip */ }
      }
    }
  } catch (err) {
    if (err.name === 'AbortError') {
      assistantMsg.content += '\n[Réponse annulée]'
    } else {
      searchError.value = 'Impossible de contacter le service IA.'
      assistantMsg.content = 'Le service IA est indisponible.'
    }
  } finally {
    ragLoading.value = false
    ragAbortCtrl.value = null
    ragAnswer.value = assistantMsg.content
  }
}

const performSearch = async () => {
  if (!searchQuery.value.trim()) return

  searchLoading.value     = true
  searched.value          = true
  showAutocomplete.value  = false
  nlpInterpretation.value = null
  searchError.value       = null

  try {
    const res = await fetch('/api/search/query', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body:    JSON.stringify(buildSearchBody(1))
    })

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }))
      searchError.value = err.error || 'Erreur de recherche.'
      searchResults.value = null
      return
    }

    const data = await res.json()

    // ── Understood check ──────────────────────────────────────────────────
    if (data.isUnderstood === false) {
      searchResults.value = null
      // Show language-appropriate message
      const lang = data.detectedLanguage
      searchError.value = lang === 'ar'
        ? 'الرجاء إدخال مصطلح بحث صحيح.'
        : lang === 'fr'
          ? 'Veuillez entrer un terme de recherche valide.'
          : 'Please enter a proper search term.'
      return
    }

    searchResults.value = data

    // ── NLP banner ────────────────────────────────────────────────────────
    // nlpSummary comes from the backend, e.g. "Factures · PDF · depuis 2024-01-01"
    if (data.nlpSummary) {
      nlpInterpretation.value = data.nlpSummary
    }

  } catch (err) {
    console.error('[Search] Network error:', err)
    searchError.value = 'Erreur réseau. Vérifiez que le backend est démarré.'
  } finally {
    searchLoading.value = false
  }
}

const goToPage = async (page) => {
  if (!searchQuery.value.trim()) return
  searchLoading.value = true
  try {
    const res = await fetch('/api/search/query', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body:    JSON.stringify(buildSearchBody(page))
    })
    if (res.ok) {
      searchResults.value = await res.json()
      window.scrollTo({ top: 0, behavior: 'smooth' })
    }
  } finally {
    searchLoading.value = false }
}

// ── Upload (Batch) ───────────────────────────────────────────────────────────────
const handleFileSelectMultiple = (e) => {
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
  const inp = document.querySelector('.file-input'); if (inp) inp.value = ''
}
const clearFiles = () => {
  selectedFiles.value = []
  uploadData.title = ''
  uploadData.category = ''
  const inp = document.querySelector('.file-input'); if (inp) inp.value = ''
}
const closeUploadModal = () => { showUploadModal.value = false; clearFiles() }

const uploadDocuments = async () => {
  if (selectedFiles.value.length === 0 || !uploadData.category) return
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
      form.append('category', uploadData.category)
      
      const r = await fetch('/api/documents/upload', { 
        method: 'POST', 
        headers: { Authorization: `Bearer ${localStorage.getItem('ged_token')}` }, 
        body: form 
      })
      
      if (r.ok) {
        successCount++
      } else {
        errorCount++
        const err = await r.json().catch(() => ({}))
        window.__gedNotify?.(`Échec upload: ${file.name} — ${err.error || r.status}`, 'error', '❌')
      }
      uploadProgress.value = i + 1
    }
    
    if (successCount > 0) {
      const msg = `${successCount} document(s) importé(s) avec succès !${errorCount > 0 ? ` ${errorCount} échecs.` : ''}`
      window.__gedNotify?.(msg, errorCount > 0 ? 'info' : 'success', '✅')
      closeUploadModal()
      if (searchResults.value) handleSearch()
    } else {
      window.__gedNotify?.('Échec de l\'import de tous les fichiers.', 'error', '❌')
    }
  } catch { 
    window.__gedNotify?.("Erreur réseau lors de l'import.", 'error', '❌')
  }
  finally { 
    uploading.value = false 
    uploadProgress.value = 0
  }
}
</script>

<style scoped>
.user-layout { display:flex; min-height:100vh; background:#f1f5f9; font-family:'Segoe UI',system-ui,sans-serif; }

/* ── Sidebar ── */
.sidebar { width:240px; min-height:100vh; background:#0f172a; display:flex; flex-direction:column; position:sticky; top:0; height:100vh; overflow-y:auto; }
.sidebar-brand { display:flex; align-items:center; gap:.75rem; padding:1.25rem 1rem; border-bottom:1px solid #1e293b; }
.brand-icon { width:36px; height:36px; background:linear-gradient(135deg,#3b82f6,#8b5cf6); border-radius:10px; display:flex; align-items:center; justify-content:center; }
.brand-icon svg { width:20px; height:20px; color:white; }
.brand-name { font-weight:700; font-size:1rem; color:white; }
.sidebar-nav { flex:1; padding:1rem .75rem; display:flex; flex-direction:column; gap:.25rem; }
.nav-item { display:flex; align-items:center; gap:.625rem; padding:.625rem .875rem; border-radius:8px; background:none; border:none; color:#94a3b8; font-size:.875rem; cursor:pointer; transition:all .15s; text-align:left; }
.nav-item:hover { background:#1e293b; color:#e2e8f0; }
.nav-item.active { background:#1d4ed8; color:white; font-weight:600; }
.nav-icon { width:18px; height:18px; flex-shrink:0; display:flex; align-items:center; }
.nav-icon :deep(svg) { width:18px; height:18px; }
.sidebar-footer { padding:.875rem; border-top:1px solid #1e293b; display:flex; align-items:center; gap:.5rem; }
.user-badge { display:flex; align-items:center; gap:.625rem; flex:1; overflow:hidden; }
.user-avatar { width:34px; height:34px; background:linear-gradient(135deg,#3b82f6,#8b5cf6); border-radius:50%; color:white; font-size:.75rem; font-weight:700; display:flex; align-items:center; justify-content:center; flex-shrink:0; }
.user-info { overflow:hidden; }
.user-name { font-size:.8rem; font-weight:600; color:#e2e8f0; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.user-role-tag { font-size:.65rem; font-weight:600; padding:.1rem .35rem; border-radius:9999px; width:fit-content; margin-top:.15rem; }
.tag-admin    { background:#fef3c7; color:#92400e; }
.tag-manager  { background:#dbeafe; color:#1d4ed8; }
.tag-user     { background:#d1fae5; color:#065f46; }
.tag-readonly { background:#374151; color:#9ca3af; }
.logout-btn { background:none; border:none; color:#64748b; cursor:pointer; padding:.375rem; border-radius:6px; display:flex; align-items:center; transition:all .15s; }
.logout-btn svg { width:18px; height:18px; }
.logout-btn:hover { background:#1e293b; color:#f87171; }

/* ── Main ── */
.user-main { flex:1; padding:1.5rem; overflow-y:auto; min-width:0; }

/* ── Search ── */
.search-section { display:flex; flex-direction:column; gap:1.25rem; }
.search-card { background:white; border-radius:14px; box-shadow:0 4px 16px rgba(0,0,0,.06); padding:1.5rem; border:1px solid #e5e7eb; }
.search-bar-wrapper { display:flex; gap:.625rem; flex-wrap:wrap; }
.search-input-wrapper { flex:1; min-width:200px; position:relative; display:flex; align-items:center; }
.search-icon { position:absolute; left:.875rem; width:20px; height:20px; color:#9ca3af; pointer-events:none; }
.search-input { width:100%; padding:.8rem 1rem .8rem 2.75rem; font-size:.9rem; border:2px solid #e5e7eb; border-radius:9px; outline:none; transition:all .2s; }
.search-input:focus { border-color:#3b82f6; box-shadow:0 0 0 3px rgba(59,130,246,.1); }
.search-btn { padding:.8rem 1.5rem; background:linear-gradient(135deg,#2563eb,#4f46e5); color:white; border:none; border-radius:9px; font-weight:600; cursor:pointer; transition:all .2s; white-space:nowrap; }
.search-btn:hover:not(:disabled) { box-shadow:0 6px 14px rgba(37,99,235,.3); transform:translateY(-1px); }
.search-btn:disabled { opacity:.5; cursor:not-allowed; }
.upload-btn { display:flex; align-items:center; gap:.4rem; padding:.8rem 1.1rem; background:#f0fdf4; color:#16a34a; border:1.5px solid #86efac; border-radius:9px; font-weight:600; font-size:.875rem; cursor:pointer; transition:all .2s; white-space:nowrap; }
.upload-btn svg { width:16px; height:16px; }
.upload-btn:hover { background:#dcfce7; }
.loading-text { display:flex; align-items:center; gap:.5rem; }
.quick-searches { margin-top:.75rem; display:flex; flex-wrap:wrap; gap:.5rem; align-items:center; }
.quick-label { font-size:.78rem; color:#6b7280; }
.quick-btn { padding:.28rem .65rem; font-size:.78rem; background:#f3f4f6; color:#374151; border:none; border-radius:7px; cursor:pointer; transition:all .15s; }
.quick-btn:hover { background:#dbeafe; color:#1d4ed8; }
.filters-toggle { margin-top:.75rem; display:inline-flex; align-items:center; gap:.25rem; color:#2563eb; background:none; border:none; font-size:.8rem; font-weight:500; cursor:pointer; }
.toggle-icon { width:14px; height:14px; }
.filters-panel { margin-top:1rem; padding-top:1rem; border-top:1px solid #f0f0f0; }
.filters-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(160px,1fr)); gap:.875rem; }
.filter-group { display:flex; flex-direction:column; }
.filter-label { font-size:.78rem; font-weight:600; color:#374151; margin-bottom:.35rem; }
.filter-select,.filter-input { width:100%; padding:.5rem .75rem; border:2px solid #e5e7eb; border-radius:8px; outline:none; font-size:.875rem; transition:all .2s; background:white; }
.filter-select:focus,.filter-input:focus { border-color:#3b82f6; }
.results-summary { display:flex; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:.75rem; }
.summary-card { background:white; border-radius:9px; box-shadow:0 1px 4px rgba(0,0,0,.07); padding:.5rem 1.1rem; font-size:.875rem; }
.summary-count { font-weight:700; color:#111827; }
.summary-text  { color:#6b7280; }
.summary-divider { color:#d1d5db; margin:0 .4rem; }
.summary-time  { font-weight:600; color:#2563eb; }
.summary-page  { font-size:.8rem; color:#6b7280; }
.documents-grid { display:grid; gap:.875rem; }
.document-card { background:white; border-radius:12px; box-shadow:0 2px 8px rgba(0,0,0,.05); border:1px solid #f0f0f0; transition:all .2s; }
.document-card:hover { box-shadow:0 10px 24px rgba(0,0,0,.1); transform:translateY(-2px); }
.card-content { padding:1.1rem; display:flex; justify-content:space-between; gap:1rem; flex-wrap:wrap; }
.doc-info { flex:1; min-width:0; }
.doc-header { display:flex; gap:.75rem; }
.file-icon-box { width:46px; height:46px; border-radius:10px; background:linear-gradient(135deg,#dbeafe,#e0e7ff); display:flex; align-items:center; justify-content:center; flex-shrink:0; }
.icon-emoji { font-size:1.6rem; }
.doc-details { flex:1; min-width:0; }
.doc-title { font-size:1rem; font-weight:700; color:#111827; margin-bottom:.3rem; word-break:break-word; transition:color .2s; }
.document-card:hover .doc-title { color:#2563eb; }
.doc-description { display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.highlights { display:flex; flex-direction:column; gap:.35rem; margin-bottom:.6rem; }
.highlight-item { font-size:.78rem; background:#fef9c3; border:1px solid #fde68a; border-radius:6px; padding:.4rem .65rem; font-style:italic; color:#374151; }
.metadata-row { display:flex; flex-wrap:wrap; gap:.5rem; font-size:.78rem; color:#6b7280; margin-bottom:.5rem; }
.meta-item { display:inline-flex; align-items:center; gap:.2rem; }
.meta-highlight { background:linear-gradient(135deg,#fef3c7,#fde68a); padding:.18rem .4rem; border-radius:5px; font-weight:600; color:#78350f; }
.category-badge { padding:.18rem .55rem; background:linear-gradient(135deg,#dbeafe,#e0e7ff); color:#1d4ed8; border-radius:9999px; font-weight:500; }
.tags-row { display:flex; flex-wrap:wrap; gap:.35rem; }
.tag { padding:.18rem .5rem; background:#f3f4f6; color:#374151; font-size:.72rem; border-radius:5px; }
.tag-more { color:#9ca3af; font-size:.72rem; }
.doc-actions { display:flex; flex-direction:column; align-items:center; gap:.625rem; }
.score-wrapper { text-align:center; }
.score-circle { position:relative; width:68px; height:68px; }
.circle-svg { width:100%; height:100%; transform:rotate(-90deg); }
.circle-bg { fill:none; stroke:#e5e7eb; stroke-width:8; }
.circle-progress { fill:none; stroke:#2563eb; stroke-width:8; stroke-dasharray:251; stroke-dashoffset:251; transition:stroke-dashoffset .8s ease; stroke-linecap:round; }
.score-text { position:absolute; top:50%; left:50%; transform:translate(-50%,-50%); }
.score-value { font-size:1rem; font-weight:700; background:linear-gradient(135deg,#2563eb,#4f46e5); -webkit-background-clip:text; -webkit-text-fill-color:transparent; background-clip:text; }
.score-label { font-size:.68rem; color:#9ca3af; margin-top:.15rem; }
.view-btn { display:inline-flex; align-items:center; gap:.3rem; padding:.5rem 1.1rem; background:linear-gradient(135deg,#059669,#10b981); color:white; border:none; border-radius:9px; font-weight:600; font-size:.82rem; cursor:pointer; transition:all .2s; white-space:nowrap; }
.view-btn svg { width:15px; height:15px; }
.view-btn:hover { box-shadow:0 6px 12px rgba(5,150,105,.3); transform:translateY(-1px); }
.delete-btn { background:#fef2f2; border:1px solid #fecaca; color:#dc2626; border-radius:7px; padding:.3rem .6rem; font-size:.85rem; cursor:pointer; transition:all .15s; }
.delete-btn:hover { background:#fee2e2; }
.pagination { display:flex; background:white; border-radius:9px; box-shadow:0 1px 4px rgba(0,0,0,.07); padding:.35rem; gap:.2rem; justify-content:center; flex-wrap:wrap; }
.page-btn { padding:.4rem .8rem; border-radius:7px; font-weight:500; border:none; cursor:pointer; color:#374151; background:transparent; transition:all .15s; }
.page-btn:hover { background:#f3f4f6; }
.page-btn.active { background:linear-gradient(135deg,#2563eb,#4f46e5); color:white; }
.empty-state,.initial-state { text-align:center; padding:4rem 1rem; }
.state-icon { display:inline-flex; align-items:center; justify-content:center; width:80px; height:80px; border-radius:50%; margin-bottom:1rem; }
.empty-icon { background:linear-gradient(135deg,#f3f4f6,#e5e7eb); }
.initial-icon { background:linear-gradient(135deg,#dbeafe,#e0e7ff); }
.empty-icon svg,.initial-icon svg { width:40px; height:40px; color:#9ca3af; }
.initial-icon svg { color:#2563eb; }
.empty-state h3,.initial-state h3 { font-size:1.25rem; font-weight:700; color:#111827; margin-bottom:.35rem; }
.empty-state p,.initial-state p { color:#6b7280; margin-bottom:1rem; }
.clear-btn { padding:.6rem 1.25rem; background:#f3f4f6; color:#374151; border:none; border-radius:9px; font-weight:500; cursor:pointer; }
.clear-btn:hover { background:#e5e7eb; }

/* ── RAG redirect ── */
.page-header { display:flex; align-items:center; justify-content:space-between; margin-bottom:1.5rem; }
.page-title    { font-size:1.5rem; font-weight:700; color:#0f172a; }
.page-subtitle { font-size:.875rem; color:#64748b; margin-top:.2rem; }
.redirect-card { background:white; border-radius:12px; border:1px solid #e2e8f0; padding:2.5rem; text-align:center; }
.redirect-card p { color:#64748b; margin-bottom:1rem; }
.btn-primary { display:inline-flex; align-items:center; gap:.5rem; padding:.6rem 1.25rem; background:linear-gradient(135deg,#2563eb,#4f46e5); color:white; border:none; border-radius:9px; font-size:.875rem; font-weight:600; cursor:pointer; text-decoration:none; transition:opacity .2s; }
.btn-primary:hover { opacity:.9; }

/* ══════════════════════════════════════════════════════════════════════════════
   VIEWER MODAL
══════════════════════════════════════════════════════════════════════════════ */
.modal-overlay { position:fixed; inset:0; background:rgba(0,0,0,.6); backdrop-filter:blur(6px); z-index:200; display:flex; align-items:center; justify-content:center; padding:1rem; }
.viewer-modal { background:#fff; border-radius:18px; box-shadow:0 32px 64px -12px rgba(0,0,0,.3); width:96vw; max-width:1380px; height:90vh; display:flex; flex-direction:column; overflow:hidden; }
.viewer-header { display:flex; align-items:center; justify-content:space-between; padding:.875rem 1.25rem; background:linear-gradient(to right,#f8fafc,#f0f9ff); border-bottom:1px solid #e5e7eb; gap:1rem; flex-shrink:0; }
.viewer-header-left { display:flex; align-items:center; gap:.875rem; min-width:0; flex:1; }
.viewer-file-badge { flex-shrink:0; padding:.2rem .55rem; background:linear-gradient(135deg,#2563eb,#4f46e5); color:white; font-size:.62rem; font-weight:800; border-radius:5px; letter-spacing:.05em; text-transform:uppercase; }
.viewer-title-block { min-width:0; }
.viewer-title { font-size:1rem; font-weight:700; color:#111827; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.viewer-filename { font-size:.76rem; color:#6b7280; display:flex; align-items:center; gap:.3rem; flex-wrap:wrap; }
.vf-sep { color:#d1d5db; }
.vf-cat { background:#dbeafe; color:#1d4ed8; padding:.1rem .4rem; border-radius:9999px; font-weight:600; font-size:.68rem; }
.viewer-header-actions { display:flex; align-items:center; gap:.5rem; flex-shrink:0; }
.tab-switcher { display:flex; background:#f3f4f6; border-radius:8px; padding:.18rem; gap:.18rem; }
.tab-btn { display:flex; align-items:center; gap:.3rem; padding:.3rem .7rem; border:none; border-radius:7px; font-size:.78rem; font-weight:600; cursor:pointer; color:#6b7280; background:transparent; transition:all .15s; }
.tab-btn svg { width:13px; height:13px; }
.tab-btn.active { background:white; color:#1d4ed8; box-shadow:0 1px 3px rgba(0,0,0,.1); }
.hdr-close { display:flex; align-items:center; padding:.4rem; border:none; border-radius:7px; background:#f3f4f6; color:#6b7280; cursor:pointer; transition:all .15s; }
.hdr-close svg { width:17px; height:17px; }
.hdr-close:hover { background:#fee2e2; color:#dc2626; }
.viewer-body { flex:1; display:grid; grid-template-columns:1fr 320px; overflow:hidden; }
.viewer-preview-pane { border-right:1px solid #e5e7eb; overflow:hidden; display:flex; flex-direction:column; background:#f9fafb; }
.preview-loading { flex:1; display:flex; flex-direction:column; align-items:center; justify-content:center; gap:1rem; position:relative; color:#6b7280; font-size:.9rem; }
.pulse-ring { position:absolute; width:90px; height:90px; border-radius:50%; border:3px solid #3b82f6; animation:pulse-ring 1.6s ease-out infinite; }
@keyframes pulse-ring { 0%{transform:scale(.85);opacity:.8}100%{transform:scale(1.4);opacity:0} }
.pdf-viewer { flex:1; display:flex; }
.pdf-frame  { flex:1; width:100%; border:none; }
.image-viewer { flex:1; overflow:auto; display:flex; flex-direction:column; align-items:center; justify-content:center; padding:1.5rem; }
.document-image { max-width:100%; max-height:70vh; border-radius:10px; box-shadow:0 8px 24px rgba(0,0,0,.15); display:block; }
.image-hint { font-size:.73rem; color:#9ca3af; margin-top:.5rem; }
.text-viewer { flex:1; display:flex; flex-direction:column; overflow:hidden; }
.text-toolbar { display:flex; gap:1rem; padding:.5rem 1rem; background:white; border-bottom:1px solid #f0f0f0; font-size:.76rem; color:#9ca3af; flex-shrink:0; }
.text-content { flex:1; overflow:auto; padding:1.25rem; font-family:'Fira Mono','Courier New',monospace; font-size:.78rem; line-height:1.65; background:#0f172a; color:#e2e8f0; white-space:pre-wrap; word-break:break-word; }
.office-viewer { flex:1; display:flex; flex-direction:column; overflow:hidden; }
.office-tabs { display:flex; padding:.75rem 1rem 0; background:white; flex-shrink:0; }
.otab { padding:.4rem .9rem; border:1px solid #e5e7eb; background:#f9fafb; font-size:.78rem; font-weight:600; cursor:pointer; color:#6b7280; transition:all .15s; }
.otab:first-child { border-radius:8px 0 0 8px; }
.otab:last-child  { border-radius:0 8px 8px 0; border-left:none; }
.otab.active { background:white; color:#2563eb; border-color:#3b82f6; }
.office-text-panel { flex:1; overflow:hidden; display:flex; flex-direction:column; }
.office-text-stats { display:flex; gap:1.25rem; padding:.45rem 1rem; background:#f8fafc; border-bottom:1px solid #f0f0f0; font-size:.73rem; color:#9ca3af; flex-shrink:0; }
.office-text-wrap { flex:1; display:flex; flex-direction:column; overflow:hidden; }
.office-text-content { flex:1; overflow:auto; padding:1.25rem; font-size:.83rem; line-height:1.7; color:#1e293b; background:white; white-space:pre-wrap; word-break:break-word; }
.office-no-text { flex:1; display:flex; flex-direction:column; align-items:center; justify-content:center; gap:.75rem; padding:2rem; text-align:center; }
.ont-icon { font-size:4rem; }
.ont-title { font-size:1.1rem; font-weight:700; color:#374151; }
.ont-sub { font-size:.83rem; color:#6b7280; max-width:280px; }
.office-embed-panel { flex:1; padding:1.5rem; display:flex; align-items:center; justify-content:center; }
.office-embed-notice { background:#eff6ff; border:1px solid #bfdbfe; border-radius:9px; padding:1.25rem; font-size:.83rem; color:#1d4ed8; display:flex; flex-direction:column; gap:.5rem; max-width:360px; text-align:center; }
.office-embed-link { font-weight:700; color:#1d4ed8; }
.audio-viewer { flex:1; display:flex; flex-direction:column; align-items:center; justify-content:center; gap:2rem; padding:2rem; background:linear-gradient(135deg,#0f172a,#1e293b); }
.audio-art { text-align:center; }
.audio-wave { display:flex; align-items:center; justify-content:center; gap:3px; height:70px; margin-bottom:1rem; }
.wave-bar { width:4px; border-radius:2px; background:linear-gradient(to top,#3b82f6,#818cf8); height:40%; animation:wave 1.2s ease-in-out infinite alternate; }
@keyframes wave { 0%{transform:scaleY(.3)}100%{transform:scaleY(1)} }
.audio-player { width:100%; max-width:400px; border-radius:9px; }
.video-viewer { flex:1; display:flex; align-items:center; justify-content:center; background:#000; }
.video-player { max-width:100%; max-height:100%; }
.unsupported-viewer { flex:1; display:flex; flex-direction:column; align-items:center; justify-content:center; gap:.75rem; padding:2rem; text-align:center; }
.unsupported-viewer h3 { font-size:1.2rem; font-weight:700; color:#111827; }
.unsupported-viewer p  { color:#6b7280; }
.unsupported-viewer code { font-size:.75rem; font-family:monospace; background:#f3f4f6; padding:.3rem .65rem; border-radius:5px; color:#374151; }


.search-error-banner {display: flex;    align-items: center;    justify-content: center;    margin-top: 2rem;    padding: 1rem 1.5rem;    background: #fef2f2;    border: 1px solid #fecaca;    border-radius: 8px;    color: #b91c1c;    font-size: 0.95rem;    gap: 0.5rem;  }
.search-error-banner::before { content: '⚠️'; }


/* Details pane */
.viewer-details-pane { overflow-y:auto; display:flex; flex-direction:column; background:#fafafa; }
.ocr-status-bar { display:flex; align-items:center; gap:.5rem; padding:.55rem 1rem; font-size:.76rem; font-weight:600; flex-shrink:0; }
.ocr-dot { width:8px; height:8px; border-radius:50%; flex-shrink:0; }
.ocr-done    { background:#d1fae5; color:#065f46; border-bottom:1px solid #a7f3d0; }
.ocr-done    .ocr-dot { background:#10b981; }
.ocr-fail    { background:#fee2e2; color:#991b1b; border-bottom:1px solid #fca5a5; }
.ocr-fail    .ocr-dot { background:#ef4444; }
.ocr-pending { background:#fef3c7; color:#92400e; border-bottom:1px solid #fde68a; }
.ocr-pending .ocr-dot { background:#f59e0b; animation:blink 1s ease infinite; }
.ocr-partial { background:#eff6ff; color:#1d4ed8; border-bottom:1px solid #bfdbfe; }
.ocr-partial .ocr-dot { background:#3b82f6; animation:blink 1.2s ease infinite; }
@keyframes blink { 0%,100%{opacity:1}50%{opacity:.3} }
.detail-section { padding:.875rem 1rem; border-bottom:1px solid #f0f0f0; }
.detail-section:last-child { border-bottom:none; }
.detail-section-title { display:flex; align-items:center; gap:.4rem; font-size:.75rem; font-weight:800; color:#374151; text-transform:uppercase; letter-spacing:.05em; margin-bottom:.7rem; }
.detail-section-title svg { width:13px; height:13px; color:#6b7280; }
.detail-list { display:flex; flex-direction:column; gap:.45rem; }
.dl-row { display:grid; grid-template-columns:80px 1fr; gap:.45rem; align-items:start; }
.dl-row dt { font-size:.73rem; font-weight:600; color:#9ca3af; padding-top:.1rem; }
.dl-row dd { font-size:.8rem; color:#111827; word-break:break-all; }
.dd-mono   { font-family:monospace; font-size:.73rem; color:#374151; }
.dd-accent { font-weight:600; color:#047857; }
.mime-badge { display:inline-block; background:#eff6ff; color:#1d4ed8; font-size:.65rem; font-weight:800; padding:.1rem .4rem; border-radius:4px; text-transform:uppercase; margin-right:.3rem; }
.mime-text  { font-size:.7rem; color:#6b7280; font-family:monospace; }
.cat-pill { display:inline-block; background:linear-gradient(135deg,#dbeafe,#e0e7ff); color:#1d4ed8; font-size:.75rem; font-weight:600; padding:.15rem .55rem; border-radius:9999px; }
.relevance-bar-wrap { display:flex; align-items:center; gap:.5rem; }
.relevance-bar { flex:1; height:6px; background:#e5e7eb; border-radius:3px; overflow:hidden; }
.relevance-fill { height:100%; background:linear-gradient(90deg,#2563eb,#4f46e5); border-radius:3px; transition:width .8s ease; }
.relevance-pct { font-size:.73rem; font-weight:700; color:#2563eb; white-space:nowrap; }
.tags-cloud { display:flex; flex-wrap:wrap; gap:.35rem; }
.tag-cloud-item { padding:.22rem .55rem; background:#f0f9ff; border:1px solid #bae6fd; color:#0369a1; font-size:.73rem; font-weight:600; border-radius:6px; cursor:pointer; transition:all .15s; }
.tag-cloud-item:hover { background:#0369a1; color:white; border-color:#0369a1; }
.detail-desc { font-size:.81rem; color:#374151; line-height:1.6; }
.detail-highlights { display:flex; flex-direction:column; gap:.35rem; margin-top:.5rem; }
.detail-highlight-item { font-size:.76rem; background:#fef9c3; border-left:3px solid #fbbf24; padding:.35rem .55rem; border-radius:0 5px 5px 0; font-style:italic; color:#374151; }
.suggestions-loading { display:flex; align-items:center; gap:.5rem; font-size:.8rem; color:#9ca3af; }
.suggestions-empty { font-size:.8rem; color:#9ca3af; }
.suggestions-list { display:flex; flex-direction:column; gap:.45rem; }
.suggestion-card { display:flex; align-items:center; gap:.7rem; padding:.6rem .7rem; background:white; border:1px solid #e5e7eb; border-radius:9px; cursor:pointer; transition:all .15s; }
.suggestion-card:hover { border-color:#3b82f6; background:#eff6ff; }
.sug-icon { font-size:1.4rem; flex-shrink:0; }
.sug-info { flex:1; min-width:0; }
.sug-title  { font-size:.8rem; font-weight:600; color:#111827; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.sug-reason { font-size:.7rem; color:#9ca3af; margin-top:.1rem; }
.sug-score-ring { --pct:0; width:38px; height:38px; border-radius:50%; flex-shrink:0; background:conic-gradient(#2563eb calc(var(--pct) * 1%),#e5e7eb 0); display:flex; align-items:center; justify-content:center; }
.sug-score-ring span { font-size:.62rem; font-weight:800; color:#1d4ed8; background:white; width:26px; height:26px; border-radius:50%; display:flex; align-items:center; justify-content:center; }
.spinner { animation:spin 1s linear infinite; }
.spinner.sm { width:15px; height:15px; }
.spinner.xl { width:38px; height:38px; }
.spinner-bg   { opacity:.25; }
.spinner-path { opacity:.75; }
@keyframes spin { to{transform:rotate(360deg)} }

/* ── Mobile ── */
@media(min-width:900px) { .tab-hidden{display:flex !important} .tab-switcher{display:none} }
@media(max-width:899px) {
  .viewer-body{grid-template-columns:1fr}
  .viewer-preview-pane{border-right:none;border-bottom:1px solid #e5e7eb;height:55vh}
  .viewer-details-pane{height:auto;max-height:35vh}
  .tab-hidden{display:none !important}
}

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

/* ── OCR badges on cards ── */
.ocr-badge { display:inline-flex; align-items:center; gap:.2rem; padding:.14rem .42rem; border-radius:5px; font-size:.68rem; font-weight:700; }
.ocr-badge-done    { background:#d1fae5; color:#065f46; border:1px solid #6ee7b7; }
.ocr-badge-pending { background:#fef3c7; color:#92400e; border:1px solid #fde68a; }
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

/* ── Filter reset row ── */
.filters-reset-row { margin-top:.75rem; display:flex; justify-content:flex-end; }
.filters-reset-btn { background:none; border:1px solid #fca5a5; color:#dc2626; border-radius:7px; padding:.28rem .75rem; font-size:.76rem; font-weight:600; cursor:pointer; transition:all .15s; }
.filters-reset-btn:hover { background:#fee2e2; }

/* ══════════════════════════════════════════════════════════════════════════════
   UPLOAD MODAL
══════════════════════════════════════════════════════════════════════════════ */
.modal-content { background:white; border-radius:14px; box-shadow:0 25px 50px rgba(0,0,0,.2); max-width:42rem; width:100%; max-height:90vh; overflow-y:auto; padding:2rem; }
.modal-large { max-width:56rem; }

.batch-upload-area { min-height:200px; }
.files-preview-list { display:flex; flex-direction:column; gap:.5rem; max-height:300px; overflow-y:auto; }
.file-preview-item { display:flex; align-items:center; gap:.75rem; padding:.6rem .75rem; background:#f9fafb; border-radius:8px; border:1px solid #e5e7eb; }
.file-preview-item .file-icon { font-size:1.5rem; }
.file-preview-item .file-info { flex:1; min-width:0; }
.file-preview-item .file-name { font-weight:500; color:#1f2937; font-size:.875rem; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.file-preview-item .file-size { font-size:.75rem; color:#6b7280; }
.file-remove-btn { background:none; border:none; color:#ef4444; cursor:pointer; font-size:1rem; padding:.25rem; border-radius:4px; }
.file-remove-btn:hover { background:#fee2e2; }

.batch-category-section { margin-top:1rem; padding-top:1rem; border-top:1px solid #e5e7eb; }
.batch-info { font-size:.8rem; color:#6b7280; margin-bottom:.75rem; }
.modal-header { display:flex; align-items:center; justify-content:space-between; margin-bottom:1.5rem; }
.modal-header h2 { font-size:1.25rem; font-weight:700; color:#111827; }
.modal-close { background:none; border:none; color:#9ca3af; cursor:pointer; font-size:1.2rem; padding:.2rem .5rem; border-radius:5px; }
.modal-close:hover { background:#f3f4f6; color:#374151; }
.modal-body-upload { display:flex; flex-direction:column; gap:1.1rem; }
.upload-area { border:2px dashed #d1d5db; border-radius:11px; padding:2rem; text-align:center; cursor:pointer; transition:all .2s; min-height:120px; display:flex; align-items:center; justify-content:center; }
.upload-area:hover { border-color:#60a5fa; background:#f0f9ff; }
.file-input { display:none; }
.upload-prompt { display:flex; flex-direction:column; align-items:center; gap:.4rem; }
.upload-icon { width:48px; height:48px; color:#9ca3af; }
.upload-prompt p { font-size:.9rem; font-weight:500; color:#374151; }
.upload-hint { font-size:.78rem !important; color:#6b7280 !important; font-weight:400 !important; }
.file-preview { display:flex; align-items:center; gap:.875rem; cursor:default; }
.form-group { display:flex; flex-direction:column; }
.form-label { font-size:.82rem; font-weight:600; color:#374151; margin-bottom:.35rem; }
.form-input { width:100%; padding:.65rem .875rem; border:2px solid #e5e7eb; border-radius:9px; outline:none; font-size:.875rem; transition:all .2s; }
.form-input:focus { border-color:#3b82f6; }
.input-error { border-color:#ef4444 !important; background:#fef2f2; }
.modal-actions { display:flex; gap:.75rem; }
.upload-submit { flex:1; padding:.7rem; background:linear-gradient(135deg,#2563eb,#4f46e5); color:white; border:none; border-radius:9px; font-weight:700; cursor:pointer; transition:all .2s; display:flex; align-items:center; justify-content:center; gap:.5rem; }
.upload-submit:hover:not(:disabled) { box-shadow:0 6px 14px rgba(37,99,235,.35); }
.upload-submit:disabled { opacity:.5; cursor:not-allowed; }
.cancel-btn { padding:.7rem 1.4rem; background:#f3f4f6; color:#374151; border:none; border-radius:9px; font-weight:600; cursor:pointer; }
.cancel-btn:hover { background:#e5e7eb; }


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

/* ── RAG Conversation History ── */
.rag-conversation { display:flex; flex-direction:column; gap:.875rem; padding:1rem 1.25rem; max-height:420px; overflow-y:auto; }
.rag-msg { display:flex; align-items:flex-start; gap:.6rem; }
.rag-msg-user { flex-direction:row-reverse; }
.rag-msg-user .rag-msg-bubble { background:#2563eb; color:white; border-radius:14px 4px 14px 14px; }
.rag-msg-user .rag-msg-text { color:white; }
.rag-msg-user .rag-msg-avatar { background:#1d4ed8; color:white; font-size:.65rem; font-weight:700; }
.rag-msg-elise .rag-msg-bubble { background:#f0f4ff; border:1px solid #dde4ff; border-radius:4px 14px 14px 14px; }
.rag-msg-avatar { width:30px; height:30px; border-radius:50%; flex-shrink:0; display:flex; align-items:center; justify-content:center; font-size:.8rem; }
.rag-msg-bubble { padding:.55rem .875rem; max-width:75%; }
.rag-msg-text { font-size:.875rem; line-height:1.6; white-space:pre-wrap; word-break:break-word; color:#1f2937; }
.rag-clear-btn { background:rgba(255,255,255,.15); border:1px solid rgba(255,255,255,.3); color:white; border-radius:7px; padding:.2rem .6rem; font-size:.72rem; font-weight:600; cursor:pointer; transition:background .2s; }
.rag-clear-btn:hover { background:rgba(255,255,255,.3); }

.elise-attach-btn { padding: .65rem .9rem; border: 2px dashed #a5b4fc; border-radius: 9px; background: #f5f3ff; color: #4f46e5; font-size: .82rem; font-weight: 600; cursor: pointer; white-space: nowrap; transition: all .2s; }
.elise-attach-btn:hover { background: #ede9fe; border-color: #6366f1; }

.picker-overlay { position: fixed; inset: 0; background: rgba(0,0,0,.45); z-index: 1000; display: flex; align-items: center; justify-content: center; }
.picker-modal { background: white; border-radius: 16px; width: 480px; max-width: 95vw; max-height: 80vh; display: flex; flex-direction: column; box-shadow: 0 20px 60px rgba(0,0,0,.2); overflow: hidden; }
.picker-modal-header { display: flex; align-items: center; justify-content: space-between; padding: 1.25rem 1.5rem; border-bottom: 1px solid #f0f0f0; }
.picker-modal-title { font-size: 1rem; font-weight: 700; color: #1f2937; margin: 0; }
.picker-modal-close { background: none; border: none; font-size: 1.1rem; color: #9ca3af; cursor: pointer; padding: .25rem; border-radius: 6px; }
.picker-modal-close:hover { background: #f3f4f6; color: #374151; }
.picker-modal-search { display: flex; align-items: center; gap: .5rem; margin: 1rem 1.5rem .5rem; border: 1.5px solid #e5e7eb; border-radius: 9px; padding: .5rem .75rem; }
.picker-modal-search:focus-within { border-color: #6366f1; box-shadow: 0 0 0 3px rgba(99,102,241,.1); }
.picker-modal-search-input { flex: 1; border: none; outline: none; font-size: .875rem; color: #1f2937; background: transparent; }
.picker-modal-search-clear { background: none; border: none; color: #9ca3af; cursor: pointer; font-size: .9rem; }
.picker-modal-body { flex: 1; overflow-y: auto; padding: .5rem 1rem 1rem; display: flex; flex-direction: column; gap: .35rem; }
.picker-modal-loading, .picker-modal-empty { display: flex; align-items: center; justify-content: center; gap: .5rem; color: #9ca3af; font-size: .875rem; padding: 2rem; }
.picker-modal-item { display: flex; align-items: center; gap: .75rem; padding: .65rem .75rem; border-radius: 9px; border: 1.5px solid transparent; cursor: pointer; transition: all .15s; }
.picker-modal-item:hover { background: #f5f3ff; border-color: #e0e7ff; }
.picker-modal-item.selected { background: #ede9fe; border-color: #a5b4fc; }
.picker-modal-icon { font-size: 1.2rem; flex-shrink: 0; }
.picker-modal-info { flex: 1; min-width: 0; }
.picker-modal-name { display: block; font-size: .875rem; font-weight: 500; color: #1f2937; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.picker-modal-meta { display: block; font-size: .75rem; color: #9ca3af; margin-top: 2px; }
.picker-modal-check { color: #4f46e5; font-weight: 700; font-size: 1rem; flex-shrink: 0; }
.picker-modal-footer { display: flex; align-items: center; gap: .75rem; padding: 1rem 1.5rem; border-top: 1px solid #f0f0f0; }
.picker-modal-count { font-size: .8rem; color: #6b7280; flex: 1; }
.picker-modal-clear-btn { padding: .45rem .9rem; border: 1px solid #e5e7eb; border-radius: 7px; background: white; color: #6b7280; font-size: .8rem; cursor: pointer; }
.picker-modal-clear-btn:hover { background: #f9fafb; }
.picker-modal-confirm-btn { padding: .45rem 1.1rem; border: none; border-radius: 7px; background: linear-gradient(135deg,#4f46e5,#2563eb); color: white; font-size: .85rem; font-weight: 600; cursor: pointer; }
.picker-modal-confirm-btn:hover { opacity: .9; }


</style>


================================================================================