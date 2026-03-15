<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal">

      <!-- Header -->
      <div class="modal-header">
        <div class="header-left">
          <div class="header-icon">🔐</div>
          <div>
            <h2 class="modal-title">Gestion des accès</h2>
            <p class="modal-subtitle">Groupes de documents · Droits utilisateurs · Rôles</p>
          </div>
        </div>
        <button @click="$emit('close')" class="close-btn">✕</button>
      </div>

      <!-- Tabs -->
      <div class="tab-bar">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          @click="activeTab = tab.id"
          class="tab-btn"
          :class="{ active: activeTab === tab.id }"
        >
          <span class="tab-icon">{{ tab.icon }}</span>
          {{ tab.label }}
          <span v-if="tab.badge" class="tab-badge">{{ tab.badge }}</span>
        </button>
      </div>

      <!-- Body -->
      <div class="modal-body">

        <!-- GLOBAL BANNERS -->
        <div v-if="globalError"   class="banner error">{{ globalError }}</div>
        <div v-if="globalSuccess" class="banner success">{{ globalSuccess }}</div>

        <!-- ═══════════════════════════════════════════════════════
             TAB 1 — DOCUMENT GROUPS
        ═══════════════════════════════════════════════════════ -->
        <div v-if="activeTab === 'groups'">

          <!-- Group list -->
          <div v-if="!activeGroup" class="section">
            <div class="section-header">
              <h3 class="section-title">Groupes de documents</h3>
              <button @click="showCreateGroup = !showCreateGroup" class="btn-primary-sm">
                {{ showCreateGroup ? 'Annuler' : '+ Nouveau groupe' }}
              </button>
            </div>

            <!-- Create form -->
            <div v-if="showCreateGroup" class="create-form">
              <h4 class="form-title">Créer un groupe</h4>
              <div class="form-grid-3">
                <input v-model="groupForm.name"     placeholder="Nom du groupe *" class="input" />
                <input v-model="groupForm.category" placeholder="Catégorie (ex: RH, Finance…)" class="input" />
                <div class="icon-color-row">
                  <input v-model="groupForm.icon"  placeholder="Icône" class="input icon-input" maxlength="4" />
                  <input v-model="groupForm.color" type="color" class="input color-input" title="Couleur" />
                </div>
              </div>
              <textarea v-model="groupForm.description" placeholder="Description (optionnel)" class="input textarea" rows="2"></textarea>
              <button @click="createGroup" :disabled="saving" class="btn-primary">
                {{ saving ? 'Création…' : 'Créer le groupe' }}
              </button>
            </div>

            <!-- Groups list -->
            <div v-if="loadingGroups" class="loading">Chargement…</div>
            <div v-else-if="!groups.length" class="empty-state">
              Aucun groupe créé. Créez votre premier groupe pour commencer.
            </div>
            <div v-else class="groups-grid">
              <div
                v-for="g in groups" :key="g.id"
                class="group-card"
                :style="{ borderLeftColor: g.color || '#2563eb' }"
                @click="openGroup(g)"
              >
                <div class="group-card-top">
                  <div class="group-icon" :style="{ background: (g.color || '#2563eb') + '22' }">
                    {{ g.icon || '📁' }}
                  </div>
                  <div class="group-info">
                    <p class="group-name">{{ g.name }}</p>
                    <p class="group-category">{{ g.category || 'Sans catégorie' }}</p>
                  </div>
                  <svg class="chevron" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/>
                  </svg>
                </div>
                <p v-if="g.description" class="group-desc">{{ g.description }}</p>
                <div class="group-stats">
                  <span class="stat">📄 {{ g.documentCount }} document{{ g.documentCount !== 1 ? 's' : '' }}</span>
                  <span class="stat">👤 {{ g.userCount }} utilisateur{{ g.userCount !== 1 ? 's' : '' }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- ── Group detail view ── -->
          <div v-else class="section">
            <button @click="activeGroup = null; loadGroups()" class="back-btn">
              ← Retour aux groupes
            </button>

            <div class="group-detail-header" :style="{ borderLeftColor: activeGroup.color || '#2563eb' }">
              <div class="group-icon-lg" :style="{ background: (activeGroup.color || '#2563eb') + '22' }">
                {{ activeGroup.icon || '📁' }}
              </div>
              <div>
                <h3 class="group-detail-name">{{ activeGroup.name }}</h3>
                <p class="group-detail-meta">
                  {{ activeGroup.category || 'Sans catégorie' }}
                  <span v-if="activeGroup.description"> · {{ activeGroup.description }}</span>
                </p>
              </div>
              <button @click="confirmDeleteGroup(activeGroup)" class="btn-danger-sm ml-auto">
                Supprimer le groupe
              </button>
            </div>

            <!-- Sub-tabs -->
            <div class="subtab-bar">
              <button
                v-for="st in groupSubtabs" :key="st.id"
                @click="activeGroupSubtab = st.id"
                class="subtab-btn"
                :class="{ active: activeGroupSubtab === st.id }"
              >{{ st.label }}</button>
            </div>

            <!-- ══════════════════════════════════════════
                 Documents sub-tab — FIXED PICKER
            ══════════════════════════════════════════ -->
            <div v-if="activeGroupSubtab === 'docs'">
              <div class="subsection-header">
                <p class="subsection-title">
                  Documents dans ce groupe ({{ activeGroup.documents?.length || 0 }})
                </p>
                <button @click="toggleAddDocs" class="btn-primary-sm">
                  {{ showAddDocs ? 'Annuler' : '+ Ajouter des documents' }}
                </button>
              </div>

              <!-- ── NEW: Card-grid document picker with drag & drop ── -->
              <div v-if="showAddDocs" class="add-docs-panel">

                <!-- Toolbar -->
                <div class="picker-toolbar">
                  <div class="picker-search-wrap">
                    <svg class="picker-search-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                        d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>
                    </svg>
                    <input
                      v-model="docSearch"
                      @input="onPickerSearch"
                      placeholder="Rechercher un document…"
                      class="picker-search-input"
                    />
                    <button v-if="docSearch" @click="docSearch = ''; onPickerSearch()" class="picker-search-clear">✕</button>
                  </div>
                  <div class="picker-view-toggle">
                    <button @click="pickerView = 'grid'" :class="['pview-btn', { active: pickerView === 'grid' }]" title="Grille">⊞</button>
                    <button @click="pickerView = 'list'" :class="['pview-btn', { active: pickerView === 'list' }]" title="Liste">☰</button>
                  </div>
                  <span v-if="selectedDocIds.length" class="picker-selection-badge">
                    {{ selectedDocIds.length }} sélectionné(s)
                  </span>
                </div>

                <!-- Loading state -->
                <div v-if="loadingDocs" class="picker-loading">
                  <div class="spinner-ring-sm"></div>
                  Chargement des documents…
                </div>

                <!-- Drop zone hint when dragging -->
                <div
                  v-else-if="isDraggingOver"
                  class="drop-zone-overlay"
                  @dragover.prevent
                  @dragleave="isDraggingOver = false"
                  @drop.prevent="onDropZone"
                >
                  <div class="drop-zone-inner">
                    📂 Relâchez pour ajouter au groupe
                  </div>
                </div>

                <!-- GRID VIEW -->
                <div
                  v-else-if="pickerView === 'grid'"
                  class="doc-picker-grid"
                  @dragover.prevent="isDraggingOver = true"
                  @dragleave.self="isDraggingOver = false"
                >
                  <div
                    v-for="doc in filteredAvailableDocs"
                    :key="doc.id"
                    class="doc-card"
                    :class="{ selected: selectedDocIds.includes(doc.id) }"
                    draggable="true"
                    @dragstart="onDragStart(doc)"
                    @dragend="draggedDoc = null"
                    @click="toggleDocSelection(doc.id)"
                  >
                    <div class="doc-card-check" :class="{ checked: selectedDocIds.includes(doc.id) }">
                      <svg v-if="selectedDocIds.includes(doc.id)" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="3" d="M5 13l4 4L19 7"/>
                      </svg>
                    </div>
                    <div class="doc-card-icon">{{ getFileIcon(doc.contentType) }}</div>
                    <div class="doc-card-body">
                      <p class="doc-card-title">{{ doc.title }}</p>
                      <p class="doc-card-meta">{{ doc.category || '—' }}</p>
                      <p class="doc-card-file">{{ doc.fileName }}</p>
                    </div>
                    <div class="doc-card-drag-hint">⠿</div>
                  </div>

                  <div v-if="!filteredAvailableDocs.length && !loadingDocs" class="picker-empty">
                    <span style="font-size:2rem">🔍</span>
                    <p>{{ docSearch ? 'Aucun résultat pour "' + docSearch + '"' : 'Tous les documents sont déjà dans ce groupe.' }}</p>
                  </div>
                </div>

                <!-- LIST VIEW -->
                <div v-else class="doc-picker-list-view">
                  <label
                    v-for="doc in filteredAvailableDocs"
                    :key="doc.id"
                    class="doc-list-item"
                    :class="{ selected: selectedDocIds.includes(doc.id) }"
                    draggable="true"
                    @dragstart="onDragStart(doc)"
                    @dragend="draggedDoc = null"
                  >
                    <input type="checkbox" :value="doc.id" v-model="selectedDocIds" class="sr-only" />
                    <div class="doc-list-check" :class="{ checked: selectedDocIds.includes(doc.id) }">
                      <svg v-if="selectedDocIds.includes(doc.id)" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="3" d="M5 13l4 4L19 7"/>
                      </svg>
                    </div>
                    <span class="doc-list-icon">{{ getFileIcon(doc.contentType) }}</span>
                    <div class="doc-list-info">
                      <p class="doc-picker-title">{{ doc.title }}</p>
                      <p class="doc-picker-sub">{{ doc.category || '—' }} · {{ doc.fileName }}</p>
                    </div>
                    <span v-if="selectedDocIds.includes(doc.id)" class="check-mark">✓</span>
                  </label>
                  <p v-if="!filteredAvailableDocs.length" class="empty-picker">
                    {{ docSearch ? 'Aucun résultat.' : 'Aucun document disponible.' }}
                  </p>
                </div>

                <!-- Footer -->
                <div class="add-docs-footer">
                  <button @click="selectedDocIds = []" v-if="selectedDocIds.length" class="btn-ghost-sm">
                    Tout désélectionner
                  </button>
                  <select v-model="addDocsPermission" class="input input-sm">
                    <option value="Read">Lecture</option>
                    <option value="Write">Écriture</option>
                    <option value="FullControl">Contrôle total</option>
                  </select>
                  <button
                    @click="addDocsToGroup"
                    :disabled="!selectedDocIds.length || saving"
                    class="btn-primary"
                  >
                    {{ saving ? 'Ajout…' : `Ajouter (${selectedDocIds.length})` }}
                  </button>
                </div>
              </div>
              <!-- end add-docs-panel -->

              <!-- Documents already in group -->
              <div v-if="activeGroup.documents?.length" class="mini-table-wrap">
                <table class="mini-table">
                  <thead>
                    <tr>
                      <th>Document</th>
                      <th>Catégorie</th>
                      <th>Permission par défaut</th>
                      <th>Ajouté le</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="doc in activeGroup.documents" :key="doc.memberId">
                      <td>
                        <span class="doc-title-cell">{{ getFileIcon(doc.contentType) }} {{ doc.title }}</span>
                        <span class="doc-filename">{{ doc.fileName }}</span>
                      </td>
                      <td><span class="category-pill">{{ doc.category || '—' }}</span></td>
                      <td><span class="perm-badge" :class="permCss(doc.defaultPermission)">{{ permLabel(doc.defaultPermission) }}</span></td>
                      <td class="cell-date">{{ formatDate(doc.addedAt) }}</td>
                      <td>
                        <button @click="removeDocFromGroup(doc)" class="btn-icon-danger" title="Retirer">✕</button>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div v-else-if="!showAddDocs" class="empty-state">
                Aucun document dans ce groupe.
              </div>
            </div>
            <!-- end docs sub-tab -->

            <!-- Assignees sub-tab -->
            <div v-if="activeGroupSubtab === 'users'">
              <div class="subsection-header">
                <p class="subsection-title">Utilisateurs assignés ({{ activeGroup.assignments?.length || 0 }})</p>
                <button @click="showAssignUser = !showAssignUser" class="btn-primary-sm">
                  {{ showAssignUser ? 'Annuler' : '+ Assigner un utilisateur' }}
                </button>
              </div>

              <div v-if="showAssignUser" class="assign-form">
                <select v-model="assignForm.userId" class="input">
                  <option value="">Choisir un utilisateur…</option>
                  <option v-for="u in allUsers" :key="u.id" :value="u.id">
                    {{ u.fullName || u.username }} ({{ roleLabel(u.role) }})
                  </option>
                </select>
                <select v-model="assignForm.permission" class="input">
                  <option value="Read">Lecture</option>
                  <option value="Write">Écriture</option>
                  <option value="Delete">Suppression</option>
                  <option value="FullControl">Contrôle total</option>
                </select>
                <input v-model="assignForm.expiresAt" type="date" class="input" :min="today" title="Expiration (vide = permanent)" />
                <button @click="assignGroupToUser" :disabled="!assignForm.userId || saving" class="btn-primary">
                  {{ saving ? 'Assignation…' : 'Assigner le groupe' }}
                </button>
              </div>

              <div v-if="activeGroup.assignments?.length" class="mini-table-wrap">
                <table class="mini-table">
                  <thead>
                    <tr>
                      <th>Utilisateur</th><th>Rôle système</th><th>Permission</th>
                      <th>Assigné le</th><th>Expiration</th><th></th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="a in activeGroup.assignments" :key="a.assignmentId" :class="{ expired: !a.isActive }">
                      <td>
                        <div class="user-cell">
                          <div class="mini-avatar">{{ initials(a) }}</div>
                          <div>
                            <p class="cell-name">{{ a.fullName || a.username }}</p>
                            <p class="cell-sub">{{ a.username }}</p>
                          </div>
                        </div>
                      </td>
                      <td><span class="role-badge" :class="roleCss(a.userRole)">{{ roleLabel(a.userRole) }}</span></td>
                      <td><span class="perm-badge" :class="permCss(a.permission)">{{ permLabel(a.permission) }}</span></td>
                      <td class="cell-date">{{ formatDate(a.assignedAt) }}</td>
                      <td class="cell-date">
                        <span v-if="a.expiresAt" :class="{ 'text-red': !a.isActive }">
                          {{ formatDate(a.expiresAt) }}
                          <span v-if="!a.isActive" class="expired-tag">Expiré</span>
                        </span>
                        <span v-else class="perm-badge badge-permanent">Permanent</span>
                      </td>
                      <td>
                        <button @click="revokeAssignment(a)" class="btn-danger-sm">Révoquer</button>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div v-else class="empty-state">Aucun utilisateur assigné à ce groupe.</div>
            </div>
          </div>
        </div>

        <!-- ═══════════════════════════════════════════════════════
             TAB 2 — USER RIGHTS OVERVIEW
        ═══════════════════════════════════════════════════════ -->
        <div v-if="activeTab === 'rights'">
          <div class="section-header">
            <h3 class="section-title">Droits par utilisateur</h3>
            <input v-model="userSearch" placeholder="Rechercher…" class="input input-sm search-input" />
          </div>

          <div v-if="loadingRights" class="loading">Chargement…</div>
          <div v-else class="users-rights-list">
            <div
              v-for="u in filteredUsersRights" :key="u.userId"
              class="user-rights-card"
              :class="{ inactive: !u.isActive }"
            >
              <div class="user-rights-header" @click="toggleUserExpanded(u.userId)">
                <div class="user-cell">
                  <div class="mini-avatar" :class="roleCss(u.role)">{{ initialsFromUser(u) }}</div>
                  <div>
                    <p class="cell-name">{{ u.fullName || u.username }}</p>
                    <p class="cell-sub">{{ u.username }} · <span class="role-badge-inline" :class="roleCss(u.role)">{{ roleLabel(u.role) }}</span></p>
                  </div>
                </div>
                <div class="user-rights-summary">
                  <span class="stat">📦 {{ u.groups.length }} groupe{{ u.groups.length !== 1 ? 's' : '' }}</span>
                  <span class="stat">📄 {{ u.directGrants.length }} accès direct{{ u.directGrants.length !== 1 ? 's' : '' }}</span>
                  <span v-if="!u.isActive" class="expired-tag">Désactivé</span>
                </div>
                <svg class="chevron" :class="{ rotated: expandedUsers.includes(u.userId) }" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"/>
                </svg>
              </div>

              <div v-if="expandedUsers.includes(u.userId)" class="user-rights-detail">
                <div v-if="u.groups.length" class="rights-subsection">
                  <p class="rights-subsection-title">Groupes assignés</p>
                  <div class="group-tags">
                    <div
                      v-for="g in u.groups" :key="g.assignmentId"
                      class="group-tag"
                      :style="{ borderColor: g.groupColor || '#2563eb', background: (g.groupColor || '#2563eb') + '11' }"
                    >
                      <span>{{ g.groupIcon || '📁' }} {{ g.groupName }}</span>
                      <span class="perm-badge" :class="permCss(g.permission)">{{ permLabel(g.permission) }}</span>
                      <span class="stat">{{ g.documentCount }} docs</span>
                      <button @click="revokeGroupFromUser(u, g)" class="btn-icon-danger" title="Révoquer">✕</button>
                    </div>
                  </div>
                </div>

                <div v-if="u.directGrants.length" class="rights-subsection">
                  <p class="rights-subsection-title">Accès directs (hors groupes)</p>
                  <div class="mini-table-wrap">
                    <table class="mini-table">
                      <thead>
                        <tr><th>Document</th><th>Permission</th><th>Accordé le</th><th>Expiration</th><th></th></tr>
                      </thead>
                      <tbody>
                        <tr v-for="grant in u.directGrants" :key="grant.id">
                          <td>
                            <span class="doc-title-cell">
                              {{ allDocs.find(d => d.id === grant.documentId)?.title || '—' }}
                            </span>
                            <span class="doc-filename">{{ grant.documentId.slice(0, 8) }}…</span>
                          </td>
                          <td><span class="perm-badge" :class="permCss(grant.permission)">{{ permLabel(grant.permission) }}</span></td>
                          <td class="cell-date">{{ formatDate(grant.grantedAt) }}</td>
                          <td class="cell-date">
                            <span v-if="grant.expiresAt" :class="{ 'text-red': !grant.isActive }">{{ formatDate(grant.expiresAt) }}</span>
                            <span v-else class="perm-badge badge-permanent">Permanent</span>
                          </td>
                          <td>
                            <button @click="revokeDirectGrant(grant)" class="btn-danger-sm">Révoquer</button>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                </div>

                <p v-if="!u.groups.length && !u.directGrants.length" class="empty-state">
                  Aucun accès accordé à cet utilisateur.
                </p>
              </div>
            </div>
            <div v-if="!filteredUsersRights.length" class="empty-state">Aucun utilisateur trouvé.</div>
          </div>
        </div>

        <!-- ═══════════════════════════════════════════════════════
             TAB 3 — ROLES
        ═══════════════════════════════════════════════════════ -->
        <div v-if="activeTab === 'roles'">
          <div class="section-header">
            <h3 class="section-title">Rôles système</h3>
          </div>

          <div class="role-legend">
            <div v-for="r in roleDefs" :key="r.role" class="role-def-card" :class="'role-def-' + r.role.toLowerCase()">
              <div class="role-def-header">
                <span class="role-badge" :class="roleCss(r.role)">{{ roleLabel(r.role) }}</span>
              </div>
              <p class="role-def-desc">{{ r.description }}</p>
              <ul class="role-def-perms">
                <li v-for="p in r.permissions" :key="p">✓ {{ p }}</li>
              </ul>
            </div>
          </div>

          <div v-if="loadingUsers" class="loading">Chargement…</div>
          <div v-else class="mini-table-wrap">
            <table class="mini-table">
              <thead>
                <tr>
                  <th>Utilisateur</th><th>Rôle actuel</th><th>Statut</th>
                  <th>Dernière connexion</th><th>Changer le rôle</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="u in allUsers" :key="u.id">
                  <td>
                    <div class="user-cell">
                      <div class="mini-avatar" :class="roleCss(u.role)">{{ initials(u) }}</div>
                      <div>
                        <p class="cell-name">{{ u.fullName || u.username }}</p>
                        <p class="cell-sub">{{ u.username }}</p>
                      </div>
                    </div>
                  </td>
                  <td><span class="role-badge" :class="roleCss(u.role)">{{ roleLabel(u.role) }}</span></td>
                  <td>
                    <span class="status-badge" :class="u.isActive ? 'status-active' : 'status-inactive'">
                      {{ u.isActive ? 'Actif' : 'Désactivé' }}
                    </span>
                  </td>
                  <td class="cell-date">{{ u.lastLoginAt ? formatDate(u.lastLoginAt) : '—' }}</td>
                  <td>
                    <div class="role-change-row">
                      <select v-model="roleChanges[u.id]" class="input input-sm">
                        <option value="User">Utilisateur</option>
                        <option value="Manager">Responsable</option>
                        <option value="ReadOnly">Lecture seule</option>
                        <option value="Admin">Administrateur</option>
                      </select>
                      <button
                        @click="changeRole(u)"
                        :disabled="roleChanges[u.id] === u.role || saving"
                        class="btn-primary-sm"
                      >Appliquer</button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

      </div><!-- end modal-body -->
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, reactive, watch } from 'vue'
import { auth } from '../api.js'

