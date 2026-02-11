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
          <!-- Search Bar -->
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
            <button
              @click="performSearch"
              :disabled="loading || !searchQuery.trim()"
              class="search-btn"
            >
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

          <!-- Quick Search Suggestions -->
          <div class="quick-searches">
            <span class="quick-label">Try:</span>
            <button
              v-for="suggestion in quickSearches"
              :key="suggestion"
              @click="searchQuery = suggestion; performSearch()"
              class="quick-btn"
            >
              {{ suggestion }}
            </button>
          </div>

          <!-- Advanced Filters Toggle -->
          <button @click="showFilters = !showFilters" class="filters-toggle">
            <svg class="toggle-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4" />
            </svg>
            {{ showFilters ? 'Hide' : 'Show' }} Advanced Filters
          </button>

          <!-- Advanced Filters Panel -->
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
        <!-- Results Summary -->
        <div class="results-summary">
          <div class="summary-card">
            <span class="summary-count">{{ searchResults.totalResults }}</span>
            <span class="summary-text"> results</span>
            <span class="summary-divider">•</span>
            <span class="summary-text">in </span>
            <span class="summary-time">{{ searchResults.searchTimeMs }}ms</span>
          </div>
          <div class="summary-page">
            Page {{ searchResults.page }} of {{ searchResults.totalPages }}
          </div>
        </div>

        <!-- Document Cards -->
        <div class="documents-grid">
          <article
            v-for="doc in searchResults.documents"
            :key="doc.id"
            class="document-card"
          >
            <div class="card-content">
              <!-- Document Info -->
              <div class="doc-info">
                <div class="doc-header">
                  <!-- File Icon -->
                  <div class="file-icon-wrapper">
                    <div class="file-icon">
                      <span class="icon-emoji">{{ getFileIcon(doc.contentType) }}</span>
                    </div>
                  </div>

                  <!-- Content -->
                  <div class="doc-details">
                    <h3 class="doc-title">{{ doc.title }}</h3>
                    
                    <p v-if="doc.description" class="doc-description">
                      {{ doc.description }}
                    </p>
                    
                    <!-- Highlights -->
                    <div v-if="doc.highlights && doc.highlights.length" class="highlights">
                      <div
                        v-for="(highlight, idx) in doc.highlights.slice(0, 2)"
                        :key="idx"
                        class="highlight-item"
                        v-html="highlight"
                      ></div>
                    </div>

                    <!-- Metadata Row -->
                    <div class="metadata-row">
                      <span class="meta-item">
                        <svg class="meta-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                        </svg>
                        {{ doc.fileName }}
                      </span>
                      <span class="meta-item">
                        <svg class="meta-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                        </svg>
                        {{ formatDate(doc.createdAt) }}
                      </span>
                      <span class="meta-item">
                        <svg class="meta-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M9 19l3 3m0 0l3-3m-3 3V10" />
                        </svg>
                        {{ formatFileSize(doc.fileSize) }}
                      </span>
                      <span v-if="doc.category" class="category-badge">
                        {{ doc.category }}
                      </span>
                    </div>

                    <!-- Tags -->
                    <div v-if="doc.tags && doc.tags.length" class="tags-row">
                      <span
                        v-for="tag in doc.tags.slice(0, 5)"
                        :key="tag"
                        class="tag"
                      >
                        #{{ tag }}
                      </span>
                      <span v-if="doc.tags.length > 5" class="tag-more">
                        +{{ doc.tags.length - 5 }} more
                      </span>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Relevance Score & Actions -->
              <div class="doc-actions">
                <div class="score-wrapper">
                  <div class="score-circle">
                    <svg class="circle-svg" viewBox="0 0 100 100">
                      <circle cx="50" cy="50" r="40" class="circle-bg" />
                      <circle 
                        cx="50" 
                        cy="50" 
                        r="40" 
                        class="circle-progress"
                        :style="`stroke-dashoffset: ${251 - (251 * doc.score)}`"
                      />
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

        <!-- Pagination -->
        <nav v-if="searchResults.totalPages > 1" class="pagination">
          <button
            v-for="page in paginationPages"
            :key="page"
            @click="goToPage(page)"
            :class="['page-btn', { active: page === searchResults.page }]"
          >
            {{ page }}
          </button>
        </nav>
      </section>

      <!-- Empty State -->
      <section v-else-if="!loading && searched" class="empty-state">
        <div class="empty-icon">
          <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        </div>
        <h3 class="empty-title">No documents found</h3>
        <p class="empty-text">Try adjusting your search terms or filters</p>
        <button @click="searchQuery = ''; searchResults = null; searched = false" class="clear-btn">
          Clear Search
        </button>
      </section>

      <!-- Initial State -->
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

    <!-- Document Viewer Modal -->
    <div v-if="showDocumentViewer" class="modal-overlay" @click.self="closeDocumentViewer">
      <div class="viewer-modal">
        <!-- Viewer Header -->
        <div class="viewer-header">
          <div class="viewer-title-section">
            <h2 class="viewer-title">{{ currentDocument?.title }}</h2>
            <p class="viewer-filename">{{ currentDocument?.fileName }}</p>
          </div>
          <div class="viewer-actions">
            <button @click="downloadDocument(currentDocument.id)" class="viewer-action-btn download-btn" title="Download">
              <svg class="btn-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
              </svg>
              Download
            </button>
            <button @click="closeDocumentViewer" class="viewer-action-btn close-btn" title="Close">
              <svg class="btn-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>

        <!-- Viewer Content -->
        <div class="viewer-content">
          <!-- Loading State -->
          <div v-if="documentLoading" class="viewer-loading">
            <svg class="spinner large" fill="none" viewBox="0 0 24 24">
              <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
            <p class="loading-message">Loading document...</p>
          </div>

          <!-- PDF Viewer -->
          <div v-else-if="isPDF(currentDocument?.contentType)" class="pdf-viewer">
            <iframe 
              :src="documentUrl" 
              class="pdf-frame"
              title="PDF Viewer"
            ></iframe>
          </div>

          <!-- Image Viewer -->
          <div v-else-if="isImage(currentDocument?.contentType)" class="image-viewer">
            <img :src="documentUrl" :alt="currentDocument?.title" class="document-image" />
          </div>

          <!-- Text Viewer -->
          <div v-else-if="isText(currentDocument?.contentType)" class="text-viewer">
            <pre class="text-content">{{ documentContent }}</pre>
          </div>

          <!-- Unsupported Format -->
          <div v-else class="unsupported-viewer">
            <div class="unsupported-icon">
              <svg class="icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
            </div>
            <h3 class="unsupported-title">Preview not available</h3>
            <p class="unsupported-text">This file type cannot be previewed in the browser.</p>
            <p class="unsupported-hint">File type: {{ currentDocument?.contentType }}</p>
            <button @click="downloadDocument(currentDocument.id)" class="download-instead-btn">
              <svg class="btn-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
              </svg>
              Download File Instead
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Upload Modal -->
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
          <!-- AI Suggestions Toggle -->
          <div class="ai-toggle-section">
            <label class="ai-toggle-label">
              <input
                v-model="useAiSuggestions"
                @change="saveAiPreference(useAiSuggestions)"
                type="checkbox"
                class="ai-toggle-checkbox"
              />
              <span class="ai-toggle-switch"></span>
              <span class="ai-toggle-text">
                <span class="toggle-icon">✨</span>
                <span class="toggle-title">Use AI to suggest metadata</span>
              </span>
            </label>
            <p class="ai-toggle-hint">AI will analyze your document and suggest a title and category</p>
          </div>

          <!-- File Upload Area -->
          <div class="upload-area" :class="{ 'has-file': selectedFile }">
            <input
              ref="fileInput"
              type="file"
              @change="handleFileSelect"
              class="file-input"
              accept=".pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png,.tiff,.txt"
            />
            <div v-if="!selectedFile" @click="$refs.fileInput.click()" class="upload-prompt">
              <svg class="upload-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
              </svg>
              <p class="upload-text">Click to upload or drag and drop</p>
              <p class="upload-hint">PDF, Word, Excel, Images, Text files (max 100MB)</p>
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

          <!-- AI Suggestion Loading -->
          <div v-if="aiSuggesting" class="ai-loading">
            <svg class="spinner" fill="none" viewBox="0 0 24 24">
              <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
            <span class="ai-text">✨ AI is analyzing your document...</span>
          </div>

          <!-- AI Suggestion Banner -->
          <div v-if="aiSuggestion && !aiSuggesting" class="ai-suggestion">
            <div class="ai-icon">✨</div>
            <div class="ai-content">
              <p class="ai-label">AI Suggestions ({{ Math.round(aiSuggestion.confidence * 100) }}% confidence)</p>
              <p class="ai-hint">You can edit these suggestions below</p>
            </div>
          </div>

          <!-- Metadata Fields -->
          <div class="form-group">
            <label class="form-label">Title</label>
            <input
              v-model="uploadData.title"
              type="text"
              :placeholder="useAiSuggestions ? 'AI will suggest a title' : 'Enter document title'"
              class="form-input"
              :class="{ 'ai-suggested': aiSuggestion }"
            />
          </div>

          <div class="form-group">
            <label class="form-label">Category</label>
            <select v-model="uploadData.category" class="filter-select" :class="{ 'ai-suggested': aiSuggestion }">
              <option value="">{{ useAiSuggestions ? 'AI will suggest a category' : 'Select a category' }}</option>
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

          <!-- Action Buttons -->
          <div class="modal-actions">
            <button
              @click="uploadDocument"
              :disabled="!selectedFile || uploading"
              class="upload-submit"
            >
              <span v-if="!uploading">Upload</span>
              <span v-else class="loading-text">
                <svg class="spinner" fill="none" viewBox="0 0 24 24">
                  <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                Uploading...
              </span>
            </button>
            <button @click="closeUploadModal" class="cancel-btn">
              Cancel
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { format } from 'date-fns'

