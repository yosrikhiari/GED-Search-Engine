<template>
  <div class="login-page">
    <div class="login-container">
      <!-- Left side - Branding -->
      <div class="login-branding">
        <div class="branding-content">
          <div class="brand-logo">
            <div class="logo-mark">
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
            <span class="logo-text">GED Elise</span>
          </div>
          
          <h1 class="brand-title">
            Gestion Électronique des Documents
          </h1>
          <p class="brand-description">
            Recherchez, organisez et partagez vos documents en toute simplicité avec notre moteur de recherche intelligent.
          </p>
          
          <div class="feature-list">
            <div class="feature-item">
              <div class="feature-icon">
                <svg
                  width="20"
                  height="20"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                >
                  <circle
                    cx="11"
                    cy="11"
                    r="8"
                  />
                  <path d="M21 21l-4.35-4.35" />
                </svg>
              </div>
              <div class="feature-text">
                <strong>Recherche intelligente</strong>
                <span>Langage naturel et NLP</span>
              </div>
            </div>
            <div class="feature-item">
              <div class="feature-icon">
                <svg
                  width="20"
                  height="20"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                >
                  <path d="M12 2L2 7l10 5 10-5-10-5z" />
                  <path d="M2 17l10 5 10-5" />
                  <path d="M2 12l10 5 10-5" />
                </svg>
              </div>
              <div class="feature-text">
                <strong>Indexation OCR</strong>
                <span>Extraction automatique du texte</span>
              </div>
            </div>
            <div class="feature-item">
              <div class="feature-icon">
                <svg
                  width="20"
                  height="20"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                >
                  <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
                </svg>
              </div>
              <div class="feature-text">
                <strong>Sécurité avancée</strong>
                <span>Contrôle d'accès granulaire</span>
              </div>
            </div>
          </div>
        </div>
        
        <div class="branding-decoration">
          <div class="decoration-circle circle-1" />
          <div class="decoration-circle circle-2" />
          <div class="decoration-circle circle-3" />
        </div>
      </div>

      <!-- Right side - Login form -->
      <div class="login-form-section">
        <div class="login-card">
          <div class="login-header">
            <h2 class="login-title">
              Bon retour
            </h2>
            <p class="login-subtitle">
              Connectez-vous à votre compte
            </p>
          </div>

          <!-- Error message -->
          <div
            v-if="error"
            class="error-banner"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <circle
                cx="12"
                cy="12"
                r="10"
              />
              <line
                x1="12"
                y1="8"
                x2="12"
                y2="12"
              />
              <line
                x1="12"
                y1="16"
                x2="12.01"
                y2="16"
              />
            </svg>
            {{ error }}
          </div>

          <!-- Form -->
          <form
            class="login-form"
            @submit.prevent="handleLogin"
          >
            <div class="form-group">
              <label class="form-label">Nom d'utilisateur</label>
              <div class="input-wrapper">
                <svg
                  class="input-icon"
                  width="18"
                  height="18"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                >
                  <path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2" />
                  <circle
                    cx="12"
                    cy="7"
                    r="4"
                  />
                </svg>
                <input
                  v-model="username"
                  type="text"
                  class="form-input"
                  placeholder="Entrez votre nom d'utilisateur"
                  autocomplete="username"
                  required
                >
              </div>
            </div>

            <div class="form-group">
              <label class="form-label">Mot de passe</label>
              <div class="input-wrapper">
                <svg
                  class="input-icon"
                  width="18"
                  height="18"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                >
                  <rect
                    x="3"
                    y="11"
                    width="18"
                    height="11"
                    rx="2"
                    ry="2"
                  />
                  <path d="M7 11V7a5 5 0 0110 0v4" />
                </svg>
                <input
                  v-model="password"
                  :type="showPassword ? 'text' : 'password'"
                  class="form-input"
                  placeholder="Entrez votre mot de passe"
                  autocomplete="current-password"
                  required
                >
                <button
                  type="button"
                  class="password-toggle"
                  @click="showPassword = !showPassword"
                >
                  <svg
                    v-if="!showPassword"
                    width="18"
                    height="18"
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
                  <svg
                    v-else
                    width="18"
                    height="18"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="2"
                  >
                    <path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24" />
                    <line
                      x1="1"
                      y1="1"
                      x2="23"
                      y2="23"
                    />
                  </svg>
                </button>
              </div>
            </div>

            <button
              type="submit"
              :disabled="loading"
              class="login-btn"
            >
              <span v-if="!loading">Se connecter</span>
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
                Connexion en cours...
              </span>
            </button>
          </form>

          <div class="login-footer">
            <div class="demo-credentials">
              <span class="demo-label">Compte de démonstration</span>
              <code>admin</code>
              <span class="demo-sep">/</span>
              <code>Admin@1234</code>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { auth } from '../api.js'

const router = useRouter()
const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')
const showPassword = ref(false)

