<template>
  <div class="app-container">
    <!-- Modern Header with Gradient -->
    <header class="app-header">
      <div class="header-content">
        <div class="header-left">
          <div class="logo-icon">
            <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
          </div>
          <div class="header-text">
            <h1 class="header-title">GED Search Engine</h1>
            <p class="header-subtitle">Intelligent Document Management</p>
          </div>
        </div>
        <div class="header-right">
          <button @click="showUploadModal = true" class="upload-btn">
            <svg class="btn-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
            </svg>
            <span class="btn-text">Upload Document</span>
          </button>
        </div>
      </div>
    </header>

    <!-- Main Content -->
    <main class="main-content">
      <!-- Search Section -->
      <section class="search-section">
        <div class="search-card">
          <div class="search-bar-wrapper">
            <div class="search-input-wrapper">
              <svg class="search-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
              <input
                v-model="searchQuery"
                @keyup.enter="performSearch"
                type="text"
                placeholder="Search using natural language... e.g., 'find invoices from last month'"
                class="search-input"
              />
            </div>
            <button @click="performSearch" :disabled="loading || !searchQuery.trim()" class="search-btn">
              <span v-if="!loading">Search</span>
              <span v-else class="loading-text">
                <svg class="spinner" fill="none" viewBox="0 0 24 24">
                  <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                Searching...
              </span>
            </button>
          </div>

          <div class="quick-searches">
            <span class="quick-label">Try:</span>
            <button v-for="suggestion in quickSearches" :key="suggestion" @click="searchQuery = suggestion; performSearch()" class="quick-btn">
              {{ suggestion }}
            </button>
          </div>

          <button @click="showFilters = !showFilters" class="filters-toggle">
            <svg class="toggle-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4" />
            </svg>
            {{ showFilters ? 'Hide' : 'Show' }} Advanced Filters
          </button>

          <div v-if="showFilters" class="filters-panel">
            <div class="filters-grid">
              <div class="filter-group">
                <label class="filter-label">Category</label>
                <select v-model="filters.category" class="filter-select">
                  <option value="">All Categories</option>
                  <option value="Invoice">📄 Invoice</option>
                  <option value="Contract">📜 Contract</option>
                  <option value="Report">📊 Report</option>
                  <option value="Letter">✉️ Letter</option>
                  <option value="Memo">📝 Memo</option>
                  <option value="Presentation">📽️ Presentation</option>
                  <option value="Spreadsheet">📈 Spreadsheet</option>
                  <option value="Image">🖼️ Image</option>
                  <option value="Other">📎 Other</option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">File Type</label>
                <select v-model="filters.contentType" class="filter-select">
                  <option value="">All Types</option>
                  <option value="application/pdf">📄 PDF Documents</option>
                  <option value="application/vnd.openxmlformats-officedocument.wordprocessingml.document">📝 Word Documents</option>
                  <option value="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet">📊 Excel Spreadsheets</option>
                  <option value="text/plain">📃 Text Files</option>
                  <option value="image/jpeg">🖼️ Images (JPEG)</option>
                  <option value="image/png">🖼️ Images (PNG)</option>
                </select>
              </div>
              <div class="filter-group">
                <label class="filter-label">Date From</label>
                <input v-model="filters.dateFrom" type="date" class="filter-input" />
              </div>
              <div class="filter-group">
                <label class="filter-label">Date To</label>
                <input v-model="filters.dateTo" type="date" class="filter-input" />
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- Search Results -->
      <section v-if="searchResults && searchResults.documents.length > 0" class="results-section">
        <div class="results-summary">
          <div class="summary-card">
            <span class="summary-count">{{ searchResults.totalResults }}</span>
            <span class="summary-text"> results</span>
            <span class="summary-divider">•</span>
            <span class="summary-text">in </span>
            <span class="summary-time">{{ searchResults.searchTimeMs }}ms</span>
          </div>
          <div class="summary-page">Page {{ searchResults.page }} of {{ searchResults.totalPages }}</div>
        </div>

        <div class="documents-grid">
          <article v-for="doc in searchResults.documents" :key="doc.id" class="document-card">
            <div class="card-content">
              <div class="doc-info">
                <div class="doc-header">
                  <div class="file-icon-wrapper">
                    <div class="file-icon">
                      <span class="icon-emoji">{{ getFileIcon(doc.contentType) }}</span>
                    </div>
                  </div>
                  <div class="doc-details">
                    <h3 class="doc-title">{{ doc.title }}</h3>
                    <p v-if="doc.description" class="doc-description">{{ doc.description }}</p>
                    <div v-if="doc.highlights && doc.highlights.length" class="highlights">
                      <div v-for="(highlight, idx) in doc.highlights.slice(0, 2)" :key="idx" class="highlight-item" v-html="highlight"></div>
                    </div>
                    <div class="metadata-row">
                      <span class="meta-item">
                        <svg class="meta-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                        </svg>
                        {{ doc.fileName }}
                      </span>
                      <span v-if="doc.documentDate" class="meta-item meta-highlight" title="Document Date">
                        📅 {{ formatDate(doc.documentDate) }}
                      </span>
                      <span class="meta-item">
                        <svg class="meta-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                        </svg>
                        <span v-if="doc.documentDate">↑ </span>{{ formatDate(doc.createdAt) }}
                      </span>
                      <span class="meta-item">
                        {{ formatFileSize(doc.fileSize) }}
                      </span>
                      <span v-if="doc.category" class="category-badge">{{ doc.category }}</span>
                    </div>
                    <div v-if="doc.tags && doc.tags.length" class="tags-row">
                      <span v-for="tag in doc.tags.slice(0, 5)" :key="tag" class="tag">#{{ tag }}</span>
                      <span v-if="doc.tags.length > 5" class="tag-more">+{{ doc.tags.length - 5 }} more</span>
                    </div>
                  </div>
                </div>
              </div>
              <div class="doc-actions">
                <div class="score-wrapper">
                  <div class="score-circle">
                    <svg class="circle-svg" viewBox="0 0 100 100">
                      <circle cx="50" cy="50" r="40" class="circle-bg" />
                      <circle cx="50" cy="50" r="40" class="circle-progress" :style="`stroke-dashoffset: ${251 - (251 * doc.score)}`" />
                    </svg>
                    <div class="score-text">
                      <span class="score-value">{{ Math.round(doc.score * 100) }}%</span>
                    </div>
                  </div>
                  <p class="score-label">Relevance</p>
                </div>
                <button @click="viewDocument(doc)" class="view-btn">
                  <svg class="btn-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                  </svg>
                  View
                </button>
              </div>
            </div>
          </article>
        </div>

        <nav v-if="searchResults.totalPages > 1" class="pagination">
          <button v-for="page in paginationPages" :key="page" @click="goToPage(page)" :class="['page-btn', { active: page === searchResults.page }]">
            {{ page }}
          </button>
        </nav>
      </section>

      <section v-else-if="!loading && searched" class="empty-state">
        <div class="empty-icon">
          <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        </div>
        <h3 class="empty-title">No documents found</h3>
        <p class="empty-text">Try adjusting your search terms or filters</p>
        <button @click="searchQuery = ''; searchResults = null; searched = false" class="clear-btn">Clear Search</button>
      </section>

      <section v-else-if="!searched" class="initial-state">
        <div class="initial-icon">
          <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
          </svg>
        </div>
        <h3 class="initial-title">Start searching your documents</h3>
        <p class="initial-text">Use natural language to find what you need</p>
      </section>
    </main>

    <!-- ═══════════════════════════════════════════════════════════════════════
         ENHANCED DOCUMENT VIEWER MODAL
         Split-panel: left = file preview, right = details + suggestions
    ═══════════════════════════════════════════════════════════════════════ -->
    <div v-if="showDocumentViewer" class="modal-overlay" @click.self="closeDocumentViewer">
      <div class="viewer-modal">

        <!-- ── Header ── -->
        <div class="viewer-header">
          <div class="viewer-header-left">
            <div class="viewer-file-badge">{{ getFileExtension(currentDocument?.fileName) }}</div>
            <div class="viewer-title-block">
              <h2 class="viewer-title">{{ currentDocument?.title }}</h2>
              <p class="viewer-filename">
                <span class="vf-icon">{{ getFileIcon(currentDocument?.contentType) }}</span>
                {{ currentDocument?.fileName }}
                <span class="vf-sep">·</span>
                {{ formatFileSize(currentDocument?.fileSize) }}
                <span v-if="currentDocument?.category" class="vf-sep">·</span>
                <span v-if="currentDocument?.category" class="vf-cat">{{ currentDocument.category }}</span>
              </p>
            </div>
          </div>
          <div class="viewer-header-actions">
            <!-- Tab switcher for narrow screens -->
            <div class="tab-switcher">
              <button :class="['tab-btn', { active: activeTab === 'preview' }]" @click="activeTab = 'preview'">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"/><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"/></svg>
                Preview
              </button>
              <button :class="['tab-btn', { active: activeTab === 'details' }]" @click="activeTab = 'details'">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
                Details
              </button>
            </div>

            <button @click="closeDocumentViewer" class="hdr-btn hdr-close">
              <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/></svg>
            </button>
          </div>
        </div>

        <!-- ── Body: two-column split ── -->
        <div class="viewer-body">

          <!-- LEFT: File Preview -->
          <div class="viewer-preview-pane" :class="{ 'tab-hidden': activeTab !== 'preview' }">
            <div v-if="documentLoading" class="preview-loading">
              <div class="pulse-ring"></div>
              <svg class="spinner xl" fill="none" viewBox="0 0 24 24">
                <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
              </svg>
              <p class="preview-loading-text">Loading preview…</p>
            </div>

            <!-- PDF -->
            <div v-else-if="isPDF(currentDocument?.contentType)" class="pdf-viewer">
              <iframe
                v-if="documentUrl"
                :src="documentUrl + '#toolbar=1&navpanes=0&zoom=page-fit'"
                class="pdf-frame"
                title="PDF Viewer"
              ></iframe>
              <div v-else class="preview-loading">
                <div class="pulse-ring"></div>
                <p class="preview-loading-text">Preparing PDF…</p>
              </div>
            </div>

            <!-- Image -->
            <div v-else-if="isImage(currentDocument?.contentType)" class="image-viewer">
              <div class="image-viewer-inner">
                <img :src="documentUrl" :alt="currentDocument?.title" class="document-image" @load="imageLoaded = true" />
                <div class="image-zoom-hint">
                  <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0zM10 7v3m0 0v3m0-3h3m-3 0H7"/></svg>
                  Click image to open full size
                </div>
                <a :href="documentUrl" target="_blank" class="image-fullscreen-link">
                  <img :src="documentUrl" :alt="currentDocument?.title" class="document-image" />
                </a>
              </div>
            </div>

            <!-- Plain Text -->
            <div v-else-if="isText(currentDocument?.contentType)" class="text-viewer">
              <div class="text-toolbar">
                <span class="text-lines">{{ documentContent?.split('\n').length }} lines</span>
                <span class="text-chars">{{ documentContent?.length?.toLocaleString() }} chars</span>
              </div>
              <pre class="text-content">{{ documentContent }}</pre>
            </div>

            <!-- Word / Excel: extracted text viewer + Office embed option -->
            <div v-else-if="isOffice(currentDocument?.contentType)" class="office-viewer">
              <div class="office-tabs">
                <button :class="['otab', { active: officeViewMode === 'text' }]" @click="officeViewMode = 'text'">
                  📄 Extracted Text
                </button>
                <button :class="['otab', { active: officeViewMode === 'embed' }]" @click="officeViewMode = 'embed'">
                  🌐 Office Online
                </button>
              </div>

              <!-- Extracted text -->
              <div v-if="officeViewMode === 'text'" class="office-text-panel">
                <div v-if="documentContent" class="office-text-wrap">
                  <div class="office-text-stats">
                    <span>{{ documentContent.split(/\s+/).filter(Boolean).length.toLocaleString() }} words</span>
                    <span>{{ documentContent.split('\n').length }} paragraphs</span>
                  </div>
                  <pre class="office-text-content">{{ documentContent }}</pre>
                </div>
                <div v-else class="office-no-text">
                  <div class="ont-icon">{{ getFileIcon(currentDocument?.contentType) }}</div>
                  <p class="ont-title">No extracted text available</p>
                  <p class="ont-sub">Text extraction may still be processing, or this file has no selectable text.</p>
                  <button @click="downloadDocument(currentDocument.id)" class="ont-download-btn">
                    <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"/></svg>
                    Download to open locally
                  </button>
                </div>
              </div>

              <!-- Office Online embed (requires public URL) -->
              <div v-if="officeViewMode === 'embed'" class="office-embed-panel">
                <div class="office-embed-notice">
                  <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
                  Office Online viewer requires a publicly accessible file URL.
                  <a :href="`https://view.officeapps.live.com/op/view.aspx?src=${encodeURIComponent(officePublicUrl || '')}`" target="_blank" class="office-embed-link">
                    Open in Office Online ↗
                  </a>
                </div>
              </div>
            </div>

            <!-- Audio -->
            <div v-else-if="isAudio(currentDocument?.contentType)" class="audio-viewer">
              <div class="audio-art">
                <div class="audio-wave">
                  <span v-for="i in 20" :key="i" class="wave-bar" :style="`animation-delay: ${i * 0.07}s; height: ${20 + Math.random() * 60}%`"></span>
                </div>
                <div class="audio-file-icon">🎵</div>
                <p class="audio-filename">{{ currentDocument?.fileName }}</p>
              </div>
              <audio :src="documentUrl" controls class="audio-player">
                Your browser does not support audio playback.
              </audio>
            </div>

            <!-- Video -->
            <div v-else-if="isVideo(currentDocument?.contentType)" class="video-viewer">
              <video :src="documentUrl" controls class="video-player">
                Your browser does not support video playback.
              </video>
            </div>

            <!-- Unsupported / fallback -->
            <div v-else class="unsupported-viewer">
              <div class="unsupported-icon-wrap">
                <span class="unsupported-emoji">{{ getFileIcon(currentDocument?.contentType) }}</span>
              </div>
              <h3 class="unsupported-title">Preview not available</h3>
              <p class="unsupported-text">This file type cannot be rendered in the browser.</p>
              <code class="unsupported-mime">{{ currentDocument?.contentType }}</code>
              <button @click="downloadDocument(currentDocument.id)" class="download-instead-btn">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"/></svg>
                Download File
              </button>
            </div>
          </div>

          <!-- RIGHT: Details Sidebar -->
          <div class="viewer-details-pane" :class="{ 'tab-hidden': activeTab !== 'details' }">

            <!-- OCR / Processing status -->
            <div v-if="ocrStatus" class="ocr-status-bar" :class="ocrStatus.status === 2 ? 'ocr-done' : ocrStatus.status === 3 ? 'ocr-fail' : 'ocr-pending'">
              <span class="ocr-dot"></span>
              <span v-if="ocrStatus.status === 2">OCR Complete · {{ ocrStatus.extractedText?.length?.toLocaleString() }} chars extracted</span>
              <span v-else-if="ocrStatus.status === 3">OCR Failed: {{ ocrStatus.errorMessage }}</span>
              <span v-else>OCR Processing…</span>
            </div>

            <!-- ── Document Info ── -->
            <div class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
                Document Info
              </h3>
              <dl class="detail-list">
                <div class="dl-row">
                  <dt>File name</dt>
                  <dd class="dd-mono">{{ currentDocument?.fileName }}</dd>
                </div>
                <div class="dl-row">
                  <dt>Type</dt>
                  <dd>
                    <span class="mime-badge">{{ getFileExtension(currentDocument?.fileName).toUpperCase() }}</span>
                    <span class="mime-text">{{ currentDocument?.contentType }}</span>
                  </dd>
                </div>
                <div class="dl-row">
                  <dt>Size</dt>
                  <dd>{{ formatFileSize(currentDocument?.fileSize) }}</dd>
                </div>
                <div class="dl-row" v-if="currentDocument?.category">
                  <dt>Category</dt>
                  <dd><span class="cat-pill">{{ currentDocument.category }}</span></dd>
                </div>
                <div class="dl-row" v-if="currentDocument?.documentDate">
                  <dt>Document date</dt>
                  <dd class="dd-accent">📅 {{ formatDate(currentDocument.documentDate) }}</dd>
                </div>
                <div class="dl-row">
                  <dt>Uploaded</dt>
                  <dd>{{ formatDateLong(currentDocument?.createdAt) }}</dd>
                </div>
                <div class="dl-row" v-if="currentDocument?.modifiedAt">
                  <dt>Last modified</dt>
                  <dd>{{ formatDateLong(currentDocument.modifiedAt) }}</dd>
                </div>
                <div class="dl-row" v-if="currentDocument?.score !== undefined">
                  <dt>Relevance</dt>
                  <dd>
                    <div class="relevance-bar-wrap">
                      <div class="relevance-bar">
                        <div class="relevance-fill" :style="`width: ${Math.round((currentDocument.score || 0) * 100)}%`"></div>
                      </div>
                      <span class="relevance-pct">{{ Math.round((currentDocument.score || 0) * 100) }}%</span>
                    </div>
                  </dd>
                </div>
              </dl>
            </div>

            <!-- ── Tags ── -->
            <div v-if="currentDocument?.tags && currentDocument.tags.length" class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"/></svg>
                Tags
              </h3>
              <div class="tags-cloud">
                <button
                  v-for="tag in currentDocument.tags"
                  :key="tag"
                  @click="searchByTag(tag)"
                  class="tag-cloud-item"
                  :title="`Search for '${tag}'`"
                >
                  #{{ tag }}
                </button>
              </div>
            </div>

            <!-- ── Description / Highlights ── -->
            <div v-if="currentDocument?.description || (currentDocument?.highlights && currentDocument.highlights.length)" class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/></svg>
                Excerpt
              </h3>
              <p v-if="currentDocument.description" class="detail-desc">{{ currentDocument.description }}</p>
              <div v-if="currentDocument.highlights?.length" class="detail-highlights">
                <div v-for="(h, i) in currentDocument.highlights" :key="i" class="detail-highlight-item" v-html="h"></div>
              </div>
            </div>

            <!-- ── Similar Documents ── -->
            <div class="detail-section">
              <h3 class="detail-section-title">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10"/></svg>
                Similar Documents
              </h3>

              <div v-if="suggestionsLoading" class="suggestions-loading">
                <svg class="spinner sm" fill="none" viewBox="0 0 24 24">
                  <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
                </svg>
                Finding similar documents…
              </div>

              <div v-else-if="suggestions.length === 0" class="suggestions-empty">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
                No similar documents found
              </div>

              <div v-else class="suggestions-list">
                <div
                  v-for="sug in suggestions"
                  :key="sug.documentId"
                  class="suggestion-card"
                  @click="openSuggestion(sug)"
                >
                  <div class="sug-icon">{{ getFileIconById(sug) }}</div>
                  <div class="sug-info">
                    <p class="sug-title">{{ sug.title }}</p>
                    <p class="sug-reason">{{ sug.reason }}</p>
                  </div>
                  <div class="sug-score">
                    <div class="sug-score-ring" :style="`--pct: ${Math.round(sug.similarityScore * 100)}`">
                      <span>{{ Math.round(sug.similarityScore * 100) }}%</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>

          </div><!-- /details pane -->
        </div><!-- /viewer-body -->
      </div><!-- /viewer-modal -->
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════════
         UPLOAD MODAL
    ═══════════════════════════════════════════════════════════════════════ -->
    <div v-if="showUploadModal" class="modal-overlay" @click.self="closeUploadModal">
      <div class="modal-content">
        <div class="modal-header">
          <h2 class="modal-title">Upload Document</h2>
          <button @click="closeUploadModal" class="modal-close">
            <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
        <div class="modal-body">
          <div class="upload-area" :class="{ 'has-file': selectedFile }">
            <input ref="fileInput" type="file" @change="handleFileSelect" class="file-input"
              accept=".pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png,.tiff,.txt,.mp3,.mp4,.wav,.ogg" />
            <div v-if="!selectedFile" @click="$refs.fileInput.click()" class="upload-prompt">
              <svg class="upload-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
              </svg>
              <p class="upload-text">Click to upload or drag and drop</p>
              <p class="upload-hint">PDF, Word, Excel, Images, Text, Audio, Video (max 100MB)</p>
            </div>
            <div v-else class="file-preview">
              <span class="file-emoji">{{ getFileIcon(selectedFile.type) }}</span>
              <div class="file-info">
                <p class="file-name">{{ selectedFile.name }}</p>
                <p class="file-size">{{ formatFileSize(selectedFile.size) }}</p>
              </div>
              <button @click="clearFile" class="remove-file">
                <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">Title</label>
            <input v-model="uploadData.title" type="text" placeholder="Enter document title" class="form-input" />
          </div>
          <div class="form-group">
            <label class="form-label">Category <span class="required-indicator">*</span></label>
            <select v-model="uploadData.category" class="filter-select" :class="{ 'error': !uploadData.category && selectedFile }" required>
              <option value="">Select a category (required)</option>
              <option value="Invoice">📄 Invoice</option>
              <option value="Contract">📜 Contract</option>
              <option value="Report">📊 Report</option>
              <option value="Letter">✉️ Letter</option>
              <option value="Memo">📝 Memo</option>
              <option value="Presentation">📽️ Presentation</option>
              <option value="Spreadsheet">📈 Spreadsheet</option>
              <option value="Image">🖼️ Image</option>
              <option value="Other">📎 Other</option>
            </select>
            <p v-if="!uploadData.category && selectedFile" class="error-message">Category is required</p>
          </div>
          <div class="modal-actions">
            <button @click="uploadDocument" :disabled="!selectedFile || uploading || !uploadData.category" class="upload-submit" :class="{ 'disabled': !selectedFile || uploading || !uploadData.category }">
              <span v-if="!uploading">Upload</span>
              <span v-else class="loading-text">
                <svg class="spinner" fill="none" viewBox="0 0 24 24">
                  <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
                </svg>
                Uploading...
              </span>
            </button>
            <button @click="closeUploadModal" class="cancel-btn">Cancel</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { format } from 'date-fns'