const searchQuery = ref('')
const showFilters = ref(false)
const loading = ref(false)
const searched = ref(false)
const searchResults = ref(null)
const showUploadModal = ref(false)
const selectedFile = ref(null)
const uploading = ref(false)
const aiSuggesting = ref(false)
const aiSuggestion = ref(null)

// Document viewer state
const showDocumentViewer = ref(false)
const currentDocument = ref(null)
const documentUrl = ref(null)
const documentContent = ref(null)
const documentLoading = ref(false)

// Load preference from localStorage
const savedPreference = localStorage.getItem('useAiSuggestions')
const useAiSuggestions = ref(savedPreference !== null ? JSON.parse(savedPreference) : true)

const filters = reactive({
  category: '',
  contentType: '',
  dateFrom: '',
  dateTo: ''
})

const uploadData = reactive({
  title: '',
  category: ''
})

const quickSearches = [
  'all documents',
  'invoices',
  'PDFs from last month',
  'contracts from 2024'
]

const paginationPages = computed(() => {
  if (!searchResults.value) return []
  const total = searchResults.value.totalPages
  const current = searchResults.value.page
  const pages = []
  
  const start = Math.max(1, current - 4)
  const end = Math.min(total, start + 9)
  
  for (let i = start; i <= end; i++) {
    pages.push(i)
  }
  
  return pages
})

