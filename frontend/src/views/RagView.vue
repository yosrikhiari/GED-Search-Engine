
<template>
  <div class="rag-container">
    <!-- Header -->
    <header class="rag-header">
      <div class="header-content">
        <div class="header-left">
          <router-link to="/" class="back-btn">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7"/>
            </svg>
          </router-link>
          <div class="ai-logo">✨</div>
          <div>
            <h1 class="header-title">Assistant IA — RAG</h1>
            <p class="header-subtitle">Posez une question sur vos documents</p>
          </div>
        </div>
        <div class="header-right">
          <span class="model-badge">{{ modelInfo }}</span>
        </div>
      </div>
    </header>

    <!-- Chat area -->
    <main class="chat-main">
      <!-- Conversation history -->
      <div class="messages-area" ref="messagesArea">

        <!-- Welcome message -->
        <div v-if="messages.length === 0" class="welcome-panel">
          <div class="welcome-icon">🤖</div>
          <h2 class="welcome-title">Bienvenue dans l'Assistant IA</h2>
          <p class="welcome-text">
            Je peux répondre à vos questions en analysant votre base documentaire.
            Je cite toujours mes sources pour que vous puissiez vérifier les informations.
          </p>
          <div class="example-questions">
            <p class="example-label">Questions d'exemple :</p>
            <button
              v-for="q in exampleQuestions"
              :key="q"
              @click="askQuestion(q)"
              class="example-btn"
            >
              {{ q }}
            </button>
          </div>
        </div>

        <!-- Messages -->
        <div v-for="(msg, idx) in messages" :key="idx" class="message-wrapper" :class="msg.role">

          <!-- User message -->
          <div v-if="msg.role === 'user'" class="user-message">
            <div class="message-bubble user-bubble">{{ msg.content }}</div>
            <div class="avatar user-avatar">Vous</div>
          </div>

          <!-- Assistant message -->
          <div v-else class="assistant-message">
            <div class="avatar ai-avatar">IA</div>
            <div class="assistant-content">

              <!-- Answer -->
              <div class="message-bubble ai-bubble">
                <div v-if="msg.loading" class="thinking">
                  <span class="dot"></span><span class="dot"></span><span class="dot"></span>
                  <span class="thinking-text">Analyse en cours…</span>
                </div>
                <div v-else class="answer-text">{{ msg.content }}</div>
              </div>

              <!-- Sources -->
              <div v-if="msg.sources && msg.sources.length > 0" class="sources-panel">
                <p class="sources-title">
                  📚 Sources utilisées ({{ msg.sources.length }} document{{ msg.sources.length > 1 ? 's' : '' }})
                </p>
                <div class="sources-grid">
                  <div
                    v-for="(src, sIdx) in msg.sources"
                    :key="sIdx"
                    class="source-card"
                  >
                    <div class="source-header">
                      <span class="source-num">{{ sIdx + 1 }}</span>
                      <div class="source-info">
                        <p class="source-title">{{ src.title }}</p>
                        <div class="source-meta">
                          <span v-if="src.category" class="source-cat">{{ src.category }}</span>
                          <span v-if="src.documentDate" class="source-date">
                            📅 {{ formatDate(src.documentDate) }}
                          </span>
                          <span class="source-score">
                            {{ Math.round(src.relevanceScore * 100) }}% pertinent
                          </span>
                        </div>
                      </div>
                      <a
                        :href="`/api/documents/${src.documentId}/download`"
                        target="_blank"
                        class="source-download"
                        title="Télécharger"
                      >
                        <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                            d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"/>
                        </svg>
                      </a>
                    </div>
                    <div v-if="src.excerpt" class="source-excerpt">
                      "{{ src.excerpt }}"
                    </div>
                  </div>
                </div>

                <!-- Timing info -->
                <p class="rag-timing" v-if="msg.searchTimeMs">
                  ⚡ Réponse générée en {{ msg.searchTimeMs }}ms
                  · {{ msg.totalDocs }} document(s) dans la base
                </p>
              </div>

            </div>
          </div>
        </div>
      </div>

      <!-- Input area -->
      <div class="input-area">
        <!-- Filter bar (collapsible) -->
        <div v-if="showFilters" class="filter-bar">
          <select v-model="filters.category" class="filter-select">
            <option value="">Toutes les catégories</option>
            <option value="Invoice">Facture</option>
            <option value="Contract">Contrat</option>
            <option value="Report">Rapport</option>
            <option value="Letter">Lettre</option>
            <option value="Memo">Mémo</option>
          </select>
          <select v-model="filters.language" class="filter-select">
            <option value="fr">Réponse en français</option>
            <option value="en">Respond in English</option>
            <option value="ar">أجب بالعربية</option>
          </select>
          <input v-model="filters.fromDate" type="date" class="filter-input" placeholder="Depuis" />
          <input v-model="filters.toDate"   type="date" class="filter-input" placeholder="Jusqu'à" />
        </div>

        <div class="input-row">
          <button @click="showFilters = !showFilters" class="filter-toggle" :class="{ active: showFilters }">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2a1 1 0 01-.293.707L13 13.414V19a1 1 0 01-.553.894l-4 2A1 1 0 017 21v-7.586L3.293 6.707A1 1 0 013 6V4z"/>
            </svg>
          </button>

          <textarea
            v-model="userInput"
            @keydown.enter.exact.prevent="sendMessage"
            @keydown.enter.shift.exact="userInput += '\n'"
            placeholder="Posez votre question… (Entrée pour envoyer, Maj+Entrée pour nouvelle ligne)"
            class="message-input"
            rows="2"
            :disabled="loading"
          ></textarea>

          <button
            @click="sendMessage"
            :disabled="loading || !userInput.trim()"
            class="send-btn"
          >
            <svg v-if="!loading" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8"/>
            </svg>
            <svg v-else class="spinner" fill="none" viewBox="0 0 24 24">
              <circle class="spinner-bg" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
              <path class="spinner-path" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
            </svg>
          </button>
        </div>
      </div>
    </main>
  </div>