// ── State ─────────────────────────────────────────────────────────────────────
const searchQuery    = ref('')
const showFilters    = ref(false)
const loading        = ref(false)
const searched       = ref(false)
const searchResults  = ref(null)
const showUploadModal = ref(false)
const selectedFile   = ref(null)
const uploading      = ref(false)

// Viewer state
const showDocumentViewer = ref(false)
const currentDocument    = ref(null)
const documentUrl        = ref(null)
const documentContent    = ref(null)
const documentLoading    = ref(false)
const imageLoaded        = ref(false)
const activeTab          = ref('preview')      // 'preview' | 'details'
const officeViewMode     = ref('text')          // 'text' | 'embed'
const officePublicUrl    = ref('')
const ocrStatus          = ref(null)
const suggestions        = ref([])
const suggestionsLoading = ref(false)
const suggestionsCache   = new Map()           // cache by documentId

const filters = reactive({ category: '', contentType: '', dateFrom: '', dateTo: '' })
const uploadData = reactive({ title: '', category: '' })

const quickSearches = ['all documents', 'invoices', 'PDFs from last month', 'contracts from 2024']

// ── Computed ──────────────────────────────────────────────────────────────────
const paginationPages = computed(() => {
  if (!searchResults.value) return []
  const total = searchResults.value.totalPages
  const current = searchResults.value.page
  const pages = []
  const start = Math.max(1, current - 4)
  const end = Math.min(total, start + 9)
  for (let i = start; i <= end; i++) pages.push(i)
  return pages
})