// ⭐ NEW HELPER FUNCTION: Map file type to content type
const mapFileTypeToContentType = (fileType) => {
  const mapping = {
    'pdf': 'application/pdf',
    'doc': 'application/msword',
    'docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    'xls': 'application/vnd.ms-excel',
    'xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
    'png': 'image/png',
    'txt': 'text/plain'
  }
  return mapping[fileType?.toLowerCase()] || null
}

// Document viewer helper functions
const isPDF = (contentType) => {
  return contentType === 'application/pdf'
}

const isImage = (contentType) => {
  return contentType?.startsWith('image/')
}

const isText = (contentType) => {
  return contentType === 'text/plain'
}

const viewDocument = async (doc) => {
  currentDocument.value = doc
  showDocumentViewer.value = true
  documentLoading.value = true
  documentContent.value = null
  
  try {
    // For PDFs and images, we can use the download URL directly
    if (isPDF(doc.contentType) || isImage(doc.contentType)) {
      documentUrl.value = `/api/documents/${doc.id}/download`
      documentLoading.value = false
    }
    // For text files, fetch the content
    else if (isText(doc.contentType)) {
      const response = await fetch(`/api/documents/${doc.id}/download`)
      if (response.ok) {
        documentContent.value = await response.text()
      } else {
        throw new Error('Failed to load document')
      }
      documentLoading.value = false
    }
    // For other types, just show unsupported message
    else {
      documentLoading.value = false
    }
  } catch (error) {
    console.error('Error loading document:', error)
    alert('Failed to load document. Please try downloading it instead.')
    closeDocumentViewer()
  }
}

const closeDocumentViewer = () => {
  showDocumentViewer.value = false
  currentDocument.value = null
  documentUrl.value = null
  documentContent.value = null
  documentLoading.value = false
}

const downloadDocument = (id) => {
  window.open(`/api/documents/${id}/download`, '_blank')
}

onMounted(() => {
  console.log('AI suggestions preference loaded:', useAiSuggestions.value)
})

const saveAiPreference = (value) => {
  localStorage.setItem('useAiSuggestions', JSON.stringify(value))
  console.log('AI suggestions preference saved:', value)
}

const handleFileSelect = async (event) => {
  const file = event.target.files[0]
  if (!file) return
  
  selectedFile.value = file
  console.log('File selected:', file.name, file.type, file.size)
  
  uploadData.title = ''
  uploadData.category = ''
  aiSuggestion.value = null
  
  if (useAiSuggestions.value) {
    console.log('AI suggestions enabled - requesting AI analysis')
    await getAiSuggestions(file)
  } else {
    console.log('AI suggestions disabled - using filename as title')
    uploadData.title = file.name.replace(/\.[^/.]+$/, "")
  }
}

const getAiSuggestions = async (file) => {
  aiSuggesting.value = true
  aiSuggestion.value = null
  
  try {
    const formData = new FormData()
    formData.append('file', file)

    console.log('Requesting AI suggestions for:', file.name)

    const response = await fetch('/api/metadata/suggest', {
      method: 'POST',
      body: formData
    })

    if (response.ok) {
      const suggestion = await response.json()
      console.log('AI suggestion received:', suggestion)
      aiSuggestion.value = suggestion
      
      uploadData.title = suggestion.title || file.name
      uploadData.category = suggestion.category || ''
      
      console.log('Form filled with AI suggestions:', uploadData)
    } else {
      const errorText = await response.text()
      console.error('AI suggestion failed:', response.status, errorText)
      uploadData.title = file.name.replace(/\.[^/.]+$/, "")
    }
  } catch (error) {
    console.error('AI suggestion error:', error)
    uploadData.title = file.name.replace(/\.[^/.]+$/, "")
  } finally {
    aiSuggesting.value = false
  }
}