// ── API helpers ────────────────────────────────────────────────────────────────
const apiFetch = async (path, opts = {}) => {
  const token = localStorage.getItem('ged_token')
  const headers = { ...(opts.headers || {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) }
  if (opts.body && typeof opts.body === 'string') headers['Content-Type'] = 'application/json'
  return fetch(path, { ...opts, headers })
}

const props = defineProps({
  initialTab: { type: String, default: 'groups' }
})
const emit = defineEmits(['close', 'saved'])

const groupsApi = {
  list:             ()          => apiFetch('/api/groups'),
  create:           (data)      => apiFetch('/api/groups', { method:'POST', body: JSON.stringify(data) }),
  get:              (id)        => apiFetch(`/api/groups/${id}`),
  delete:           (id)        => apiFetch(`/api/groups/${id}`, { method:'DELETE' }),
  addDocuments:     (id, data)  => apiFetch(`/api/groups/${id}/documents`, { method:'POST', body: JSON.stringify(data) }),
  removeDocument:   (id, mId)   => apiFetch(`/api/groups/${id}/documents/${mId}`, { method:'DELETE' }),
  assign:           (id, data)  => apiFetch(`/api/groups/${id}/assign`, { method:'POST', body: JSON.stringify(data) }),
  revokeAssignment: (id, aId)   => apiFetch(`/api/groups/${id}/assignments/${aId}`, { method:'DELETE' }),
  accessSummary:    ()          => apiFetch('/api/groups/users/access-summary'),
  revokeAcl:        (dId, aId)  => apiFetch(`/api/documents/${dId}/acl/${aId}`, { method:'DELETE' }),
  changeRole:       (uid, role) => apiFetch(`/api/groups/users/${uid}/role`, { method:'PATCH', body: JSON.stringify({ role }) }),
}

// ── State ──────────────────────────────────────────────────────────────────────
const activeTab         = ref(props.initialTab)
const activeGroupSubtab = ref('docs')
const globalError       = ref('')
const globalSuccess     = ref('')
const saving            = ref(false)

// Groups tab
const groups            = ref([])
const loadingGroups     = ref(false)
const activeGroup       = ref(null)
const showCreateGroup   = ref(false)
const showAddDocs       = ref(false)
const showAssignUser    = ref(false)
const docSearch         = ref('')
const selectedDocIds    = ref([])
const addDocsPermission = ref('Read')
const allDocs           = ref([])
const loadingDocs       = ref(false)
const pickerView        = ref('grid')  // 'grid' | 'list'

// Drag & drop state
const draggedDoc        = ref(null)
const isDraggingOver    = ref(false)

const groupForm  = ref({ name: '', description: '', color: '#2563eb', icon: '📁', category: '' })
const assignForm = ref({ userId: '', permission: 'Read', expiresAt: '' })

// Rights tab
const usersRights   = ref([])
const loadingRights = ref(false)
const userSearch    = ref('')
const expandedUsers = ref([])

// Roles tab
const allUsers     = ref([])
const loadingUsers = ref(false)
const roleChanges  = reactive({})

// ── Computed ──────────────────────────────────────────────────────────────────
const tabs = computed(() => [
  { id: 'groups', icon: '📦', label: 'Groupes', badge: groups.value.length || null },
  { id: 'rights', icon: '🔑', label: 'Droits',  badge: null },
  { id: 'roles',  icon: '👤', label: 'Rôles',   badge: null },
])

const groupSubtabs = [
  { id: 'docs',  label: 'Documents' },
  { id: 'users', label: 'Utilisateurs assignés' },
]

const today = computed(() => new Date().toISOString().split('T')[0])

const filteredUsersRights = computed(() =>
  usersRights.value.filter(u =>
    !userSearch.value ||
    u.username.toLowerCase().includes(userSearch.value.toLowerCase()) ||
    (u.fullName || '').toLowerCase().includes(userSearch.value.toLowerCase())
  )
)

const filteredAvailableDocs = computed(() => {
  const inGroup = new Set(activeGroup.value?.documents?.map(d => d.documentId) || [])
  const q = docSearch.value.toLowerCase()
  return allDocs.value.filter(d =>
    !inGroup.has(d.id) &&
    (!q || d.title.toLowerCase().includes(q) || d.fileName.toLowerCase().includes(q))
  )
})

// ── Helpers ───────────────────────────────────────────────────────────────────
const formatDate = (d) => {
  try { return new Date(d).toLocaleDateString('fr-FR', { day:'2-digit', month:'2-digit', year:'numeric' }) }
  catch { return d }
}

const getFileIcon = (contentType) => {
  if (!contentType) return '📄'
  if (contentType.includes('pdf'))   return '📕'
  if (contentType.includes('word') || contentType.includes('document')) return '📝'
  if (contentType.includes('sheet') || contentType.includes('excel'))   return '📊'
  if (contentType.includes('image')) return '🖼️'
  if (contentType.includes('text'))  return '📃'
  if (contentType.includes('audio')) return '🎵'
  if (contentType.includes('video')) return '🎬'
  return '📄'
}

const initials         = (u) => (u.fullName || u.username || '?').split(' ').map(n => n[0]).join('').toUpperCase().slice(0,2)
const initialsFromUser = (u) => (u.fullName || u.username || '?').split(' ').map(n => n[0]).join('').toUpperCase().slice(0,2)
const roleLabel = (r) => ({ Admin:'Administrateur', Manager:'Responsable', User:'Utilisateur', ReadOnly:'Lecture seule' }[r] || r)
const roleCss   = (r) => ({ Admin:'badge-admin', Manager:'badge-manager', User:'badge-user', ReadOnly:'badge-readonly' }[r] || '')
const permLabel = (p) => ({ Read:'Lecture', Write:'Écriture', Delete:'Suppression', FullControl:'Contrôle total' }[p] || p)
const permCss   = (p) => ({ Read:'perm-read', Write:'perm-write', Delete:'perm-delete', FullControl:'perm-full' }[p] || '')

const flash = (ok, msg) => {
  if (ok) { globalSuccess.value = msg; globalError.value = '' }
  else    { globalError.value = msg;   globalSuccess.value = '' }
  setTimeout(() => { globalSuccess.value = ''; globalError.value = '' }, 4000)
}

const roleDefs = [
  { role: 'Admin',    description: 'Accès complet à toutes les fonctionnalités.', permissions: ['Gestion des utilisateurs', 'Upload / suppression documents', 'Gestion des accès et groupes', 'Recherche et RAG'] },
  { role: 'Manager',  description: 'Peut gérer les documents mais pas les utilisateurs.', permissions: ['Upload et mise à jour documents', 'Lecture et téléchargement', 'Recherche et RAG'] },
  { role: 'User',     description: 'Peut uploader et lire les documents.', permissions: ['Upload de documents', 'Lecture des documents accessibles', 'Recherche'] },
  { role: 'ReadOnly', description: 'Lecture seule sur les documents autorisés.', permissions: ['Lecture des documents accessibles', 'Recherche'] },
]

// ── Data loading ──────────────────────────────────────────────────────────────
const loadGroups = async () => {
  loadingGroups.value = true
  try {
    const res = await groupsApi.list()
    if (res.ok) groups.value = await res.json()
  } catch { flash(false, 'Erreur réseau.') }
  finally { loadingGroups.value = false }
}

const loadUsers = async () => {
  loadingUsers.value = true
  try {
    const res = await auth.getUsers()
    if (res.ok) {
      allUsers.value = await res.json()
      allUsers.value.forEach(u => { roleChanges[u.id] = u.role })
    }
  } finally { loadingUsers.value = false }
}

const loadRights = async () => {
  loadingRights.value = true
  try {
    const res = await groupsApi.accessSummary()
    if (res.ok) usersRights.value = await res.json()
  } finally { loadingRights.value = false }
}

// ── FIX: Use POST /api/search/query instead of GET /api/documents ────────────
const loadAllDocs = async (query = '') => {
  loadingDocs.value = true
  try {
    const res = await apiFetch('/api/search/query', {
      method: 'POST',
      body: JSON.stringify({
        query:      query.trim() || '*',
        searchType: 0,
        page:       1,
        pageSize:   200,
      })
    })
    if (res.ok) {
      const data = await res.json()
      allDocs.value = data.documents || []
    } else {
      flash(false, 'Impossible de charger les documents.')
    }
  } catch (e) {
    console.error('[Picker] loadAllDocs error:', e)
    flash(false, 'Erreur réseau lors du chargement des documents.')
  } finally {
    loadingDocs.value = false
  }
}

// Debounced search inside picker
let _searchTimer = null
const onPickerSearch = () => {
  clearTimeout(_searchTimer)
  _searchTimer = setTimeout(() => loadAllDocs(docSearch.value), 300)
}

// ── Picker open/close ─────────────────────────────────────────────────────────
const toggleAddDocs = async () => {
  showAddDocs.value = !showAddDocs.value
  if (showAddDocs.value && !allDocs.value.length) {
    await loadAllDocs()
  }
}

// ── Drag & drop ───────────────────────────────────────────────────────────────
const onDragStart = (doc) => {
  draggedDoc.value = doc
  // select it too if not already
  if (!selectedDocIds.value.includes(doc.id)) {
    selectedDocIds.value = [...selectedDocIds.value, doc.id]
  }
}

const onDropZone = () => {
  isDraggingOver.value = false
  // addDocsToGroup will use selectedDocIds which already includes dragged doc
  addDocsToGroup()
}

const toggleDocSelection = (id) => {
  const idx = selectedDocIds.value.indexOf(id)
  if (idx >= 0) selectedDocIds.value.splice(idx, 1)
  else selectedDocIds.value.push(id)
}

// ── Groups actions ────────────────────────────────────────────────────────────
const createGroup = async () => {
  if (!groupForm.value.name) { flash(false, 'Le nom du groupe est obligatoire.'); return }
  saving.value = true
  try {
    const res = await groupsApi.create(groupForm.value)
    if (res.ok) {
      flash(true, `Groupe "${groupForm.value.name}" créé.`)
      groupForm.value = { name: '', description: '', color: '#2563eb', icon: '📁', category: '' }
      showCreateGroup.value = false
      emit('saved')
      await loadGroups()
    } else {
      const err = await res.json(); flash(false, err.error || 'Erreur lors de la création.')
    }
  } finally { saving.value = false }
}

const openGroup = async (g) => {
  activeGroupSubtab.value = 'docs'
  const res = await groupsApi.get(g.id)
  if (res.ok) activeGroup.value = await res.json()
  if (!allUsers.value.length) await loadUsers()
  showAddDocs.value = false
  showAssignUser.value = false
  selectedDocIds.value = []
  docSearch.value = ''
  // Load docs immediately so picker is ready
  await loadAllDocs()
}

const confirmDeleteGroup = async (g) => {
  if (!confirm(`Supprimer le groupe "${g.name}" ? Tous les accès seront révoqués.`)) return
  const res = await groupsApi.delete(g.id)
  if (res.ok) { flash(true, 'Groupe supprimé.'); emit('saved'); activeGroup.value = null; await loadGroups() }
  else flash(false, 'Erreur lors de la suppression.')
}

const addDocsToGroup = async () => {
  if (!selectedDocIds.value.length) return
  saving.value = true
  try {
    const res = await groupsApi.addDocuments(activeGroup.value.id, {
      documentIds:       selectedDocIds.value,
      defaultPermission: addDocsPermission.value
    })
    if (res.ok) {
      const d = await res.json()
      flash(true, d.message || 'Documents ajoutés.')
      selectedDocIds.value = []
      showAddDocs.value = false
      emit('saved')
      await openGroup(activeGroup.value)
    } else flash(false, 'Erreur lors de l\'ajout.')
  } finally { saving.value = false }
}

const removeDocFromGroup = async (doc) => {
  if (!confirm(`Retirer "${doc.title}" du groupe ?`)) return
  const res = await groupsApi.removeDocument(activeGroup.value.id, doc.memberId)
  if (res.ok) { flash(true, 'Document retiré.'); emit('saved'); await openGroup(activeGroup.value) }
  else flash(false, 'Erreur.')
}

const assignGroupToUser = async () => {
  if (!assignForm.value.userId) return
  saving.value = true
  try {
    const payload = {
      userId:     assignForm.value.userId,
      permission: assignForm.value.permission,
      expiresAt:  assignForm.value.expiresAt || null
    }
    const res = await groupsApi.assign(activeGroup.value.id, payload)
    if (res.ok) {
      flash(true, 'Groupe assigné avec succès.')
      assignForm.value = { userId: '', permission: 'Read', expiresAt: '' }
      showAssignUser.value = false
      emit('saved')
      await openGroup(activeGroup.value)
    } else {
      const err = await res.json(); flash(false, err.error || 'Erreur.')
    }
  } finally { saving.value = false }
}

const revokeAssignment = async (a) => {
  if (!confirm(`Révoquer l'accès de "${a.fullName || a.username}" à ce groupe ?`)) return
  const res = await groupsApi.revokeAssignment(activeGroup.value.id, a.assignmentId)
  if (res.ok) { flash(true, 'Accès révoqué.'); emit('saved'); await openGroup(activeGroup.value) }
  else flash(false, 'Erreur lors de la révocation.')
}

// ── Rights actions ─────────────────────────────────────────────────────────────
const toggleUserExpanded = (userId) => {
  const idx = expandedUsers.value.indexOf(userId)
  if (idx >= 0) expandedUsers.value.splice(idx, 1)
  else expandedUsers.value.push(userId)
}

const revokeGroupFromUser = async (user, group) => {
  if (!confirm(`Révoquer le groupe "${group.groupName}" pour "${user.fullName || user.username}" ?`)) return
  const res = await groupsApi.revokeAssignment(group.groupId, group.assignmentId)
  if (res.ok) { flash(true, 'Accès révoqué.'); emit('saved'); await loadRights() }
  else flash(false, 'Erreur.')
}

const revokeDirectGrant = async (grant) => {
  if (!confirm('Révoquer cet accès direct ?')) return
  const res = await groupsApi.revokeAcl(grant.documentId, grant.id)
  if (res.ok) { flash(true, 'Accès révoqué.'); emit('saved'); await loadRights() }
  else flash(false, 'Erreur.')
}

// ── Roles actions ─────────────────────────────────────────────────────────────
const changeRole = async (u) => {
  const newRole = roleChanges[u.id]
  if (!newRole || newRole === u.role) return
  if (!confirm(`Changer le rôle de "${u.username}" de "${roleLabel(u.role)}" à "${roleLabel(newRole)}" ?`)) return
  saving.value = true
  try {
    const res = await groupsApi.changeRole(u.id, newRole)
    if (res.ok) {
      flash(true, `Rôle mis à jour.`)
      emit('saved')
      await loadUsers()
      await loadRights()
    } else {
      const err = await res.json(); flash(false, err.error || 'Erreur.')
    }
  } finally { saving.value = false }
}

// ── Tab side-effects ──────────────────────────────────────────────────────────
watch(activeTab, async (tab) => {
  if (tab === 'rights' && !usersRights.value.length) await loadRights()
  if (tab === 'roles'  && !allUsers.value.length)    await loadUsers()
})

onMounted(async () => {
  await loadGroups()
  await loadUsers()
})
</script>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────────────────────── */
.modal-overlay {
  position: fixed; inset: 0;
  background: rgba(0,0,0,0.55);
  backdrop-filter: blur(4px);
  z-index: 600;
  display: flex; align-items: center; justify-content: center;
  padding: 1rem;
}
.modal {
  background: white; border-radius: 18px;
  width: 100%; max-width: 1000px; max-height: 90vh;
  display: flex; flex-direction: column;
  box-shadow: 0 30px 60px rgba(0,0,0,0.25);
  overflow: hidden;
}

/* ── Header ─────────────────────────────────────────────────────────────────── */
.modal-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 1.25rem 1.5rem; border-bottom: 1px solid #f1f5f9;
  background: linear-gradient(135deg, #f8faff 0%, #f0f4ff 100%);
}
.header-left { display: flex; align-items: center; gap: 0.75rem; }
.header-icon { font-size: 1.75rem; }
.modal-title { font-size: 1.2rem; font-weight: 700; color: #111827; margin: 0; }
.modal-subtitle { font-size: 0.78rem; color: #6b7280; margin: 0; }
.close-btn { background: none; border: none; font-size: 1.25rem; color: #9ca3af; cursor: pointer; padding: 0.25rem; border-radius: 6px; }
.close-btn:hover { background: #f3f4f6; color: #374151; }

/* ── Tabs ───────────────────────────────────────────────────────────────────── */
.tab-bar { display: flex; gap: 0.25rem; padding: 0.75rem 1.5rem 0; border-bottom: 2px solid #e5e7eb; background: white; }
.tab-btn {
  display: flex; align-items: center; gap: 0.35rem;
  padding: 0.55rem 1rem; border: none; background: none;
  font-size: 0.875rem; font-weight: 500; color: #6b7280;
  cursor: pointer; border-radius: 8px 8px 0 0;
  border-bottom: 3px solid transparent; transition: all 0.15s;
}
.tab-btn:hover { background: #f9fafb; color: #374151; }
.tab-btn.active { color: #2563eb; border-bottom-color: #2563eb; font-weight: 600; }
.tab-icon { font-size: 1rem; }
.tab-badge { background: #dbeafe; color: #1e40af; font-size: 0.7rem; font-weight: 700; padding: 0.1rem 0.4rem; border-radius: 9999px; }

/* ── Body ───────────────────────────────────────────────────────────────────── */
.modal-body { flex: 1; overflow-y: auto; padding: 1.5rem; display: flex; flex-direction: column; gap: 1rem; }
.banner { padding: 0.75rem 1rem; border-radius: 10px; font-size: 0.875rem; font-weight: 500; }
.banner.error   { background: #fef2f2; color: #dc2626; border: 1px solid #fecaca; }
.banner.success { background: #f0fdf4; color: #16a34a; border: 1px solid #bbf7d0; }

/* ── Sections ───────────────────────────────────────────────────────────────── */
.section { display: flex; flex-direction: column; gap: 1rem; }
.section-header { display: flex; align-items: center; justify-content: space-between; }
.section-title { font-size: 1rem; font-weight: 700; color: #111827; }
.subsection-header { display: flex; align-items: center; justify-content: space-between; margin: 0.5rem 0; }
.subsection-title { font-size: 0.875rem; font-weight: 600; color: #374151; }

/* ── Create form ────────────────────────────────────────────────────────────── */
.create-form { background: #f8faff; border: 1px solid #dbeafe; border-radius: 12px; padding: 1rem; display: flex; flex-direction: column; gap: 0.75rem; }
.form-title { font-size: 0.875rem; font-weight: 600; color: #1e40af; }
.form-grid-3 { display: grid; grid-template-columns: 1fr 1fr 0.5fr; gap: 0.5rem; }
.icon-color-row { display: flex; gap: 0.5rem; }
.icon-input { width: 60px; text-align: center; flex-shrink: 0; }
.color-input { width: 48px; padding: 0.2rem; height: 38px; cursor: pointer; }

/* ── Groups grid ────────────────────────────────────────────────────────────── */
.groups-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px,1fr)); gap: 0.75rem; }
.group-card { border: 1px solid #e5e7eb; border-left: 4px solid #2563eb; border-radius: 12px; padding: 1rem; cursor: pointer; transition: all 0.15s; background: white; }
.group-card:hover { box-shadow: 0 4px 12px rgba(37,99,235,0.12); transform: translateY(-1px); border-color: #bfdbfe; }
.group-card-top { display: flex; align-items: center; gap: 0.75rem; }
.group-icon { width: 40px; height: 40px; border-radius: 10px; display: flex; align-items: center; justify-content: center; font-size: 1.25rem; flex-shrink: 0; }
.group-info { flex: 1; min-width: 0; }
.group-name { font-weight: 700; color: #111827; font-size: 0.95rem; }
.group-category { font-size: 0.78rem; color: #6b7280; }
.group-desc { font-size: 0.8rem; color: #6b7280; margin-top: 0.4rem; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
.group-stats { display: flex; gap: 0.75rem; margin-top: 0.5rem; }
.stat { font-size: 0.78rem; color: #6b7280; }
.chevron { width: 16px; height: 16px; color: #9ca3af; flex-shrink: 0; transition: transform 0.2s; }
.chevron.rotated { transform: rotate(180deg); }

/* ── Group detail ───────────────────────────────────────────────────────────── */
.back-btn { background: none; border: none; color: #2563eb; font-size: 0.875rem; font-weight: 500; cursor: pointer; padding: 0; margin-bottom: 0.75rem; }
.back-btn:hover { text-decoration: underline; }
.group-detail-header { display: flex; align-items: center; gap: 1rem; border-left: 5px solid #2563eb; padding-left: 1rem; margin-bottom: 1rem; }
.group-icon-lg { width: 52px; height: 52px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 1.6rem; flex-shrink: 0; }
.group-detail-name { font-size: 1.1rem; font-weight: 700; color: #111827; }
.group-detail-meta { font-size: 0.8rem; color: #6b7280; }
.ml-auto { margin-left: auto; }

/* ── Sub-tabs ────────────────────────────────────────────────────────────────── */
.subtab-bar { display: flex; gap: 0.25rem; border-bottom: 2px solid #f3f4f6; margin-bottom: 1rem; }
.subtab-btn { padding: 0.4rem 0.85rem; background: none; border: none; border-bottom: 2px solid transparent; font-size: 0.85rem; font-weight: 500; color: #6b7280; cursor: pointer; transition: all 0.15s; }
.subtab-btn.active { color: #2563eb; border-bottom-color: #2563eb; }

/* ═══════════════════════════════════════════════════════════════
   NEW DOC PICKER
══════════════════════════════════════════════════════════════ */
.add-docs-panel {
  background: #f8faff; border: 1px solid #dbeafe;
  border-radius: 14px; padding: 1rem;
  display: flex; flex-direction: column; gap: 0.75rem;
  margin-bottom: 1rem;
}

/* Toolbar */
.picker-toolbar { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
.picker-search-wrap {
  flex: 1; min-width: 200px;
  display: flex; align-items: center; gap: 0; position: relative;
  background: white; border: 1.5px solid #d1d5db; border-radius: 9px;
  transition: border-color 0.15s;
}
.picker-search-wrap:focus-within { border-color: #2563eb; box-shadow: 0 0 0 3px rgba(37,99,235,0.08); }
.picker-search-icon { width: 16px; height: 16px; color: #9ca3af; margin-left: 0.6rem; flex-shrink: 0; }
.picker-search-input { flex: 1; border: none; outline: none; padding: 0.5rem 0.5rem; font-size: 0.875rem; background: transparent; color: #111827; }
.picker-search-clear { background: none; border: none; color: #9ca3af; cursor: pointer; padding: 0 0.5rem; font-size: 0.9rem; }
.picker-search-clear:hover { color: #374151; }

.picker-view-toggle { display: flex; gap: 0; background: white; border: 1.5px solid #d1d5db; border-radius: 8px; overflow: hidden; }
.pview-btn { background: none; border: none; padding: 0.4rem 0.65rem; font-size: 1rem; cursor: pointer; color: #9ca3af; transition: all 0.12s; }
.pview-btn.active { background: #eff6ff; color: #2563eb; }

.picker-selection-badge {
  background: #2563eb; color: white;
  font-size: 0.75rem; font-weight: 700;
  padding: 0.25rem 0.65rem; border-radius: 9999px;
}

/* Loading */
.picker-loading { display: flex; align-items: center; gap: 0.75rem; color: #6b7280; font-size: 0.875rem; padding: 1.5rem; justify-content: center; }
.spinner-ring-sm {
  width: 20px; height: 20px; border-radius: 50%;
  border: 2px solid #e5e7eb; border-top-color: #2563eb;
  animation: spin 0.7s linear infinite; flex-shrink: 0;
}
@keyframes spin { to { transform: rotate(360deg); } }

/* Drop zone overlay */
.drop-zone-overlay {
  min-height: 120px; border: 2.5px dashed #2563eb;
  border-radius: 12px; background: #eff6ff;
  display: flex; align-items: center; justify-content: center;
}
.drop-zone-inner { font-size: 1.1rem; font-weight: 600; color: #2563eb; }

/* ── GRID VIEW ─────────────────────────────────────────────── */
.doc-picker-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 0.65rem;
  max-height: 320px;
  overflow-y: auto;
  padding: 0.25rem;
}

.doc-card {
  position: relative;
  background: white;
  border: 2px solid #e5e7eb;
  border-radius: 12px;
  padding: 0.85rem 0.75rem 0.65rem;
  cursor: pointer;
  transition: all 0.12s;
  display: flex; flex-direction: column; gap: 0.35rem;
  user-select: none;
}
.doc-card:hover { border-color: #93c5fd; box-shadow: 0 3px 10px rgba(37,99,235,0.1); transform: translateY(-1px); }
.doc-card.selected { border-color: #2563eb; background: #eff6ff; box-shadow: 0 0 0 3px rgba(37,99,235,0.12); }
.doc-card[draggable="true"] { cursor: grab; }
.doc-card[draggable="true"]:active { cursor: grabbing; }

.doc-card-check {
  position: absolute; top: 0.5rem; right: 0.5rem;
  width: 20px; height: 20px; border-radius: 50%;
  border: 2px solid #d1d5db; background: white;
  display: flex; align-items: center; justify-content: center;
  transition: all 0.12s; flex-shrink: 0;
}
.doc-card-check.checked { background: #2563eb; border-color: #2563eb; }
.doc-card-check svg { width: 11px; height: 11px; color: white; }

.doc-card-icon { font-size: 2rem; text-align: center; margin: 0.25rem 0; }
.doc-card-body { min-width: 0; }
.doc-card-title { font-size: 0.8rem; font-weight: 600; color: #111827; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; }
.doc-card-meta  { font-size: 0.7rem; color: #2563eb; font-weight: 500; margin-top: 0.1rem; }
.doc-card-file  { font-size: 0.68rem; color: #9ca3af; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.doc-card-drag-hint { text-align: center; font-size: 0.75rem; color: #d1d5db; margin-top: 0.15rem; letter-spacing: 2px; }
.doc-card:hover .doc-card-drag-hint { color: #93c5fd; }

/* ── LIST VIEW ─────────────────────────────────────────────── */
.doc-picker-list-view { max-height: 280px; overflow-y: auto; display: flex; flex-direction: column; gap: 0.3rem; }
.doc-list-item {
  display: flex; align-items: center; gap: 0.65rem;
  padding: 0.55rem 0.75rem; border-radius: 9px;
  border: 1.5px solid #e5e7eb; background: white;
  cursor: pointer; transition: all 0.1s;
}
.doc-list-item:hover { border-color: #93c5fd; }
.doc-list-item.selected { border-color: #2563eb; background: #eff6ff; }
.doc-list-check {
  width: 18px; height: 18px; border-radius: 50%; flex-shrink: 0;
  border: 2px solid #d1d5db; background: white;
  display: flex; align-items: center; justify-content: center;
  transition: all 0.12s;
}
.doc-list-check.checked { background: #2563eb; border-color: #2563eb; }
.doc-list-check svg { width: 10px; height: 10px; color: white; }
.doc-list-icon { font-size: 1.25rem; flex-shrink: 0; }
.doc-list-info { flex: 1; min-width: 0; }

/* Picker empty & footer */
.picker-empty { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 2rem; color: #9ca3af; font-size: 0.875rem; }
.add-docs-footer { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }

/* ── Assign form ────────────────────────────────────────────────────────────── */
.assign-form { background: #f8faff; border: 1px solid #dbeafe; border-radius: 12px; padding: 1rem; display: grid; grid-template-columns: 1fr 1fr 1fr auto; gap: 0.5rem; align-items: start; }

/* ── Tables ─────────────────────────────────────────────────────────────────── */
.mini-table-wrap { border: 1px solid #e5e7eb; border-radius: 10px; overflow: auto; }
.mini-table { width: 100%; border-collapse: collapse; font-size: 0.83rem; }
.mini-table thead tr { background: #f9fafb; }
.mini-table th { padding: 0.6rem 0.75rem; text-align: left; font-weight: 600; color: #374151; font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.03em; border-bottom: 1px solid #e5e7eb; }
.mini-table td { padding: 0.6rem 0.75rem; border-bottom: 1px solid #f3f4f6; vertical-align: middle; }
.mini-table tbody tr:last-child td { border-bottom: none; }
.mini-table tbody tr:hover { background: #fafbff; }
.mini-table tbody tr.expired { opacity: 0.5; }
.doc-title-cell { display: block; font-weight: 600; color: #111827; }
.doc-filename { display: block; font-size: 0.73rem; color: #9ca3af; }
.cell-date { color: #6b7280; white-space: nowrap; }

/* ── User rights ─────────────────────────────────────────────────────────────── */
.users-rights-list { display: flex; flex-direction: column; gap: 0.5rem; }
.user-rights-card { border: 1px solid #e5e7eb; border-radius: 12px; overflow: hidden; }
.user-rights-card.inactive { opacity: 0.6; }
.user-rights-header { display: flex; align-items: center; gap: 1rem; padding: 0.85rem 1rem; cursor: pointer; transition: background 0.1s; }
.user-rights-header:hover { background: #f9fafb; }
.user-rights-summary { display: flex; gap: 0.75rem; align-items: center; margin-left: auto; }
.user-rights-detail { padding: 1rem; border-top: 1px solid #f3f4f6; background: #fafbff; display: flex; flex-direction: column; gap: 1rem; }
.rights-subsection { display: flex; flex-direction: column; gap: 0.5rem; }
.rights-subsection-title { font-size: 0.78rem; font-weight: 600; color: #374151; text-transform: uppercase; letter-spacing: 0.05em; }
.group-tags { display: flex; flex-wrap: wrap; gap: 0.5rem; }
.group-tag { display: flex; align-items: center; gap: 0.5rem; padding: 0.3rem 0.65rem; border-radius: 8px; border: 1px solid; font-size: 0.82rem; font-weight: 500; color: #374151; }
.search-input { width: 220px; }

/* ── Role defs ───────────────────────────────────────────────────────────────── */
.role-legend { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px,1fr)); gap: 0.75rem; margin-bottom: 1.5rem; }
.role-def-card { border: 1px solid #e5e7eb; border-radius: 12px; padding: 1rem; }
.role-def-header { margin-bottom: 0.5rem; }
.role-def-desc { font-size: 0.82rem; color: #6b7280; margin-bottom: 0.5rem; }
.role-def-perms { padding-left: 0; list-style: none; font-size: 0.78rem; color: #374151; display: flex; flex-direction: column; gap: 0.2rem; }
.role-change-row { display: flex; gap: 0.4rem; align-items: center; }

/* ── Badges ──────────────────────────────────────────────────────────────────── */
.role-badge { padding: 0.2rem 0.5rem; border-radius: 6px; font-size: 0.75rem; font-weight: 600; }
.badge-admin    { background: #fef3c7; color: #92400e; }
.badge-manager  { background: #dbeafe; color: #1e40af; }
.badge-user     { background: #f0fdf4; color: #166534; }
.badge-readonly { background: #f3f4f6; color: #374151; }
.role-badge-inline { font-size: 0.72rem; font-weight: 600; padding: 0.1rem 0.35rem; border-radius: 4px; }
.perm-badge { padding: 0.18rem 0.45rem; border-radius: 6px; font-size: 0.73rem; font-weight: 600; }
.perm-read    { background: #f0fdf4; color: #166534; }
.perm-write   { background: #eff6ff; color: #1e40af; }
.perm-delete  { background: #fff7ed; color: #9a3412; }
.perm-full    { background: #fdf4ff; color: #7e22ce; }
.badge-permanent { background: #f8faff; color: #6b7280; border: 1px solid #e5e7eb; }
.status-badge { padding: 0.2rem 0.5rem; border-radius: 6px; font-size: 0.75rem; font-weight: 600; }
.status-active   { background: #f0fdf4; color: #16a34a; }
.status-inactive { background: #fef2f2; color: #dc2626; }
.category-pill { background: #eff6ff; color: #1d4ed8; padding: 0.15rem 0.45rem; border-radius: 6px; font-size: 0.75rem; font-weight: 500; }
.expired-tag { background: #fef2f2; color: #dc2626; padding: 0.1rem 0.35rem; border-radius: 4px; font-size: 0.7rem; font-weight: 600; margin-left: 0.25rem; }
.text-red { color: #dc2626; }

/* ── User cell ───────────────────────────────────────────────────────────────── */
.user-cell { display: flex; align-items: center; gap: 0.5rem; }
.mini-avatar { width: 30px; height: 30px; border-radius: 50%; background: #dbeafe; color: #1e40af; font-size: 0.7rem; font-weight: 700; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
.mini-avatar.badge-admin   { background: #fef3c7; color: #92400e; }
.mini-avatar.badge-manager { background: #dbeafe; color: #1e40af; }
.mini-avatar.badge-user    { background: #f0fdf4; color: #166534; }
.cell-name { font-weight: 600; color: #111827; font-size: 0.85rem; }
.cell-sub  { font-size: 0.75rem; color: #6b7280; }

/* ── Inputs ──────────────────────────────────────────────────────────────────── */
.input { padding: 0.5rem 0.75rem; border: 1px solid #d1d5db; border-radius: 8px; font-size: 0.875rem; color: #111827; width: 100%; outline: none; transition: border-color 0.15s; background: white; }
.input:focus { border-color: #2563eb; box-shadow: 0 0 0 3px rgba(37,99,235,0.08); }
.input-sm { padding: 0.35rem 0.6rem; font-size: 0.82rem; width: auto; }
.textarea { resize: vertical; min-height: 60px; }

/* ── Buttons ─────────────────────────────────────────────────────────────────── */
.btn-primary { padding: 0.55rem 1.25rem; background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; border: none; border-radius: 8px; font-size: 0.875rem; font-weight: 600; cursor: pointer; transition: all 0.15s; }
.btn-primary:hover:not(:disabled) { box-shadow: 0 4px 12px rgba(37,99,235,0.3); transform: translateY(-1px); }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary-sm { padding: 0.35rem 0.85rem; background: linear-gradient(135deg,#2563eb,#4f46e5); color: white; border: none; border-radius: 7px; font-size: 0.8rem; font-weight: 600; cursor: pointer; white-space: nowrap; }
.btn-primary-sm:hover:not(:disabled) { opacity: 0.9; }
.btn-primary-sm:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-danger-sm { padding: 0.3rem 0.65rem; background: #fef2f2; color: #dc2626; border: 1px solid #fecaca; border-radius: 7px; font-size: 0.78rem; font-weight: 600; cursor: pointer; white-space: nowrap; }
.btn-danger-sm:hover { background: #dc2626; color: white; }
.btn-icon-danger { background: none; border: none; color: #dc2626; font-size: 0.85rem; cursor: pointer; padding: 0.1rem 0.3rem; border-radius: 4px; }
.btn-icon-danger:hover { background: #fef2f2; }
.btn-ghost-sm { padding: 0.35rem 0.75rem; background: white; border: 1px solid #d1d5db; color: #374151; border-radius: 7px; font-size: 0.8rem; font-weight: 500; cursor: pointer; }
.btn-ghost-sm:hover { border-color: #9ca3af; }

/* ── Misc ────────────────────────────────────────────────────────────────────── */
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0,0,0,0); }
.check-mark { margin-left: auto; color: #2563eb; font-weight: 700; }
.empty-state { color: #9ca3af; font-size: 0.875rem; text-align: center; padding: 2rem 1rem; }
.empty-picker { color: #9ca3af; font-size: 0.85rem; text-align: center; padding: 1rem 0; }
.loading { color: #9ca3af; font-size: 0.875rem; text-align: center; padding: 2rem; }

/* picker shared */
.doc-picker-title { font-size: 0.85rem; font-weight: 600; color: #111827; }
.doc-picker-sub { font-size: 0.75rem; color: #6b7280; }
</style>