// ── Type helpers ──────────────────────────────────────────────────────────────
const isPDF   = (t) => t === 'application/pdf'
const isImage = (t) => !!t?.startsWith('image/')
const isText  = (t) => t === 'text/plain'
const isAudio = (t) => !!t?.startsWith('audio/')
const isVideo = (t) => !!t?.startsWith('video/')
const isOffice = (t) => [
  'application/msword',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.ms-excel',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.ms-powerpoint',
  'application/vnd.openxmlformats-officedocument.presentationml.presentation',
].includes(t)

const getFileIcon = (contentType) => {
  if (!contentType) return '📎'
  if (contentType.includes('pdf'))   return '📄'
  if (contentType.includes('word') || contentType.includes('document')) return '📝'
  if (contentType.includes('sheet') || contentType.includes('excel'))   return '📊'
  if (contentType.includes('presentation') || contentType.includes('powerpoint')) return '📽️'
  if (contentType.includes('image')) return '🖼️'
  if (contentType.includes('text'))  return '📃'
  if (contentType.includes('audio')) return '🎵'
  if (contentType.includes('video')) return '🎬'
  return '📎'
}

const getFileIconById = (sug) => '📄'   // suggestions don't carry contentType — default

const getFileExtension = (fileName) => {
  if (!fileName) return ''
  const ext = fileName.split('.').pop()
  return ext ? ext.toLowerCase() : ''
}