</template>

<script setup>
import { ref, nextTick, reactive } from 'vue'
import { format } from 'date-fns'
import { logger } from '../logger.js'

const messages     = ref([])
const userInput    = ref('')
const loading      = ref(false)
const messagesArea = ref(null)
const showFilters  = ref(false)
const modelInfo    = ref('llama3.2 via Ollama')

const filters = reactive({
  category: '',
  language: 'fr',
  fromDate: '',
  toDate: ''
})

const exampleQuestions = [
  'Quels contrats ont été signés en 2024 ?',
  'Résume les factures du mois dernier',
  'Trouve les documents relatifs au projet X',
  'Quels rapports mentionnent des problèmes ?'
]

const formatDate = (d) => {
  try { return format(new Date(d), 'dd/MM/yyyy') }
  catch { return d }
}

const scrollToBottom = async () => {
  await nextTick()
  if (messagesArea.value)
    messagesArea.value.scrollTop = messagesArea.value.scrollHeight
}

const askQuestion = (q) => {
  logger.info(`Example question clicked: "${q}"`)
  userInput.value = q
  sendMessage()
}

const sendMessage = async () => {
  const query = userInput.value.trim()
  if (!query || loading.value) return

  logger.startFlow('rag', `Query: "${query}"`)
  logger.step('rag', 'Active filters', {
    language: filters.language,
    category: filters.category || 'none',
    fromDate: filters.fromDate || 'none',
    toDate:   filters.toDate   || 'none'
  })

  // Add user message
  messages.value.push({ role: 'user', content: query })
  userInput.value = ''

  // Add placeholder for AI response
  const aiMsg = {
    role: 'assistant',
    content: '',
    loading: true,
    sources: [],
    searchTimeMs: 0,
    totalDocs: 0
  }
  messages.value.push(aiMsg)
  await scrollToBottom()

  loading.value = true

  try {
    const token = localStorage.getItem('ged_token')

    const body = {
      query,
      language: filters.language || 'fr'
    }
    if (filters.category) body.categories = [filters.category]
    if (filters.fromDate)  body.fromDate   = filters.fromDate
    if (filters.toDate)    body.toDate     = filters.toDate

    logger.step('rag', 'Sending request to /api/rag/ask', body)

    const response = await fetch('/api/rag/ask', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { 'Authorization': `Bearer ${token}` } : {})
      },
      body: JSON.stringify(body)
    })

    logger.response('POST', '/api/rag/ask', response.status)

    if (response.ok) {
      const data = await response.json()

      logger.success('rag', `Answer received (${data.answer?.length ?? 0} chars)`, {
        model:        data.modelUsed,
        searchTimeMs: data.searchTimeMs,
        sourcesCount: data.sources?.length ?? 0,
        totalDocs:    data.totalDocumentsSearched
      })

      if (data.sources?.length) {
        logger.step('rag', `Sources used (${data.sources.length})`,
          data.sources.map((s, i) => `${i + 1}. ${s.title} [score=${(s.relevanceScore * 100).toFixed(0)}%]`)
        )
      } else {
        logger.warn('rag', 'No source documents were returned with the answer')
      }

      aiMsg.content       = data.answer
      aiMsg.sources       = data.sources || []
      aiMsg.searchTimeMs  = data.searchTimeMs
      aiMsg.totalDocs     = data.totalDocumentsSearched
      aiMsg.loading       = false

      if (data.modelUsed) {
        modelInfo.value = data.modelUsed
        logger.step('rag', `LLM model: ${data.modelUsed}`)
      }

      logger.endFlow('rag', `Done in ${data.searchTimeMs}ms`)

    } else {
      const errText = await response.text().catch(() => '(no body)')
      logger.error('rag', `API returned ${response.status}`, errText)
      aiMsg.content = 'Une erreur est survenue. Veuillez réessayer.'
      aiMsg.loading = false
      logger.endFlow('rag', `Failed (HTTP ${response.status})`)
    }

  } catch (err) {
    logger.error('rag', 'Network error — backend may be down', err)
    aiMsg.content = 'Impossible de contacter le service IA. Vérifiez que le backend est démarré.'
    aiMsg.loading = false
    logger.endFlow('rag', 'Network error')
  } finally {
    loading.value = false
    await scrollToBottom()
  }
}
</script>