// ⭐ FIXED performSearch function
const performSearch = async () => {
  if (!searchQuery.value.trim()) return

  loading.value = true
  searched.value = true

  try {
    // Step 1: Call NLP Understanding Endpoint
    let nlpFilters = null
    try {
      console.log('🧠 Calling NLP endpoint to understand query:', searchQuery.value)
      
      const nlpResponse = await fetch('/api/search/nlp/understand', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(searchQuery.value)
      })
      
      if (nlpResponse.ok) {
        const nlpResult = await nlpResponse.json()
        console.log('✅ NLP understanding:', nlpResult)
        nlpFilters = nlpResult.extractedFilters
        
        if (nlpFilters && Object.keys(nlpFilters).length > 0) {
          console.log('📅 NLP extracted filters:', nlpFilters)
        }
      } else {
        console.warn('⚠️ NLP endpoint returned error:', nlpResponse.status)
      }
    } catch (nlpError) {
      console.warn('⚠️ NLP processing failed, continuing with manual filters:', nlpError)
    }

    // ⭐ FIX: Map filetype to contentType
    let contentTypeFilter = null
    if (nlpFilters?.filetype) {
      contentTypeFilter = mapFileTypeToContentType(nlpFilters.filetype)
      console.log(`📎 Mapped filetype '${nlpFilters.filetype}' to contentType '${contentTypeFilter}'`)
    } else if (filters.contentType) {
      contentTypeFilter = filters.contentType
    }

    // Step 2: Merge NLP filters with manual filters
    const requestBody = {
      query: searchQuery.value,
      searchType: 0,
      page: 1,
      pageSize: 20,
      categories: filters.category ? [filters.category] : null,
      contentTypes: contentTypeFilter ? [contentTypeFilter] : null,
      fromDate: nlpFilters?.fromDate || filters.dateFrom || null,
      toDate: nlpFilters?.toDate || filters.dateTo || null,
      includeOcrContent: true
    }

    console.log('🔍 Final search request (with NLP filters merged):', requestBody)

    const response = await fetch('/api/search/query', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(requestBody)
    })

    if (response.ok) {
      searchResults.value = await response.json()
      console.log('✅ Search results:', searchResults.value)
    } else {
      const errorText = await response.text()
      console.error('❌ Search failed:', response.status, errorText)
      alert(`Search failed: ${response.status} - ${errorText}`)
    }
  } catch (error) {
    console.error('❌ Search error:', error)
    alert('Search error. Make sure the backend is running.')
  } finally {
    loading.value = false
  }
}

// ⭐ FIXED goToPage function
const goToPage = async (page) => {
  if (!searchQuery.value.trim()) return

  loading.value = true

  try {
    // Same NLP processing as performSearch
    let nlpFilters = null
    try {
      const nlpResponse = await fetch('/api/search/nlp/understand', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(searchQuery.value)
      })
      
      if (nlpResponse.ok) {
        const nlpResult = await nlpResponse.json()
        nlpFilters = nlpResult.extractedFilters
      }
    } catch (nlpError) {
      console.warn('⚠️ NLP processing failed for pagination:', nlpError)
    }

    // ⭐ FIX: Map filetype to contentType
    let contentTypeFilter = null
    if (nlpFilters?.filetype) {
      contentTypeFilter = mapFileTypeToContentType(nlpFilters.filetype)
    } else if (filters.contentType) {
      contentTypeFilter = filters.contentType
    }

    const response = await fetch('/api/search/query', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        query: searchQuery.value,
        searchType: 0,
        page: page,
        pageSize: 20,
        categories: filters.category ? [filters.category] : null,
        contentTypes: contentTypeFilter ? [contentTypeFilter] : null,
        fromDate: nlpFilters?.fromDate || filters.dateFrom || null,
        toDate: nlpFilters?.toDate || filters.dateTo || null,
        includeOcrContent: true
      })
    })

    if (response.ok) {
      searchResults.value = await response.json()
      window.scrollTo({ top: 0, behavior: 'smooth' })
    }
  } catch (error) {
    console.error('Pagination error:', error)
  } finally {
    loading.value = false
  }
}

const uploadDocument = async () => {
  if (!selectedFile.value) return

  uploading.value = true

  try {
    const formData = new FormData()
    formData.append('file', selectedFile.value)
    formData.append('title', uploadData.title || selectedFile.value.name)
    if (uploadData.category) {
      formData.append('category', uploadData.category)
    }

    console.log('Uploading document:', {
      fileName: selectedFile.value.name,
      title: uploadData.title,
      category: uploadData.category
    })

    const response = await fetch('/api/documents/upload', {
      method: 'POST',
      body: formData
    })

    if (response.ok) {
      const result = await response.json()
      console.log('Upload successful:', result)
      alert('Document uploaded and indexed successfully!')
      closeUploadModal()
      
      if (searchResults.value) {
        performSearch()
      }
    } else {
      const error = await response.json()
      console.error('Upload failed:', error)
      alert(`Upload failed: ${error.message || 'Unknown error'}`)
    }
  } catch (error) {
    console.error('Upload error:', error)
    alert('Upload error. Make sure the backend is running.')
  } finally {
    uploading.value = false
  }
}

const clearFile = () => {
  selectedFile.value = null
  uploadData.title = ''
  uploadData.category = ''
  aiSuggestion.value = null
  aiSuggesting.value = false
  
  if (document.querySelector('.file-input')) {
    document.querySelector('.file-input').value = ''
  }
}

const closeUploadModal = () => {
  showUploadModal.value = false
  clearFile()
}