// ── Format helpers ────────────────────────────────────────────────────────────
const formatDate = (d) => {
  try { return format(new Date(d), 'MMM d, yyyy') } catch { return d }
}
const formatDateLong = (d) => {
  try { return format(new Date(d), 'MMM d, yyyy · HH:mm') } catch { return d }
}
const formatFileSize = (bytes) => {
  if (!bytes) return '—'
  if (bytes < 1024)        return bytes + ' B'
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / (1024 * 1024)).toFixed(1) + ' MB'
}

// ── Auth helper — adds JWT Bearer header to every API call ───────────────────
const authHeaders = () => {
  const token = localStorage.getItem('ged_token')
  return token ? { Authorization: `Bearer ${token}` } : {}
}

// Keep track of blob URLs so we can revoke them when the viewer closes
let _currentBlobUrl = null
const revokeBlobUrl = () => {
  if (_currentBlobUrl) { URL.revokeObjectURL(_currentBlobUrl); _currentBlobUrl = null }
}

/**
 * Fetch a file from the API with auth headers and return a blob: URL.
 *
 * Why blob URLs instead of direct fetch URLs?
 * 1. iframe/img/audio/video elements send NO custom headers — auth fails
 * 2. Content-Disposition: attachment prevents iframe display
 * Wrapping bytes in a blob: URL bypasses both issues entirely.
 *
 * @param {string} apiPath
 * @param {string} [forceMime]  Override the MIME type (e.g. 'application/pdf')
 */
const fetchBlobUrl = async (apiPath, forceMime) => {
  const res  = await fetch(apiPath, { headers: authHeaders() })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  const buf  = await res.arrayBuffer()
  // Force the MIME type so the browser knows how to render the blob
  const mime = forceMime || res.headers.get('content-type')?.split(';')[0].trim() || 'application/octet-stream'
  const blob = new Blob([buf], { type: mime })
  const url  = URL.createObjectURL(blob)
  _currentBlobUrl = url
  return url
}

// ── Document Viewer ───────────────────────────────────────────────────────────
const viewDocument = async (doc) => {
  revokeBlobUrl()                          // clean up any previous blob URL
  currentDocument.value    = doc
  showDocumentViewer.value = true
  documentLoading.value    = true
  documentContent.value    = null
  documentUrl.value        = null
  ocrStatus.value          = null
  suggestions.value        = []
  activeTab.value          = 'preview'
  officeViewMode.value     = 'text'
  imageLoaded.value        = false

  const downloadPath = `/api/documents/${doc.id}/download`
  const ocrPath      = `/api/documents/${doc.id}/ocr-status`

  try {
    // ── PDF, Image, Audio, Video → authenticated blob URL ──────────────────
    // We MUST fetch with auth headers first and turn the response into a
    // blob: URL; otherwise the browser's iframe/img/audio/video element
    // will request the URL without the Authorization header and get a 401.
    if (isPDF(doc.contentType) || isImage(doc.contentType) ||
        isAudio(doc.contentType) || isVideo(doc.contentType)) {
      try {
        // Force the correct MIME so the browser renders inline (not downloads)
        const forcedMime = doc.contentType || undefined
        documentUrl.value = await fetchBlobUrl(downloadPath, forcedMime)
      } catch (e) {
        console.warn('Blob fetch failed, falling back to direct URL:', e)
        documentUrl.value = downloadPath   // fallback (works when auth is disabled)
      }
      documentLoading.value = false

    // ── Plain text → fetch text with auth ──────────────────────────────────
    } else if (isText(doc.contentType)) {
      try {
        const res = await fetch(downloadPath, { headers: authHeaders() })
        documentContent.value = res.ok ? await res.text() : '(could not load file)'
      } catch { documentContent.value = '(could not load file)' }
      documentLoading.value = false

    // ── Office docs → show extracted text pulled from OCR endpoint ─────────
    } else if (isOffice(doc.contentType)) {
      // Pre-fill with any highlights / description we already have
      documentContent.value = doc.highlights?.join('\n\n') || doc.description || null

      try {
        const ocrRes = await fetch(ocrPath, { headers: authHeaders() })
        if (ocrRes.ok) {
          const ocr = await ocrRes.json()
          ocrStatus.value = ocr
          if (ocr.extractedText) documentContent.value = ocr.extractedText
        }
      } catch { /* non-fatal */ }

      documentLoading.value = false

    } else {
      documentLoading.value = false
    }

    // ── OCR status for images & PDFs (background, non-blocking) ───────────
    if ((isPDF(doc.contentType) || isImage(doc.contentType)) && !ocrStatus.value) {
      fetch(ocrPath, { headers: authHeaders() })
        .then(r => r.ok ? r.json() : null)
        .then(data => { if (data) ocrStatus.value = data })
        .catch(() => { /* non-fatal */ })
    }

    // ── Similar document suggestions ───────────────────────────────────────
    fetchSuggestions(doc.id)

  } catch (err) {
    console.error('Viewer error:', err)
    documentLoading.value = false
  }
}

const fetchSuggestions = async (docId) => {
  if (suggestionsCache.has(docId)) {
    suggestions.value = suggestionsCache.get(docId)
    return
  }
  suggestionsLoading.value = true
  try {
    const res = await fetch(`/api/search/suggestions/${docId}?count=5`, { headers: authHeaders() })
    if (res.ok) {
      const data = await res.json()
      suggestions.value = data
      suggestionsCache.set(docId, data)
    }
  } catch { /* non-fatal */ } finally {
    suggestionsLoading.value = false
  }
}

const openSuggestion = (sug) => {
  // Navigate to the suggestion: open it in viewer if we have enough data,
  // otherwise use its ID to fetch details then open
  const syntheticDoc = {
    id:          sug.documentId,
    title:       sug.title,
    fileName:    sug.title,
    contentType: 'application/pdf', // best guess; real type will come from server
    score:       sug.similarityScore,
  }
  closeDocumentViewer()
  setTimeout(() => viewDocument(syntheticDoc), 150)
}

const closeDocumentViewer = () => {
  revokeBlobUrl()                        // free blob memory
  showDocumentViewer.value = false
  currentDocument.value    = null
  documentUrl.value        = null
  documentContent.value    = null
  documentLoading.value    = false
  ocrStatus.value          = null
  suggestions.value        = []
}

const downloadDocument = (id) => window.open(`/api/documents/${id}/download`, '_blank')

const searchByTag = (tag) => {
  closeDocumentViewer()
  searchQuery.value = tag
  performSearch()
}

// ── Search ────────────────────────────────────────────────────────────────────
const mapFileTypeToContentType = (fileType) => ({
  pdf: 'application/pdf', doc: 'application/msword',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  xls: 'application/vnd.ms-excel',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', txt: 'text/plain'
}[fileType?.toLowerCase()] || null)

const buildSearchBody = async (page = 1) => {
  let nlpFilters = null
  try {
    const r = await fetch('/api/search/nlp/understand', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(searchQuery.value)
    })
    if (r.ok) nlpFilters = (await r.json()).extractedFilters
  } catch { /* non-fatal */ }

  const contentTypeFilter = nlpFilters?.filetype
    ? mapFileTypeToContentType(nlpFilters.filetype)
    : filters.contentType || null

  return {
    query: searchQuery.value, searchType: 0, page, pageSize: 20,
    categories: filters.category ? [filters.category] : null,
    contentTypes: contentTypeFilter ? [contentTypeFilter] : null,
    fromDate: nlpFilters?.fromDate || filters.dateFrom || null,
    toDate:   nlpFilters?.toDate   || filters.dateTo   || null,
    includeOcrContent: true
  }
}

