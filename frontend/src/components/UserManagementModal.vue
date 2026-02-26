<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal">
      <div class="modal-header">
        <h2 class="modal-title">
          <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
              d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z"/>
          </svg>
          Gestion des utilisateurs
        </h2>
        <button @click="$emit('close')" class="close-btn">✕</button>
      </div>

      <div class="modal-body">
        <!-- Error / success banners -->
        <div v-if="error"   class="banner error">{{ error }}</div>
        <div v-if="success" class="banner success">{{ success }}</div>

        <!-- Add user form -->
        <div class="add-form">
          <h3 class="section-title">Créer un utilisateur</h3>
          <div class="form-grid">
            <input v-model="form.username" placeholder="Nom d'utilisateur *" class="input" />
            <input v-model="form.password" type="password" placeholder="Mot de passe *" class="input" />
            <input v-model="form.fullName" placeholder="Nom complet" class="input" />
            <input v-model="form.email"    placeholder="Email" class="input" />
            <select v-model="form.role" class="input">
              <option value="User">Utilisateur</option>
              <option value="Manager">Responsable</option>
              <option value="ReadOnly">Lecture seule</option>
              <option value="Admin">Administrateur</option>
            </select>
          </div>
          <button @click="createUser" :disabled="saving" class="btn-primary">
            {{ saving ? 'Création...' : '+ Créer l\'utilisateur' }}
          </button>
        </div>

        <!-- Users table -->
        <div class="users-section">
          <h3 class="section-title">Utilisateurs existants</h3>

          <div v-if="loading" class="loading-state">Chargement…</div>

          <div v-else class="table-wrapper">
            <table class="users-table">
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
                <tr v-for="u in users" :key="u.id">
                  <td>
                    <div class="user-cell">
                      <div class="mini-avatar">{{ initials(u) }}</div>
                      <div>
                        <p class="cell-name">{{ u.fullName || u.username }}</p>
                        <p class="cell-sub">{{ u.username }}</p>
                      </div>
                    </div>
                  </td>
                  <td>
                    <span class="role-badge" :class="roleCss(u.role)">{{ roleLabel(u.role) }}</span>
                  </td>
                  <td>
                    <span class="status-badge" :class="u.isActive ? 'status-active' : 'status-inactive'">
                      {{ u.isActive ? 'Actif' : 'Désactivé' }}
                    </span>
                  </td>
                  <td class="cell-date">
                    {{ u.lastLoginAt ? formatDate(u.lastLoginAt) : '—' }}
                  </td>
                  <td>
                    <button
                      v-if="u.isActive"
                      @click="deactivate(u)"
                      class="btn-danger-sm"
                      title="Désactiver"
                    >
                      Désactiver
                    </button>
                    <span v-else class="cell-sub">Désactivé</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { auth } from '../api.js'

defineEmits(['close'])

const users   = ref([])
const loading = ref(false)
const saving  = ref(false)
const error   = ref('')
const success = ref('')

const form = ref({ username: '', password: '', fullName: '', email: '', role: 'User' })

const formatDate = (d) => {
  try { return new Date(d).toLocaleDateString('fr-FR', { day:'2-digit', month:'2-digit', year:'numeric' }) }
  catch { return d }
}

const initials = (u) => (u.fullName || u.username).split(' ').map(n => n[0]).join('').toUpperCase().slice(0,2)

const roleLabel = (r) => ({ Admin: 'Admin', Manager: 'Responsable', User: 'Utilisateur', ReadOnly: 'Lecture seule' }[r] || r)

const roleCss = (r) => ({ Admin: 'badge-admin', Manager: 'badge-manager', User: 'badge-user', ReadOnly: 'badge-readonly' }[r] || '')

const fetchUsers = async () => {
  loading.value = true
  try {
    const res = await auth.getUsers()
    if (res.ok) users.value = await res.json()
  } catch { /* silent */ }
  finally { loading.value = false }
}

const createUser = async () => {
  error.value = success.value = ''
  if (!form.value.username || !form.value.password) {
    error.value = 'Nom d\'utilisateur et mot de passe obligatoires.'
    return
  }
  saving.value = true
  try {
    const res = await auth.createUser(form.value)
    if (res.ok) {
      success.value = `Utilisateur "${form.value.username}" créé avec succès.`
      form.value = { username: '', password: '', fullName: '', email: '', role: 'User' }
      await fetchUsers()
    } else {
      const err = await res.json()
      error.value = err.error || 'Erreur lors de la création.'
    }
  } catch { error.value = 'Erreur réseau.' }
  finally { saving.value = false }
}