const formatDate = (dateString) => {
  return format(new Date(dateString), 'MMM d, yyyy')
}

const formatFileSize = (bytes) => {
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / (1024 * 1024)).toFixed(1) + ' MB'
}

const getFileIcon = (contentType) => {
  if (!contentType) return '📎'
  if (contentType.includes('pdf')) return '📄'
  if (contentType.includes('word') || contentType.includes('document')) return '📝'
  if (contentType.includes('sheet') || contentType.includes('excel')) return '📊'
  if (contentType.includes('image')) return '🖼️'
  if (contentType.includes('text')) return '📃'
  return '📎'
}
</script>

<style scoped>
/* All your existing CSS remains exactly the same */
*,
*::before,
*::after {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
}

.app-container {
  min-height: 100vh;
  background: linear-gradient(135deg, #f8fafc 0%, #e0f2fe 50%, #ddd6fe 100%);
  width: 100%;
  overflow-x: hidden;
}

.app-header {
  background: rgba(255, 255, 255, 0.8);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-bottom: 1px solid #e5e7eb;
  position: sticky;
  top: 0;
  z-index: 50;
  width: 100%;
}

.header-content {
  max-width: 1280px;
  margin: 0 auto;
  padding: 0 1rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 80px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.logo-icon {
  width: 48px;
  height: 48px;
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
  flex-shrink: 0;
}

.logo-icon .icon {
  width: 28px;
  height: 28px;
  color: white;
}

.header-text {
  display: flex;
  flex-direction: column;
}

.header-title {
  font-size: 1.5rem;
  font-weight: 700;
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.header-subtitle {
  font-size: 0.875rem;
  color: #6b7280;
}

.upload-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.625rem 1.25rem;
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  color: white;
  border: none;
  border-radius: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.upload-btn:hover {
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  transform: scale(1.05);
}

.btn-icon {
  width: 20px;
  height: 20px;
  flex-shrink: 0;
}

.btn-text {
  white-space: nowrap;
}

.main-content {
  max-width: 1280px;
  margin: 0 auto;
  padding: 2rem 1rem;
  width: 100%;
}

.search-section {
  margin-bottom: 2rem;
  width: 100%;
}

.search-card {
  background: white;
  border-radius: 16px;
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  padding: 2rem;
  border: 1px solid #f3f4f6;
  width: 100%;
}

.search-bar-wrapper {
  display: flex;
  gap: 0.75rem;
  width: 100%;
  flex-wrap: wrap;
}

.search-input-wrapper {
  flex: 1;
  min-width: 200px;
  position: relative;
  display: flex;
  align-items: center;
}

.search-icon {
  position: absolute;
  left: 1rem;
  width: 24px;
  height: 24px;
  color: #9ca3af;
  pointer-events: none;
  flex-shrink: 0;
}

.search-input {
  width: 100%;
  padding: 1rem 1rem 1rem 3rem;
  font-size: 1.125rem;
  border: 2px solid #e5e7eb;
  border-radius: 12px;
  outline: none;
  transition: all 0.2s;
}

.search-input:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 4px rgba(59, 130, 246, 0.1);
}

.search-btn {
  padding: 1rem 2rem;
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  color: white;
  border: none;
  border-radius: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  white-space: nowrap;
}

.search-btn:hover:not(:disabled) {
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  transform: scale(1.05);
}

.search-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.loading-text {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.spinner {
  width: 20px;
  height: 20px;
  animation: spin 1s linear infinite;
}

.spinner.large {
  width: 48px;
  height: 48px;
}

.spinner-bg {
  opacity: 0.25;
}

.spinner-path {
  opacity: 0.75;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

.quick-searches {
  margin-top: 1rem;
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
}

.quick-label {
  font-size: 0.875rem;
  color: #6b7280;
  margin-right: 0.5rem;
}

.quick-btn {
  padding: 0.375rem 0.75rem;
  font-size: 0.875rem;
  background: #f3f4f6;
  color: #374151;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s;
}

.quick-btn:hover {
  background: #dbeafe;
  color: #1d4ed8;
}

.filters-toggle {
  margin-top: 1rem;
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  color: #2563eb;
  background: none;
  border: none;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
}

.toggle-icon {
  width: 16px;
  height: 16px;
}

.filters-panel {
  margin-top: 1.5rem;
  padding-top: 1.5rem;
  border-top: 1px solid #e5e7eb;
}

.filters-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.filter-group {
  display: flex;
  flex-direction: column;
}

.filter-label {
  display: block;
  font-size: 0.875rem;
  font-weight: 600;
  color: #374151;
  margin-bottom: 0.5rem;
}

.filter-select,
.filter-input {
  width: 100%;
  padding: 0.625rem 1rem;
  border: 2px solid #e5e7eb;
  border-radius: 8px;
  outline: none;
  transition: all 0.2s;
}

.filter-select:focus,
.filter-input:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.1);
}

.results-section {
  width: 100%;
}

.results-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1.5rem;
  flex-wrap: wrap;
  gap: 1rem;
}

.summary-card {
  background: white;
  border-radius: 12px;
  box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
  padding: 0.75rem 1.5rem;
  border: 1px solid #f3f4f6;
  font-size: 0.875rem;
}

.summary-count {
  font-weight: 600;
  color: #111827;
}

.summary-text {
  color: #6b7280;
}

.summary-divider {
  color: #d1d5db;
  margin: 0 0.5rem;
}

.summary-time {
  font-weight: 600;
  color: #2563eb;
}

.summary-page {
  font-size: 0.875rem;
  color: #6b7280;
}

.documents-grid {
  display: grid;
  gap: 1.5rem;
  width: 100%;
}

.document-card {
  background: white;
  border-radius: 16px;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
  border: 1px solid #f3f4f6;
  overflow: hidden;
  transition: all 0.3s;
  width: 100%;
}

.document-card:hover {
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
}

.card-content {
  padding: 1.5rem;
  display: flex;
  justify-content: space-between;
  gap: 1.5rem;
  flex-wrap: wrap;
  width: 100%;
}

.doc-info {
  flex: 1;
  min-width: 0;
}

.doc-header {
  display: flex;
  gap: 1rem;
}

.file-icon-wrapper {
  flex-shrink: 0;
}

.file-icon {
  width: 56px;
  height: 56px;
  border-radius: 12px;
  background: linear-gradient(135deg, #dbeafe 0%, #e0e7ff 100%);
  display: flex;
  align-items: center;
  justify-content: center;
}

.icon-emoji {
  font-size: 2rem;
}

.doc-details {
  flex: 1;
  min-width: 0;
}

.doc-title {
  font-size: 1.25rem;
  font-weight: 700;
  color: #111827;
  margin-bottom: 0.5rem;
  word-wrap: break-word;
  transition: color 0.2s;
}

.document-card:hover .doc-title {
  color: #2563eb;
}

.doc-description {
  color: #6b7280;
  margin-bottom: 1rem;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  line-clamp: 2;
  overflow: hidden;
  word-wrap: break-word;
}

.highlights {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.highlight-item {
  font-size: 0.875rem;
  background: #fef3c7;
  border: 1px solid #fde68a;
  border-radius: 8px;
  padding: 0.75rem;
  font-style: italic;
  color: #374151;
}

.metadata-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  font-size: 0.875rem;
  color: #6b7280;
  margin-bottom: 0.75rem;
}

.meta-item {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
}

.meta-icon {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}

.category-badge {
  padding: 0.25rem 0.75rem;
  background: linear-gradient(135deg, #dbeafe 0%, #e0e7ff 100%);
  color: #1d4ed8;
  border-radius: 9999px;
  font-weight: 500;
}

.tags-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.tag {
  padding: 0.25rem 0.625rem;
  background: #f3f4f6;
  color: #374151;
  font-size: 0.75rem;
  border-radius: 8px;
  transition: background 0.2s;
}

.tag:hover {
  background: #e5e7eb;
}

.tag-more {
  padding: 0.25rem 0.625rem;
  color: #6b7280;
  font-size: 0.75rem;
}

.doc-actions {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
}

.score-wrapper {
  text-align: center;
}

.score-circle {
  position: relative;
  width: 80px;
  height: 80px;
}

.circle-svg {
  width: 100%;
  height: 100%;
  transform: rotate(-90deg);
}

.circle-bg {
  fill: none;
  stroke: #e5e7eb;
  stroke-width: 8;
}

.circle-progress {
  fill: none;
  stroke: url(#gradient);
  stroke-width: 8;
  stroke-dasharray: 251;
  stroke-dashoffset: 251;
  transition: stroke-dashoffset 1s ease;
  stroke-linecap: round;
}

.score-text {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
}

.score-value {
  font-size: 1.5rem;
  font-weight: 700;
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.score-label {
  font-size: 0.75rem;
  color: #6b7280;
  margin-top: 0.25rem;
}

.view-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.625rem 1.5rem;
  background: linear-gradient(135deg, #059669 0%, #10b981 100%);
  color: white;
  border: none;
  border-radius: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  white-space: nowrap;
}

.view-btn:hover {
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  transform: scale(1.05);
}

.pagination {
  display: inline-flex;
  background: white;
  border-radius: 12px;
  box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
  padding: 0.5rem;
  gap: 0.25rem;
  margin: 2rem auto;
  justify-content: center;
  width: 100%;
}

.page-btn {
  padding: 0.5rem 1rem;
  border-radius: 8px;
  font-weight: 500;
  border: none;
  cursor: pointer;
  transition: all 0.2s;
  color: #374151;
  background: transparent;
}

.page-btn:hover {
  background: #f3f4f6;
}

.page-btn.active {
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  color: white;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
}

.empty-state,
.initial-state {
  text-align: center;
  padding: 5rem 1rem;
}

.empty-icon,
.initial-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 96px;
  height: 96px;
  border-radius: 50%;
  margin-bottom: 1.5rem;
}

.empty-icon {
  background: linear-gradient(135deg, #f3f4f6 0%, #e5e7eb 100%);
}

.initial-icon {
  background: linear-gradient(135deg, #dbeafe 0%, #e0e7ff 100%);
}

.empty-icon .icon,
.initial-icon .icon {
  width: 48px;
  height: 48px;
  color: #9ca3af;
}

.initial-icon .icon {
  color: #2563eb;
}

.empty-title,
.initial-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #111827;
  margin-bottom: 0.5rem;
}

.empty-text,
.initial-text {
  color: #6b7280;
  margin-bottom: 1.5rem;
}

.clear-btn {
  padding: 0.75rem 1.5rem;
  background: #f3f4f6;
  color: #374151;
  border: none;
  border-radius: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.clear-btn:hover {
  background: #e5e7eb;
}

/* Document Viewer Modal Styles */
.viewer-modal {
  background: white;
  border-radius: 16px;
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
  width: 95vw;
  max-width: 1400px;
  height: 90vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.viewer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.5rem 2rem;
  border-bottom: 1px solid #e5e7eb;
  background: linear-gradient(135deg, #f8fafc 0%, #f0f9ff 100%);
  flex-shrink: 0;
}

.viewer-title-section {
  flex: 1;
  min-width: 0;
  margin-right: 2rem;
}

.viewer-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #111827;
  margin-bottom: 0.25rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.viewer-filename {
  font-size: 0.875rem;
  color: #6b7280;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.viewer-actions {
  display: flex;
  gap: 0.75rem;
  flex-shrink: 0;
}

.viewer-action-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.625rem 1.25rem;
  border: none;
  border-radius: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  white-space: nowrap;
}

.download-btn {
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  color: white;
}

.download-btn:hover {
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  transform: scale(1.05);
}

.close-btn {
  background: #f3f4f6;
  color: #374151;
}

.close-btn:hover {
  background: #e5e7eb;
}

.viewer-content {
  flex: 1;
  overflow: hidden;
  position: relative;
  background: #f9fafb;
}

.viewer-loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  gap: 1.5rem;
}

.loading-message {
  font-size: 1.125rem;
  color: #6b7280;
  font-weight: 500;
}

.pdf-viewer,
.image-viewer,
.text-viewer {
  width: 100%;
  height: 100%;
  overflow: auto;
}

.pdf-frame {
  width: 100%;
  height: 100%;
  border: none;
}

.document-image {
  max-width: 100%;
  height: auto;
  display: block;
  margin: 2rem auto;
  border-radius: 8px;
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
}

.text-content {
  padding: 2rem;
  font-family: 'Courier New', monospace;
  font-size: 0.875rem;
  line-height: 1.6;
  color: #374151;
  background: white;
  margin: 1rem;
  border-radius: 8px;
  border: 1px solid #e5e7eb;
  overflow-x: auto;
  white-space: pre-wrap;
  word-wrap: break-word;
}

.unsupported-viewer {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  padding: 2rem;
  text-align: center;
}

.unsupported-icon {
  width: 96px;
  height: 96px;
  border-radius: 50%;
  background: linear-gradient(135deg, #f3f4f6 0%, #e5e7eb 100%);
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 1.5rem;
}

.unsupported-icon .icon {
  width: 48px;
  height: 48px;
  color: #9ca3af;
}

.unsupported-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #111827;
  margin-bottom: 0.5rem;
}

.unsupported-text {
  color: #6b7280;
  margin-bottom: 0.25rem;
}

.unsupported-hint {
  font-size: 0.875rem;
  color: #9ca3af;
  font-family: monospace;
  margin-bottom: 1.5rem;
}

.download-instead-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1.5rem;
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  color: white;
  border: none;
  border-radius: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.download-instead-btn:hover {
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  transform: scale(1.05);
}

/* Modal Overlay */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(4px);
  -webkit-backdrop-filter: blur(4px);
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
}

.modal-content {
  background: white;
  border-radius: 16px;
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
  max-width: 42rem;
  width: 100%;
  max-height: 90vh;
  overflow-y: auto;
  padding: 2rem;
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1.5rem;
}

.modal-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #111827;
}

.modal-close {
  background: none;
  border: none;
  color: #9ca3af;
  cursor: pointer;
  padding: 0;
  transition: color 0.2s;
}

.modal-close:hover {
  color: #6b7280;
}

.modal-close .icon {
  width: 24px;
  height: 24px;
}

.modal-body {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.upload-area {
  border: 2px dashed #d1d5db;
  border-radius: 12px;
  padding: 2rem;
  text-align: center;
  transition: border-color 0.2s;
}

.upload-area:hover {
  border-color: #60a5fa;
}

.file-input {
  display: none;
}

.upload-prompt {
  cursor: pointer;
}

.upload-icon {
  width: 64px;
  height: 64px;
  color: #9ca3af;
  margin: 0 auto 1rem;
}

.upload-text {
  font-size: 1.125rem;
  font-weight: 500;
  color: #374151;
  margin-bottom: 0.25rem;
}

.upload-hint {
  font-size: 0.875rem;
  color: #6b7280;
}

.file-preview {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
}

.file-emoji {
  font-size: 3rem;
}

.file-info {
  text-align: left;
  flex: 1;
}

.file-name {
  font-weight: 500;
  color: #111827;
}

.file-size {
  font-size: 0.875rem;
  color: #6b7280;
}

.remove-file {
  background: none;
  border: none;
  color: #ef4444;
  cursor: pointer;
  padding: 0;
  transition: color 0.2s;
}

.remove-file:hover {
  color: #dc2626;
}

.remove-file .icon {
  width: 24px;
  height: 24px;
}

.ai-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 1rem;
  background: linear-gradient(135deg, #dbeafe 0%, #e0e7ff 100%);
  border-radius: 12px;
}

.ai-text {
  color: #1d4ed8;
  font-weight: 500;
}

.ai-suggestion {
  display: flex;
  align-items: flex-start;
  gap: 1rem;
  padding: 1rem;
  background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
  border: 2px solid #fbbf24;
  border-radius: 12px;
}

.ai-icon {
  font-size: 2rem;
  flex-shrink: 0;
}

.ai-content {
  flex: 1;
}

.ai-label {
  font-weight: 600;
  color: #78350f;
  margin-bottom: 0.25rem;
}

.ai-hint {
  font-size: 0.875rem;
  color: #92400e;
}

.form-group {
  display: flex;
  flex-direction: column;
}

.form-label {
  display: block;
  font-size: 0.875rem;
  font-weight: 600;
  color: #374151;
  margin-bottom: 0.5rem;
}

.form-input {
  width: 100%;
  padding: 0.75rem 1rem;
  border: 2px solid #e5e7eb;
  border-radius: 12px;
  outline: none;
  transition: all 0.2s;
}

.form-input:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.1);
}

.form-input.ai-suggested,
.filter-select.ai-suggested {
  border-color: #fbbf24;
  background: #fffbeb;
}

.modal-actions {
  display: flex;
  gap: 1rem;
}

.upload-submit {
  flex: 1;
  padding: 0.75rem 1.5rem;
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
  color: white;
  border: none;
  border-radius: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.upload-submit:hover:not(:disabled) {
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
}

.upload-submit:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.ai-toggle-section {
  padding: 1.5rem;
  background: linear-gradient(135deg, #f0f9ff 0%, #f5f3ff 100%);
  border: 2px solid #dbeafe;
  border-radius: 12px;
  margin-bottom: 1.5rem;
}

.ai-toggle-label {
  display: flex;
  align-items: center;
  gap: 1rem;
  cursor: pointer;
  user-select: none;
}

.ai-toggle-checkbox {
  display: none;
}

.ai-toggle-switch {
  position: relative;
  width: 52px;
  height: 28px;
  background: #d1d5db;
  border-radius: 14px;
  transition: background 0.3s;
  flex-shrink: 0;
}

.ai-toggle-switch::before {
  content: '';
  position: absolute;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: white;
  top: 3px;
  left: 3px;
  transition: transform 0.3s;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
}

.ai-toggle-checkbox:checked + .ai-toggle-switch {
  background: linear-gradient(135deg, #2563eb 0%, #4f46e5 100%);
}

.ai-toggle-checkbox:checked + .ai-toggle-switch::before {
  transform: translateX(24px);
}

.ai-toggle-text {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 1;
}

.toggle-icon {
  font-size: 1.5rem;
}

.toggle-title {
  font-weight: 600;
  color: #111827;
  font-size: 1rem;
}

.ai-toggle-hint {
  margin-top: 0.5rem;
  margin-left: 68px;
  font-size: 0.875rem;
  color: #6b7280;
}

.cancel-btn {
  padding: 0.75rem 1.5rem;
  background: #f3f4f6;
  color: #374151;
  border: none;
  border-radius: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.cancel-btn:hover {
  background: #e5e7eb;
}

/* Responsive */
@media (max-width: 768px) {
  .header-content {
    padding: 0 0.75rem;
    height: auto;
    min-height: 80px;
    flex-wrap: wrap;
    gap: 1rem;
  }

  .header-right {
    width: 100%;
  }

  .upload-btn {
    width: 100%;
    justify-content: center;
  }

  .search-bar-wrapper {
    flex-direction: column;
  }

  .search-btn {
    width: 100%;
  }

  .card-content {
    flex-direction: column;
  }

  .doc-actions {
    width: 100%;
    flex-direction: row;
    justify-content: space-between;
  }

  .modal-content {
    padding: 1.5rem;
  }

  .modal-actions {
    flex-direction: column;
  }

  .upload-submit,
  .cancel-btn {
    width: 100%;
  }

  .viewer-modal {
    width: 100vw;
    height: 100vh;
    border-radius: 0;
  }

  .viewer-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 1rem;
  }

  .viewer-title-section {
    margin-right: 0;
  }

  .viewer-actions {
    width: 100%;
    justify-content: stretch;
  }

  .viewer-action-btn {
    flex: 1;
    justify-content: center;
  }
}

@media (max-width: 480px) {
  .header-title {
    font-size: 1.25rem;
  }

  .header-subtitle {
    font-size: 0.75rem;
  }

  .logo-icon {
    width: 40px;
    height: 40px;
  }

  .search-card {
    padding: 1rem;
  }

  .doc-title {
    font-size: 1.125rem;
  }
  
  .ai-toggle-hint {
    margin-left: 0;
    margin-top: 0.75rem;
  }
  
  .ai-toggle-label {
    flex-wrap: wrap;
  }
}
</style>