const performSearch = async () => {
  if (!searchQuery.value.trim()) return
  loading.value = true; searched.value = true
  try {
    const body = await buildSearchBody(1)
    const res  = await fetch('/api/search/query', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
    if (res.ok) searchResults.value = await res.json()
    else alert(`Search failed: ${res.status}`)
  } catch { alert('Search error. Make sure the backend is running.') } finally { loading.value = false }
}

const goToPage = async (page) => {
  if (!searchQuery.value.trim()) return
  loading.value = true
  try {
    const body = await buildSearchBody(page)
    const res  = await fetch('/api/search/query', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
    if (res.ok) { searchResults.value = await res.json(); window.scrollTo({ top: 0, behavior: 'smooth' }) }
  } catch { /* silent */ } finally { loading.value = false }
}

// ── Upload ────────────────────────────────────────────────────────────────────
const handleFileSelect = (e) => {
  const file = e.target.files[0]
  if (!file) return
  selectedFile.value  = file
  uploadData.title    = file.name.replace(/\.[^/.]+$/, '')
  uploadData.category = ''
}

const uploadDocument = async () => {
  if (!selectedFile.value || !uploadData.category) return
  uploading.value = true
  try {
    const form = new FormData()
    form.append('file', selectedFile.value)
    form.append('title', uploadData.title || selectedFile.value.name)
    form.append('category', uploadData.category)
    const res = await fetch('/api/documents/upload', { method: 'POST', body: form })
    if (res.ok) {
      const result = await res.json()
      closeUploadModal()
      const needsOcr = result.contentType?.startsWith('image/') || result.contentType === 'application/pdf'
      if (needsOcr) {
        let pollCount = 0
        const poll = setInterval(async () => {
          pollCount++
          try {
            const sr = await fetch(`/api/documents/${result.id}/ocr-status`)
            if (!sr.ok) { if (pollCount >= 20) clearInterval(poll); return }
            const job = await sr.json()
            if (job.status === 2 || job.status === 3 || pollCount >= 20) {
              clearInterval(poll)
              alert(job.status === 2 ? `Document uploaded! OCR complete — ${job.extractedText?.length || 0} chars extracted.` : 'Document uploaded! (OCR processing in background)')
              if (searchResults.value) performSearch()
            }
          } catch { /* silent */ }
        }, 3000)
      } else {
        alert('Document uploaded and indexed!')
        if (searchResults.value) performSearch()
      }
    } else {
      const err = await res.json()
      alert(`Upload failed: ${err.error || 'Unknown error'}`)
    }
  } catch { alert('Upload error.') } finally { uploading.value = false }
}

const clearFile = () => {
  selectedFile.value = null; uploadData.title = ''; uploadData.category = ''
  const inp = document.querySelector('.file-input')
  if (inp) inp.value = ''
}
const closeUploadModal = () => { showUploadModal.value = false; clearFile() }
</script>

<style scoped>
/* ── Base reset ─────────────────────────────────────────────────────────────── */
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

.app-container {
  min-height: 100vh;
  background: linear-gradient(135deg, #f8fafc 0%, #e0f2fe 50%, #ddd6fe 100%);
}

/* ── Header ─────────────────────────────────────────────────────────────────── */
.app-header {
  background: rgba(255,255,255,0.85);
  backdrop-filter: blur(14px);
  border-bottom: 1px solid #e5e7eb;
  position: sticky; top: 0; z-index: 50;
}
.header-content {
  max-width: 1280px; margin: 0 auto; padding: 0 1rem;
  display: flex; align-items: center; justify-content: space-between; height: 72px;
}
.header-left  { display: flex; align-items: center; gap: 1rem; }
.logo-icon {
  width: 44px; height: 44px;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  border-radius: 11px; display: flex; align-items: center; justify-content: center;
}
.logo-icon .icon { width: 26px; height: 26px; color: white; }
.header-title  { font-size: 1.4rem; font-weight: 700; background: linear-gradient(135deg,#2563eb,#4f46e5); -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text; }
.header-subtitle { font-size: 0.8rem; color: #6b7280; }
.upload-btn {
  display: inline-flex; align-items: center; gap: 0.5rem;
  padding: 0.6rem 1.25rem;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  color: white; border: none; border-radius: 10px; font-weight: 600; cursor: pointer; transition: all 0.2s;
}
.upload-btn:hover { box-shadow: 0 8px 16px rgba(37,99,235,0.35); transform: translateY(-1px); }
.btn-icon { width: 18px; height: 18px; }

/* ── Main / Search ──────────────────────────────────────────────────────────── */
.main-content { max-width: 1280px; margin: 0 auto; padding: 2rem 1rem; }
.search-section { margin-bottom: 2rem; }
.search-card {
  background: white; border-radius: 16px;
  box-shadow: 0 8px 24px rgba(0,0,0,0.07); padding: 1.75rem; border: 1px solid #f0f0f0;
}
.search-bar-wrapper { display: flex; gap: 0.75rem; flex-wrap: wrap; }
.search-input-wrapper { flex: 1; min-width: 200px; position: relative; display: flex; align-items: center; }
.search-icon { position: absolute; left: 1rem; width: 22px; height: 22px; color: #9ca3af; pointer-events: none; }
.search-input { width: 100%; padding: 0.9rem 1rem 0.9rem 3rem; font-size: 1rem; border: 2px solid #e5e7eb; border-radius: 10px; outline: none; transition: all 0.2s; }
.search-input:focus { border-color: #3b82f6; box-shadow: 0 0 0 3px rgba(59,130,246,0.1); }
.search-btn { padding: 0.9rem 2rem; background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; border: none; border-radius: 10px; font-weight: 600; cursor: pointer; transition: all 0.2s; white-space: nowrap; }
.search-btn:hover:not(:disabled) { box-shadow: 0 8px 16px rgba(37,99,235,0.3); transform: translateY(-1px); }
.search-btn:disabled { opacity: 0.5; cursor: not-allowed; }
.loading-text { display: flex; align-items: center; gap: 0.5rem; }
.quick-searches { margin-top: 0.875rem; display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
.quick-label { font-size: 0.8rem; color: #6b7280; }
.quick-btn { padding: 0.3rem 0.7rem; font-size: 0.8rem; background: #f3f4f6; color: #374151; border: none; border-radius: 7px; cursor: pointer; transition: all 0.15s; }
.quick-btn:hover { background: #dbeafe; color: #1d4ed8; }
.filters-toggle { margin-top: 0.875rem; display: inline-flex; align-items: center; gap: 0.25rem; color: #2563eb; background: none; border: none; font-size: 0.8rem; font-weight: 500; cursor: pointer; }
.toggle-icon { width: 14px; height: 14px; }
.filters-panel { margin-top: 1.25rem; padding-top: 1.25rem; border-top: 1px solid #f0f0f0; }
.filters-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 1rem; }
.filter-group { display: flex; flex-direction: column; }
.filter-label { font-size: 0.8rem; font-weight: 600; color: #374151; margin-bottom: 0.4rem; }
.filter-select, .filter-input { width: 100%; padding: 0.55rem 0.875rem; border: 2px solid #e5e7eb; border-radius: 8px; outline: none; font-size: 0.875rem; transition: all 0.2s; }
.filter-select:focus, .filter-input:focus { border-color: #3b82f6; }

/* ── Results ────────────────────────────────────────────────────────────────── */
.results-summary { display: flex; align-items: center; justify-content: space-between; margin-bottom: 1.25rem; flex-wrap: wrap; gap: 0.75rem; }
.summary-card { background: white; border-radius: 10px; box-shadow: 0 1px 4px rgba(0,0,0,0.07); padding: 0.6rem 1.25rem; font-size: 0.875rem; }
.summary-count { font-weight: 700; color: #111827; }
.summary-text  { color: #6b7280; }
.summary-divider { color: #d1d5db; margin: 0 0.4rem; }
.summary-time  { font-weight: 600; color: #2563eb; }
.summary-page  { font-size: 0.8rem; color: #6b7280; }
.documents-grid { display: grid; gap: 1rem; }
.document-card { background: white; border-radius: 14px; box-shadow: 0 2px 8px rgba(0,0,0,0.06); border: 1px solid #f0f0f0; overflow: hidden; transition: all 0.25s; }
.document-card:hover { box-shadow: 0 12px 28px rgba(0,0,0,0.1); transform: translateY(-2px); }
.card-content { padding: 1.25rem; display: flex; justify-content: space-between; gap: 1.25rem; flex-wrap: wrap; }
.doc-info { flex: 1; min-width: 0; }
.doc-header { display: flex; gap: 0.875rem; }
.file-icon-wrapper { flex-shrink: 0; }
.file-icon { width: 50px; height: 50px; border-radius: 11px; background: linear-gradient(135deg,#dbeafe,#e0e7ff); display: flex; align-items: center; justify-content: center; }
.icon-emoji { font-size: 1.75rem; }
.doc-details { flex: 1; min-width: 0; }
.doc-title { font-size: 1.1rem; font-weight: 700; color: #111827; margin-bottom: 0.35rem; word-break: break-word; transition: color 0.2s; }
.document-card:hover .doc-title { color: #2563eb; }
.doc-description { color: #6b7280; font-size: 0.875rem; margin-bottom: 0.75rem; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.highlights { display: flex; flex-direction: column; gap: 0.4rem; margin-bottom: 0.75rem; }
.highlight-item { font-size: 0.8rem; background: #fef9c3; border: 1px solid #fde68a; border-radius: 7px; padding: 0.5rem 0.75rem; font-style: italic; color: #374151; }
.metadata-row { display: flex; flex-wrap: wrap; gap: 0.6rem; font-size: 0.8rem; color: #6b7280; margin-bottom: 0.6rem; }
.meta-item { display: inline-flex; align-items: center; gap: 0.2rem; }
.meta-icon { width: 14px; height: 14px; flex-shrink: 0; }
.meta-highlight { background: linear-gradient(135deg,#fef3c7,#fde68a); padding: 0.2rem 0.45rem; border-radius: 5px; font-weight: 600; color: #78350f; }
.category-badge { padding: 0.2rem 0.625rem; background: linear-gradient(135deg,#dbeafe,#e0e7ff); color: #1d4ed8; border-radius: 9999px; font-weight: 500; }
.tags-row { display: flex; flex-wrap: wrap; gap: 0.4rem; }
.tag { padding: 0.2rem 0.55rem; background: #f3f4f6; color: #374151; font-size: 0.73rem; border-radius: 6px; }
.tag-more { padding: 0.2rem 0.55rem; color: #9ca3af; font-size: 0.73rem; }
.doc-actions { display: flex; flex-direction: column; align-items: center; gap: 0.75rem; }
.score-wrapper { text-align: center; }
.score-circle { position: relative; width: 72px; height: 72px; }
.circle-svg { width: 100%; height: 100%; transform: rotate(-90deg); }
.circle-bg { fill: none; stroke: #e5e7eb; stroke-width: 8; }
.circle-progress { fill: none; stroke: #2563eb; stroke-width: 8; stroke-dasharray: 251; stroke-dashoffset: 251; transition: stroke-dashoffset 0.8s ease; stroke-linecap: round; }
.score-text { position: absolute; top: 50%; left: 50%; transform: translate(-50%,-50%); }
.score-value { font-size: 1.1rem; font-weight: 700; background: linear-gradient(135deg,#2563eb,#4f46e5); -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text; }
.score-label { font-size: 0.7rem; color: #9ca3af; margin-top: 0.2rem; }
.view-btn { display: inline-flex; align-items: center; gap: 0.3rem; padding: 0.55rem 1.25rem; background: linear-gradient(135deg,#059669,#10b981); color: white; border: none; border-radius: 10px; font-weight: 600; font-size: 0.875rem; cursor: pointer; transition: all 0.2s; white-space: nowrap; }
.view-btn:hover { box-shadow: 0 6px 14px rgba(5,150,105,0.3); transform: translateY(-1px); }
.pagination { display: flex; background: white; border-radius: 10px; box-shadow: 0 1px 4px rgba(0,0,0,0.07); padding: 0.4rem; gap: 0.2rem; margin: 1.5rem auto; justify-content: center; flex-wrap: wrap; }
.page-btn { padding: 0.45rem 0.875rem; border-radius: 7px; font-weight: 500; border: none; cursor: pointer; color: #374151; background: transparent; transition: all 0.15s; }
.page-btn:hover { background: #f3f4f6; }
.page-btn.active { background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; }
.empty-state, .initial-state { text-align: center; padding: 5rem 1rem; }
.empty-icon, .initial-icon { display: inline-flex; align-items: center; justify-content: center; width: 88px; height: 88px; border-radius: 50%; margin-bottom: 1.25rem; }
.empty-icon { background: linear-gradient(135deg,#f3f4f6,#e5e7eb); }
.initial-icon { background: linear-gradient(135deg,#dbeafe,#e0e7ff); }
.empty-icon .icon, .initial-icon .icon { width: 44px; height: 44px; color: #9ca3af; }
.initial-icon .icon { color: #2563eb; }
.empty-title, .initial-title { font-size: 1.4rem; font-weight: 700; color: #111827; margin-bottom: 0.4rem; }
.empty-text, .initial-text { color: #6b7280; margin-bottom: 1.25rem; }
.clear-btn { padding: 0.65rem 1.4rem; background: #f3f4f6; color: #374151; border: none; border-radius: 10px; font-weight: 500; cursor: pointer; transition: all 0.15s; }
.clear-btn:hover { background: #e5e7eb; }

/* ══════════════════════════════════════════════════════════════════════════════
   ENHANCED DOCUMENT VIEWER MODAL
══════════════════════════════════════════════════════════════════════════════ */
.modal-overlay {
  position: fixed; inset: 0; background: rgba(0,0,0,0.6); backdrop-filter: blur(6px);
  z-index: 200; display: flex; align-items: center; justify-content: center; padding: 1rem;
}

.viewer-modal {
  background: #fff; border-radius: 20px;
  box-shadow: 0 32px 64px -12px rgba(0,0,0,0.3);
  width: 96vw; max-width: 1400px; height: 91vh;
  display: flex; flex-direction: column; overflow: hidden;
}

/* ── Viewer Header ── */
.viewer-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 0.875rem 1.25rem;
  background: linear-gradient(to right, #f8fafc, #f0f9ff);
  border-bottom: 1px solid #e5e7eb;
  gap: 1rem; flex-shrink: 0;
}
.viewer-header-left { display: flex; align-items: center; gap: 0.875rem; min-width: 0; flex: 1; }
.viewer-file-badge {
  flex-shrink: 0; padding: 0.25rem 0.6rem;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  color: white; font-size: 0.65rem; font-weight: 800;
  border-radius: 6px; letter-spacing: 0.06em; text-transform: uppercase;
}
.viewer-title-block { min-width: 0; }
.viewer-title { font-size: 1.05rem; font-weight: 700; color: #111827; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.viewer-filename { font-size: 0.78rem; color: #6b7280; display: flex; align-items: center; gap: 0.3rem; flex-wrap: wrap; }
.vf-icon { font-size: 1rem; }
.vf-sep  { color: #d1d5db; }
.vf-cat  { background: #dbeafe; color: #1d4ed8; padding: 0.1rem 0.45rem; border-radius: 9999px; font-weight: 600; font-size: 0.7rem; }
.viewer-header-actions { display: flex; align-items: center; gap: 0.5rem; flex-shrink: 0; }
.tab-switcher { display: flex; background: #f3f4f6; border-radius: 9px; padding: 0.2rem; gap: 0.2rem; }
.tab-btn {
  display: flex; align-items: center; gap: 0.35rem;
  padding: 0.35rem 0.75rem; border: none; border-radius: 7px;
  font-size: 0.8rem; font-weight: 600; cursor: pointer;
  color: #6b7280; background: transparent; transition: all 0.15s;
}
.tab-btn svg { width: 14px; height: 14px; }
.tab-btn.active { background: white; color: #1d4ed8; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
.hdr-btn {
  display: flex; align-items: center; gap: 0.35rem;
  padding: 0.4rem 0.875rem; border: none; border-radius: 8px;
  font-size: 0.8rem; font-weight: 600; cursor: pointer; transition: all 0.15s;
}
.hdr-btn svg { width: 16px; height: 16px; }
.hdr-download { background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; }
.hdr-download:hover { box-shadow: 0 4px 12px rgba(37,99,235,0.4); }
.hdr-close { background: #f3f4f6; color: #6b7280; }
.hdr-close:hover { background: #fee2e2; color: #dc2626; }

/* ── Viewer Body: two-column ── */
.viewer-body {
  flex: 1; display: grid;
  grid-template-columns: 1fr 340px;
  overflow: hidden;
}

/* ── Left: Preview pane ── */
.viewer-preview-pane {
  border-right: 1px solid #e5e7eb;
  overflow: hidden; display: flex; flex-direction: column;
  background: #f9fafb;
}

/* Loading */
.preview-loading {
  flex: 1; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 1rem;
  position: relative;
}
.pulse-ring {
  position: absolute; width: 90px; height: 90px;
  border-radius: 50%; border: 3px solid #3b82f6;
  animation: pulse-ring 1.6s ease-out infinite;
}
@keyframes pulse-ring { 0% { transform: scale(0.85); opacity: 0.8; } 100% { transform: scale(1.4); opacity: 0; } }
.preview-loading-text { font-size: 0.9rem; color: #6b7280; }

/* PDF */
.pdf-viewer { flex: 1; display: flex; }
.pdf-frame  { flex: 1; width: 100%; border: none; }

/* Image */
.image-viewer { flex: 1; overflow: auto; display: flex; align-items: center; justify-content: center; padding: 1.5rem; }
.image-viewer-inner { position: relative; }
.image-viewer-inner .document-image:first-child { display: none; } /* hide the non-link one */
.image-fullscreen-link { display: block; }
.document-image { max-width: 100%; max-height: 70vh; border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,0.15); display: block; }
.image-zoom-hint { font-size: 0.75rem; color: #9ca3af; text-align: center; margin-top: 0.5rem; display: flex; align-items: center; justify-content: center; gap: 0.25rem; }
.image-zoom-hint svg { width: 14px; height: 14px; }

/* Text */
.text-viewer { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.text-toolbar { display: flex; gap: 1rem; padding: 0.5rem 1rem; background: white; border-bottom: 1px solid #f0f0f0; font-size: 0.78rem; color: #9ca3af; flex-shrink: 0; }
.text-content { flex: 1; overflow: auto; padding: 1.25rem; font-family: 'Fira Mono', 'Courier New', monospace; font-size: 0.8rem; line-height: 1.65; color: #1e293b; white-space: pre-wrap; word-break: break-word; background: #0f172a; color: #e2e8f0; }

/* Office */
.office-viewer { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.office-tabs { display: flex; gap: 0; padding: 0.75rem 1rem 0; background: white; flex-shrink: 0; }
.otab { padding: 0.45rem 1rem; border: 1px solid #e5e7eb; background: #f9fafb; font-size: 0.8rem; font-weight: 600; cursor: pointer; color: #6b7280; transition: all 0.15s; }
.otab:first-child { border-radius: 8px 0 0 8px; }
.otab:last-child  { border-radius: 0 8px 8px 0; border-left: none; }
.otab.active { background: white; color: #2563eb; border-color: #3b82f6; z-index: 1; }
.office-text-panel { flex: 1; overflow: hidden; display: flex; flex-direction: column; }
.office-text-stats { display: flex; gap: 1.25rem; padding: 0.5rem 1rem; background: #f8fafc; border-bottom: 1px solid #f0f0f0; font-size: 0.75rem; color: #9ca3af; flex-shrink: 0; }
.office-text-wrap { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.office-text-content { flex: 1; overflow: auto; padding: 1.25rem; font-size: 0.85rem; line-height: 1.7; color: #1e293b; background: white; white-space: pre-wrap; word-break: break-word; }
.office-no-text { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 0.75rem; padding: 2rem; text-align: center; }
.ont-icon { font-size: 4rem; }
.ont-title { font-size: 1.1rem; font-weight: 700; color: #374151; }
.ont-sub { font-size: 0.85rem; color: #6b7280; max-width: 280px; }
.ont-download-btn { display: flex; align-items: center; gap: 0.4rem; padding: 0.6rem 1.25rem; background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; border: none; border-radius: 9px; font-weight: 600; cursor: pointer; }
.office-embed-panel { flex: 1; padding: 1.5rem; display: flex; align-items: center; justify-content: center; }
.office-embed-notice { background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 10px; padding: 1.25rem; display: flex; align-items: center; gap: 0.75rem; font-size: 0.85rem; color: #1d4ed8; max-width: 400px; flex-wrap: wrap; }
.office-embed-notice svg { width: 18px; height: 18px; flex-shrink: 0; }
.office-embed-link { font-weight: 700; color: #1d4ed8; text-decoration: underline; }

/* Audio */
.audio-viewer { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2rem; padding: 2rem; background: linear-gradient(135deg, #0f172a, #1e293b); }
.audio-art { text-align: center; }
.audio-wave { display: flex; align-items: center; justify-content: center; gap: 3px; height: 80px; margin-bottom: 1rem; }
.wave-bar { width: 4px; border-radius: 2px; background: linear-gradient(to top, #3b82f6, #818cf8); animation: wave 1.2s ease-in-out infinite alternate; }
@keyframes wave { 0% { transform: scaleY(0.3); } 100% { transform: scaleY(1); } }
.audio-file-icon { font-size: 3rem; }
.audio-filename { color: #94a3b8; font-size: 0.85rem; margin-top: 0.5rem; }
.audio-player { width: 100%; max-width: 400px; border-radius: 10px; }

/* Video */
.video-viewer { flex: 1; display: flex; align-items: center; justify-content: center; background: #000; }
.video-player { max-width: 100%; max-height: 100%; }

/* Unsupported */
.unsupported-viewer { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 0.75rem; padding: 2rem; text-align: center; }
.unsupported-icon-wrap { font-size: 5rem; margin-bottom: 0.5rem; }
.unsupported-title { font-size: 1.25rem; font-weight: 700; color: #111827; }
.unsupported-text  { color: #6b7280; }
.unsupported-mime  { font-size: 0.75rem; font-family: monospace; background: #f3f4f6; padding: 0.3rem 0.7rem; border-radius: 6px; color: #374151; }
.download-instead-btn { display: flex; align-items: center; gap: 0.4rem; padding: 0.65rem 1.5rem; background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; border: none; border-radius: 10px; font-weight: 600; cursor: pointer; margin-top: 0.5rem; transition: all 0.2s; }
.download-instead-btn:hover { box-shadow: 0 6px 14px rgba(37,99,235,0.35); }

/* ── Right: Details pane ── */
.viewer-details-pane {
  overflow-y: auto; display: flex; flex-direction: column;
  background: #fafafa;
}

/* OCR status bar */
.ocr-status-bar {
  display: flex; align-items: center; gap: 0.5rem;
  padding: 0.6rem 1rem; font-size: 0.78rem; font-weight: 600; flex-shrink: 0;
}
.ocr-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
.ocr-done    { background: #d1fae5; color: #065f46; border-bottom: 1px solid #a7f3d0; }
.ocr-done    .ocr-dot { background: #10b981; }
.ocr-fail    { background: #fee2e2; color: #991b1b; border-bottom: 1px solid #fca5a5; }
.ocr-fail    .ocr-dot { background: #ef4444; }
.ocr-pending { background: #fef3c7; color: #92400e; border-bottom: 1px solid #fde68a; }
.ocr-pending .ocr-dot { background: #f59e0b; animation: blink 1s ease infinite; }
@keyframes blink { 0%,100% { opacity: 1; } 50% { opacity: 0.3; } }

/* Detail sections */
.detail-section {
  padding: 1rem 1.1rem; border-bottom: 1px solid #f0f0f0;
}
.detail-section:last-child { border-bottom: none; }
.detail-section-title {
  display: flex; align-items: center; gap: 0.4rem;
  font-size: 0.78rem; font-weight: 800; color: #374151;
  text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 0.75rem;
}
.detail-section-title svg { width: 14px; height: 14px; color: #6b7280; }

/* DL list */
.detail-list { display: flex; flex-direction: column; gap: 0.5rem; }
.dl-row { display: grid; grid-template-columns: 90px 1fr; gap: 0.5rem; align-items: start; }
.dl-row dt { font-size: 0.75rem; font-weight: 600; color: #9ca3af; padding-top: 0.1rem; }
.dl-row dd { font-size: 0.82rem; color: #111827; word-break: break-all; }
.dd-mono  { font-family: monospace; font-size: 0.76rem; color: #374151; }
.dd-accent { font-weight: 600; color: #047857; }
.mime-badge { display: inline-block; background: #eff6ff; color: #1d4ed8; font-size: 0.68rem; font-weight: 800; padding: 0.1rem 0.45rem; border-radius: 4px; text-transform: uppercase; margin-right: 0.3rem; }
.mime-text { font-size: 0.72rem; color: #6b7280; font-family: monospace; }
.cat-pill { display: inline-block; background: linear-gradient(135deg,#dbeafe,#e0e7ff); color: #1d4ed8; font-size: 0.78rem; font-weight: 600; padding: 0.15rem 0.6rem; border-radius: 9999px; }

/* Relevance bar */
.relevance-bar-wrap { display: flex; align-items: center; gap: 0.5rem; }
.relevance-bar { flex: 1; height: 6px; background: #e5e7eb; border-radius: 3px; overflow: hidden; }
.relevance-fill { height: 100%; background: linear-gradient(90deg,#2563eb,#4f46e5); border-radius: 3px; transition: width 0.8s ease; }
.relevance-pct { font-size: 0.75rem; font-weight: 700; color: #2563eb; white-space: nowrap; }

/* Tags cloud */
.tags-cloud { display: flex; flex-wrap: wrap; gap: 0.4rem; }
.tag-cloud-item {
  padding: 0.25rem 0.625rem; background: #f0f9ff;
  border: 1px solid #bae6fd; color: #0369a1;
  font-size: 0.75rem; font-weight: 600; border-radius: 6px;
  cursor: pointer; transition: all 0.15s;
}
.tag-cloud-item:hover { background: #0369a1; color: white; border-color: #0369a1; }

/* Excerpt / description */
.detail-desc { font-size: 0.83rem; color: #374151; line-height: 1.6; }
.detail-highlights { display: flex; flex-direction: column; gap: 0.4rem; margin-top: 0.5rem; }
.detail-highlight-item { font-size: 0.78rem; background: #fef9c3; border-left: 3px solid #fbbf24; padding: 0.4rem 0.6rem; border-radius: 0 6px 6px 0; font-style: italic; color: #374151; }

/* Suggestions */
.suggestions-loading { display: flex; align-items: center; gap: 0.5rem; font-size: 0.82rem; color: #9ca3af; }
.suggestions-empty { display: flex; align-items: center; gap: 0.4rem; font-size: 0.82rem; color: #9ca3af; }
.suggestions-empty svg { width: 16px; height: 16px; }
.suggestions-list { display: flex; flex-direction: column; gap: 0.5rem; }
.suggestion-card {
  display: flex; align-items: center; gap: 0.75rem;
  padding: 0.65rem 0.75rem;
  background: white; border: 1px solid #e5e7eb; border-radius: 10px;
  cursor: pointer; transition: all 0.15s;
}
.suggestion-card:hover { border-color: #3b82f6; background: #eff6ff; box-shadow: 0 2px 8px rgba(37,99,235,0.1); }
.sug-icon { font-size: 1.5rem; flex-shrink: 0; }
.sug-info { flex: 1; min-width: 0; }
.sug-title  { font-size: 0.82rem; font-weight: 600; color: #111827; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.sug-reason { font-size: 0.72rem; color: #9ca3af; margin-top: 0.1rem; }
.sug-score  { flex-shrink: 0; }
.sug-score-ring {
  width: 40px; height: 40px; border-radius: 50%;
  background: conic-gradient(#2563eb calc(var(--pct) * 1%), #e5e7eb 0);
  display: flex; align-items: center; justify-content: center;
}
.sug-score-ring span { font-size: 0.65rem; font-weight: 800; color: #1d4ed8; background: white; width: 28px; height: 28px; border-radius: 50%; display: flex; align-items: center; justify-content: center; }

/* Spinners */
.spinner { animation: spin 1s linear infinite; }
.spinner.sm { width: 16px; height: 16px; }
.spinner.xl { width: 40px; height: 40px; }
.spinner-bg   { opacity: 0.25; }
.spinner-path { opacity: 0.75; }
@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }

/* ── Tab-hidden: mobile only ── */
@media (min-width: 900px) {
  .tab-hidden { display: flex !important; }
  .tab-switcher { display: none; }
}
@media (max-width: 899px) {
  .viewer-body { grid-template-columns: 1fr; }
  .viewer-preview-pane { border-right: none; border-bottom: 1px solid #e5e7eb; height: 55vh; }
  .viewer-details-pane { height: auto; max-height: 35vh; }
  .tab-hidden { display: none !important; }
  .viewer-preview-pane.tab-hidden, .viewer-details-pane.tab-hidden { display: none !important; }
  .viewer-preview-pane:not(.tab-hidden), .viewer-details-pane:not(.tab-hidden) { display: flex !important; }
}

/* ── Upload modal ─────────────────────────────────────────────────────────── */
.modal-content {
  background: white; border-radius: 16px; box-shadow: 0 25px 50px rgba(0,0,0,0.2);
  max-width: 42rem; width: 100%; max-height: 90vh; overflow-y: auto; padding: 2rem;
}
.modal-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 1.5rem; }
.modal-title  { font-size: 1.4rem; font-weight: 700; color: #111827; }
.modal-close  { background: none; border: none; color: #9ca3af; cursor: pointer; }
.modal-close .icon { width: 22px; height: 22px; }
.modal-body { display: flex; flex-direction: column; gap: 1.25rem; }
.upload-area { border: 2px dashed #d1d5db; border-radius: 12px; padding: 2rem; text-align: center; transition: border-color 0.2s; }
.upload-area:hover { border-color: #60a5fa; }
.file-input { display: none; }
.upload-prompt { cursor: pointer; }
.upload-icon { width: 56px; height: 56px; color: #9ca3af; margin: 0 auto 0.875rem; }
.upload-text  { font-size: 1rem; font-weight: 500; color: #374151; margin-bottom: 0.2rem; }
.upload-hint  { font-size: 0.8rem; color: #6b7280; }
.file-preview { display: flex; align-items: center; justify-content: center; gap: 0.875rem; }
.file-emoji { font-size: 2.5rem; }
.file-info { text-align: left; }
.file-name { font-weight: 500; color: #111827; }
.file-size { font-size: 0.8rem; color: #6b7280; }
.remove-file { background: none; border: none; color: #ef4444; cursor: pointer; }
.remove-file .icon { width: 20px; height: 20px; }
.form-group { display: flex; flex-direction: column; }
.form-label { font-size: 0.85rem; font-weight: 600; color: #374151; margin-bottom: 0.4rem; }
.form-input { width: 100%; padding: 0.7rem 1rem; border: 2px solid #e5e7eb; border-radius: 10px; outline: none; font-size: 0.9rem; transition: all 0.2s; }
.form-input:focus { border-color: #3b82f6; }
.required-indicator { color: #ef4444; }
.filter-select.error { border-color: #ef4444; background: #fef2f2; }
.error-message { font-size: 0.75rem; color: #ef4444; margin-top: 0.3rem; }
.modal-actions { display: flex; gap: 0.875rem; }
.upload-submit { flex: 1; padding: 0.75rem; background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; border: none; border-radius: 10px; font-weight: 700; cursor: pointer; transition: all 0.2s; }
.upload-submit:hover:not(:disabled) { box-shadow: 0 6px 14px rgba(37,99,235,0.35); }
.upload-submit:disabled, .upload-submit.disabled { opacity: 0.5; cursor: not-allowed; }
.cancel-btn { padding: 0.75rem 1.5rem; background: #f3f4f6; color: #374151; border: none; border-radius: 10px; font-weight: 600; cursor: pointer; transition: all 0.15s; }
.cancel-btn:hover { background: #e5e7eb; }
</style>