const deactivate = async (u) => {
  if (!confirm(`Désactiver "${u.username}" ?`)) return
  try {
    const res = await auth.deactivateUser(u.id)
    if (res.ok) { success.value = `Utilisateur "${u.username}" désactivé.`; await fetchUsers() }
    else error.value = 'Erreur lors de la désactivation.'
  } catch { error.value = 'Erreur réseau.' }
}

onMounted(fetchUsers)
</script>

<style scoped>
.modal-overlay {
  position: fixed; inset: 0;
  background: rgba(0,0,0,0.5);
  backdrop-filter: blur(4px);
  z-index: 500;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
}

.modal {
  background: white;
  border-radius: 16px;
  width: 100%;
  max-width: 780px;
  max-height: 85vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 25px 50px rgba(0,0,0,0.2);
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid #e5e7eb;
}

.modal-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 1.125rem;
  font-weight: 700;
  color: #111827;
}

.modal-title svg { width: 20px; height: 20px; color: #2563eb; }

.close-btn {
  background: none; border: none; font-size: 1.1rem; cursor: pointer;
  color: #9ca3af; padding: 0.25rem 0.5rem; border-radius: 6px;
}

.close-btn:hover { background: #f3f4f6; color: #111827; }

.modal-body { padding: 1.5rem; overflow-y: auto; display: flex; flex-direction: column; gap: 1.5rem; }

.banner { padding: 0.75rem 1rem; border-radius: 8px; font-size: 0.875rem; }
.banner.error   { background: #fef2f2; color: #dc2626; border: 1px solid #fecaca; }
.banner.success { background: #f0fdf4; color: #16a34a; border: 1px solid #bbf7d0; }

.section-title { font-size: 0.9rem; font-weight: 700; color: #374151; margin-bottom: 0.875rem; }

.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; margin-bottom: 1rem; }

.input {
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 8px;
  font-size: 0.875rem;
  outline: none;
}

.input:focus { border-color: #3b82f6; box-shadow: 0 0 0 2px rgba(59,130,246,0.1); }

.btn-primary {
  padding: 0.5rem 1.25rem;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  color: white;
  border: none;
  border-radius: 8px;
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.2s;
}

.btn-primary:disabled { opacity: 0.6; cursor: not-allowed; }

.loading-state { text-align: center; color: #9ca3af; padding: 2rem; }

.table-wrapper { overflow-x: auto; }

.users-table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }

.users-table th {
  background: #f9fafb;
  padding: 0.625rem 0.875rem;
  text-align: left;
  font-weight: 600;
  color: #6b7280;
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  border-bottom: 1px solid #e5e7eb;
}

.users-table td {
  padding: 0.75rem 0.875rem;
  border-bottom: 1px solid #f3f4f6;
  vertical-align: middle;
}

.user-cell { display: flex; align-items: center; gap: 0.6rem; }

.mini-avatar {
  width: 30px; height: 30px;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  border-radius: 50%;
  color: white; font-size: 0.7rem; font-weight: 700;
  display: flex; align-items: center; justify-content: center;
}

.cell-name { font-weight: 600; color: #111827; }
.cell-sub  { font-size: 0.8rem; color: #9ca3af; margin-top: 0.1rem; }
.cell-date { color: #6b7280; font-size: 0.8rem; }

.role-badge, .status-badge {
  font-size: 0.75rem; font-weight: 600;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
}

.badge-admin    { background: #fef3c7; color: #92400e; }
.badge-manager  { background: #dbeafe; color: #1d4ed8; }
.badge-user     { background: #d1fae5; color: #065f46; }
.badge-readonly { background: #f3f4f6; color: #6b7280; }

.status-active   { background: #d1fae5; color: #065f46; }
.status-inactive { background: #f3f4f6; color: #9ca3af; }

.btn-danger-sm {
  padding: 0.25rem 0.625rem;
  background: #fef2f2;
  color: #dc2626;
  border: 1px solid #fecaca;
  border-radius: 6px;
  font-size: 0.8rem;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-danger-sm:hover { background: #fee2e2; }
</style>