<style scoped>
.rag-container {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: linear-gradient(135deg, #f8fafc 0%, #e0f2fe 50%, #ddd6fe 100%);
}

.rag-header {
  background: rgba(255,255,255,0.9);
  backdrop-filter: blur(12px);
  border-bottom: 1px solid #e5e7eb;
  padding: 0.75rem 1.5rem;
  flex-shrink: 0;
}

.header-content {
  max-width: 900px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.back-btn {
  display: flex;
  align-items: center;
  padding: 0.4rem;
  border-radius: 8px;
  color: #6b7280;
  text-decoration: none;
  transition: background 0.2s;
}

.back-btn:hover { background: #f3f4f6; }
.back-btn svg { width: 20px; height: 20px; }

.ai-logo { font-size: 2rem; }

.header-title {
  font-size: 1.25rem;
  font-weight: 700;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.header-subtitle { font-size: 0.8rem; color: #6b7280; }

.model-badge {
  font-size: 0.75rem;
  background: #e0e7ff;
  color: #4338ca;
  padding: 0.25rem 0.75rem;
  border-radius: 9999px;
  font-weight: 500;
}

.chat-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  max-width: 900px;
  width: 100%;
  margin: 0 auto;
  padding: 0 1rem;
  overflow: hidden;
}

.messages-area {
  flex: 1;
  overflow-y: auto;
  padding: 1.5rem 0;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

/* Welcome */
.welcome-panel {
  text-align: center;
  padding: 3rem 2rem;
}

.welcome-icon { font-size: 4rem; margin-bottom: 1rem; }

.welcome-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #111827;
  margin-bottom: 0.75rem;
}

.welcome-text { color: #6b7280; max-width: 500px; margin: 0 auto 2rem; }

.example-questions { display: flex; flex-direction: column; gap: 0.75rem; max-width: 500px; margin: 0 auto; }

.example-label { font-size: 0.875rem; font-weight: 600; color: #374151; margin-bottom: 0.25rem; }

.example-btn {
  padding: 0.625rem 1rem;
  background: white;
  border: 2px solid #e5e7eb;
  border-radius: 10px;
  color: #374151;
  cursor: pointer;
  text-align: left;
  font-size: 0.9rem;
  transition: all 0.2s;
}

.example-btn:hover {
  border-color: #3b82f6;
  background: #eff6ff;
  color: #1d4ed8;
}

/* Messages */
.user-message {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  flex-direction: row-reverse;
}

.assistant-message {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
}

.assistant-content { flex: 1; display: flex; flex-direction: column; gap: 0.75rem; }

.avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.7rem;
  font-weight: 700;
  flex-shrink: 0;
}

.user-avatar { background: linear-gradient(135deg, #2563eb, #4f46e5); color: white; }
.ai-avatar   { background: linear-gradient(135deg, #059669, #10b981); color: white; }

.message-bubble {
  padding: 0.875rem 1.125rem;
  border-radius: 16px;
  max-width: 100%;
  line-height: 1.6;
}

.user-bubble {
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  color: white;
  border-bottom-right-radius: 4px;
  align-self: flex-end;
}

.ai-bubble {
  background: white;
  border: 1px solid #e5e7eb;
  color: #111827;
  border-bottom-left-radius: 4px;
  box-shadow: 0 1px 3px rgba(0,0,0,0.06);
  white-space: pre-wrap;
}

/* Thinking animation */
.thinking {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.dot {
  width: 8px;
  height: 8px;
  background: #9ca3af;
  border-radius: 50%;
  animation: bounce 1.2s infinite;
}

.dot:nth-child(2) { animation-delay: 0.2s; }
.dot:nth-child(3) { animation-delay: 0.4s; }

@keyframes bounce {
  0%, 80%, 100% { transform: scale(0); }
  40%           { transform: scale(1); }
}

.thinking-text { font-size: 0.875rem; color: #6b7280; margin-left: 0.5rem; }

/* Sources panel */
.sources-panel {
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 12px;
  padding: 1rem;
}

.sources-title { font-size: 0.875rem; font-weight: 700; color: #374151; margin-bottom: 0.75rem; }

.sources-grid { display: flex; flex-direction: column; gap: 0.75rem; }

.source-card {
  background: #f9fafb;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  padding: 0.75rem;
}

.source-header { display: flex; align-items: flex-start; gap: 0.75rem; }

.source-num {
  width: 24px;
  height: 24px;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  color: white;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  font-weight: 700;
  flex-shrink: 0;
}

.source-info { flex: 1; }

.source-title { font-weight: 600; color: #111827; font-size: 0.9rem; margin-bottom: 0.25rem; }

.source-meta { display: flex; flex-wrap: wrap; gap: 0.5rem; }

.source-cat {
  font-size: 0.75rem;
  background: #dbeafe;
  color: #1d4ed8;
  padding: 0.125rem 0.5rem;
  border-radius: 9999px;
}

.source-date { font-size: 0.75rem; color: #6b7280; }

.source-score {
  font-size: 0.75rem;
  background: #d1fae5;
  color: #065f46;
  padding: 0.125rem 0.5rem;
  border-radius: 9999px;
}

.source-download {
  color: #6b7280;
  flex-shrink: 0;
  padding: 0.25rem;
  border-radius: 6px;
  transition: all 0.2s;
}

.source-download:hover { background: #e5e7eb; color: #111827; }
.source-download svg { width: 18px; height: 18px; }

.source-excerpt {
  margin-top: 0.5rem;
  font-size: 0.8rem;
  color: #6b7280;
  font-style: italic;
  border-left: 3px solid #dbeafe;
  padding-left: 0.75rem;
  line-height: 1.5;
}

.rag-timing { font-size: 0.75rem; color: #9ca3af; margin-top: 0.75rem; }

/* Input area */
.input-area {
  padding: 1rem 0 1.5rem;
  flex-shrink: 0;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  padding: 0.75rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 12px;
}

.filter-select,
.filter-input {
  padding: 0.375rem 0.75rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  font-size: 0.85rem;
  outline: none;
}

.filter-select:focus,
.filter-input:focus {
  border-color: #3b82f6;
}

.input-row {
  display: flex;
  gap: 0.75rem;
  align-items: flex-end;
  background: white;
  border: 2px solid #e5e7eb;
  border-radius: 16px;
  padding: 0.5rem;
  transition: border-color 0.2s;
}

.input-row:focus-within { border-color: #3b82f6; }

.filter-toggle {
  width: 40px;
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: 10px;
  background: #f3f4f6;
  color: #6b7280;
  cursor: pointer;
  flex-shrink: 0;
  transition: all 0.2s;
}

.filter-toggle svg { width: 18px; height: 18px; }
.filter-toggle.active { background: #dbeafe; color: #1d4ed8; }
.filter-toggle:hover  { background: #e5e7eb; }

.message-input {
  flex: 1;
  border: none;
  outline: none;
  resize: none;
  font-size: 0.95rem;
  line-height: 1.5;
  font-family: inherit;
  padding: 0.375rem 0.25rem;
  background: transparent;
}

.send-btn {
  width: 44px;
  height: 44px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  color: white;
  border: none;
  border-radius: 12px;
  cursor: pointer;
  flex-shrink: 0;
  transition: all 0.2s;
}

.send-btn svg   { width: 20px; height: 20px; }
.send-btn:hover:not(:disabled) { transform: scale(1.05); }
.send-btn:disabled { opacity: 0.5; cursor: not-allowed; }

.spinner { width: 20px; height: 20px; animation: spin 1s linear infinite; }
.spinner-bg   { opacity: 0.25; }
.spinner-path { opacity: 0.75; }

@keyframes spin {
  from { transform: rotate(0deg); }
  to   { transform: rotate(360deg); }
}
</style>