const handleLogin = async () => {
  loading.value = true
  error.value = ''
  try {
    const data = await auth.login(username.value, password.value)
    if (data && data.username) {
      localStorage.setItem('ged_user', JSON.stringify({
        username: data.username,
        fullName: data.fullName,
        role: data.role
      }))
      router.push('/')
    } else {
      error.value = 'Réponse invalide du serveur'
    }
  } catch (e) {
    if (e.userMessage) {
      error.value = e.userMessage
    } else if (e.message && e.message !== 'Failed to fetch') {
      error.value = e.message
    } else {
      error.value = 'Impossible de contacter le serveur'
    }
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-page {
  min-height: 100vh;
  background: var(--color-bg);
}

.login-container {
  display: grid;
  grid-template-columns: 1fr 1fr;
  min-height: 100vh;
}

@media (max-width: 968px) {
  .login-container {
    grid-template-columns: 1fr;
  }
  
  .login-branding {
    display: none;
  }
}

/* ── Branding Side ── */
.login-branding {
  background: linear-gradient(135deg, #1e3a5f 0%, #2563eb 50%, #4f46e5 100%);
  position: relative;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 3rem;
}

.branding-content {
  position: relative;
  z-index: 1;
  max-width: 480px;
}

.brand-logo {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 2.5rem;
}

.logo-mark {
  width: 52px;
  height: 52px;
  background: rgba(255, 255, 255, 0.15);
  border-radius: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  backdrop-filter: blur(10px);
}

.logo-mark svg {
  width: 28px;
  height: 28px;
  color: white;
}

.logo-text {
  font-size: 1.5rem;
  font-weight: 700;
  color: white;
  letter-spacing: -0.02em;
}

.brand-title {
  font-size: 2.5rem;
  font-weight: 800;
  color: white;
  line-height: 1.2;
  margin-bottom: 1.25rem;
  letter-spacing: -0.02em;
}

.brand-description {
  font-size: 1.1rem;
  color: rgba(255, 255, 255, 0.85);
  line-height: 1.7;
  margin-bottom: 2.5rem;
}

.feature-list {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.feature-item {
  display: flex;
  align-items: flex-start;
  gap: 1rem;
}

.feature-icon {
  width: 40px;
  height: 40px;
  background: rgba(255, 255, 255, 0.15);
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.feature-icon svg {
  color: white;
}

.feature-text {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.feature-text strong {
  font-size: 1rem;
  font-weight: 600;
  color: white;
}

.feature-text span {
  font-size: 0.875rem;
  color: rgba(255, 255, 255, 0.7);
}

.branding-decoration {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.decoration-circle {
  position: absolute;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.05);
}

.circle-1 {
  width: 400px;
  height: 400px;
  top: -100px;
  right: -100px;
}

.circle-2 {
  width: 300px;
  height: 300px;
  bottom: -50px;
  left: -50px;
}

.circle-3 {
  width: 200px;
  height: 200px;
  bottom: 30%;
  right: 10%;
}

/* ── Form Side ── */
.login-form-section {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem;
  background: var(--color-bg);
}

.login-card {
  width: 100%;
  max-width: 420px;
  background: var(--color-surface);
  border-radius: 20px;
  padding: 2.5rem;
  box-shadow: 0 20px 40px -10px rgba(0, 0, 0, 0.1);
  border: 1px solid var(--color-border);
}

.login-header {
  text-align: center;
  margin-bottom: 2rem;
}

.login-title {
  font-size: 1.75rem;
  font-weight: 700;
  color: var(--color-text-primary);
  margin-bottom: 0.5rem;
}

.login-subtitle {
  font-size: 0.95rem;
  color: var(--color-text-secondary);
}

.error-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  background: var(--color-danger-light);
  color: var(--color-danger);
  border-radius: 12px;
  padding: 0.875rem 1rem;
  font-size: 0.875rem;
  font-weight: 500;
  margin-bottom: 1.5rem;
  animation: shake 0.5s ease;
}

@keyframes shake {
  0%, 100% { transform: translateX(0); }
  25% { transform: translateX(-5px); }
  75% { transform: translateX(5px); }
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.form-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text-primary);
}

.input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.input-icon {
  position: absolute;
  left: 1rem;
  color: var(--color-text-muted);
  pointer-events: none;
}

.form-input {
  width: 100%;
  padding: 0.875rem 1rem 0.875rem 2.75rem;
  border: 2px solid var(--color-border);
  border-radius: 12px;
  font-size: 0.95rem;
  outline: none;
  transition: all 0.2s ease;
  background: var(--color-surface);
  color: var(--color-text-primary);
}

.form-input:focus {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 4px rgba(37, 99, 235, 0.1);
}

.form-input::placeholder {
  color: var(--color-text-muted);
}

.password-toggle {
  position: absolute;
  right: 0.75rem;
  background: none;
  border: none;
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 0.25rem;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: color 0.2s;
}

.password-toggle:hover {
  color: var(--color-text-primary);
}

.login-btn {
  width: 100%;
  padding: 1rem;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  color: white;
  border: none;
  border-radius: 12px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
  margin-top: 0.5rem;
  display: flex;
  align-items: center;
  justify-content: center;
}

.login-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 10px 20px -5px rgba(37, 99, 235, 0.4);
}

.login-btn:disabled {
  opacity: 0.7;
  cursor: not-allowed;
}

.loading-text {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
}

.spinner {
  width: 20px;
  height: 20px;
  animation: spin 1s linear infinite;
}

.spinner-bg {
  opacity: 0.3;
}

.spinner-path {
  opacity: 0.8;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.login-footer {
  margin-top: 2rem;
  padding-top: 1.5rem;
  border-top: 1px solid var(--color-border);
}

.demo-credentials {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.demo-label {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  margin-right: 0.5rem;
}

.demo-credentials code {
  background: var(--color-surface-hover);
  padding: 0.25rem 0.75rem;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-primary);
  font-family: 'SF Mono', Monaco, Consolas, monospace;
}

.demo-sep {
  color: var(--color-text-muted);
}
</style>
