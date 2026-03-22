<template>
  <aside
    class="app-sidebar"
    :class="{ 'is-collapsed': isCollapsed, 'is-open': !isCollapsed }"
  >
    <div class="sidebar-inner">
      <!-- Brand -->
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
        <Transition name="fade-slide">
          <span
            v-if="!isCollapsed"
            class="brand-name"
          >GED Elise</span>
        </Transition>
      </div>

      <!-- Collapse toggle -->
      <button
        class="collapse-toggle"
        :title="isCollapsed ? 'Développer' : 'Réduire'"
        @click="uiStore.toggleSidebar()"
      >
        <svg
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
          class="collapse-icon"
          :class="{ 'is-rotated': isCollapsed }"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M11 19l-7-7 7-7m8 14l-7-7 7-7"
          />
        </svg>
      </button>

      <!-- Nav -->
      <nav class="sidebar-nav">
        <button
          v-for="(tab, i) in filteredTabs"
          :key="tab.id"
          class="nav-item"
          :class="{ active: activeTab === tab.id }"
          :title="isCollapsed ? tab.label : undefined"
          :style="{ animationDelay: `${i * 40}ms` }"
          @click="$emit('navigate', tab.id)"
        >
          <span
            class="nav-icon"
            v-html="tab.icon"
          />
          <Transition name="fade-slide">
            <span
              v-if="!isCollapsed"
              class="nav-label"
            >{{ tab.label }}</span>
          </Transition>
          <Transition name="fade-slide">
            <span
              v-if="!isCollapsed && tab.badge"
              class="nav-badge"
            >{{ tab.badge }}</span>
          </Transition>
        </button>
      </nav>

      <!-- Spacer -->
      <div class="sidebar-spacer" />

      <!-- Bottom section -->
      <div class="sidebar-bottom">
        <!-- Theme toggle -->
        <button
          class="nav-item theme-toggle"
          :title="uiStore.isDarkMode ? 'Mode clair' : 'Mode sombre'"
          @click="uiStore.toggleDarkMode()"
        >
          <span class="nav-icon">
            <svg
              v-if="!uiStore.isDarkMode"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z"
              />
            </svg>
            <svg
              v-else
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z"
              />
            </svg>
          </span>
          <Transition name="fade-slide">
            <span
              v-if="!isCollapsed"
              class="nav-label"
            >{{ uiStore.isDarkMode ? 'Mode clair' : 'Mode sombre' }}</span>
          </Transition>
        </button>

        <!-- User badge -->
        <div class="sidebar-user">
          <div class="user-avatar">
            {{ userInitials }}
          </div>
          <Transition name="fade-slide">
            <div
              v-if="!isCollapsed"
              class="user-info"
            >
              <p class="user-name">
                {{ user?.fullName || user?.username }}
              </p>
              <p
                class="user-role-tag"
                :class="roleTagClass"
              >
                {{ roleLabel(user?.role) }}
              </p>
            </div>
          </Transition>
          <Transition name="fade-slide">
            <button
              v-if="!isCollapsed"
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
          </Transition>
        </div>
      </div>
    </div>
  </aside>
</template>

<script setup>
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth.js'
import { useUiStore } from '@/stores/ui.js'

const props = defineProps({
  activeTab: {
    type: String,
    default: 'search'
  },
  tabs: {
    type: Array,
    default: () => []
  }
})

defineEmits(['navigate'])

const router = useRouter()
const authStore = useAuthStore()
const uiStore = useUiStore()

const user = computed(() => authStore.user)
const isCollapsed = computed(() => !uiStore.isSidebarOpen)

const filteredTabs = computed(() => {
  if (!props.tabs.length) return defaultTabs
  return props.tabs
})

const defaultTabs = [
  {
    id: 'search',
    label: 'Recherche',
    icon: '<svg fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/></svg>'
  }
]

const userInitials = computed(() => {
  const u = user.value
  if (!u) return '?'
  const fn = u.fullName || u.username || ''
  const parts = fn.trim().split(' ')
  if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
  return fn.slice(0, 2).toUpperCase()
})

const roleTagClass = computed(() => {
  const r = user.value?.role
  if (r === 'Admin') return 'role-admin'
  if (r === 'Manager') return 'role-manager'
  if (r === 'User') return 'role-user'
  return 'role-readonly'
})

function roleLabel(role) {
  const labels = { Admin: 'Administrateur', Manager: 'Gestionnaire', User: 'Utilisateur', ReadOnly: 'Lecture seule' }
  return labels[role] || role || '—'
}

function logout() {
  authStore.logout()
  router.push('/login')
}
</script>

<style scoped>
.app-sidebar {
  position: relative;
  width: 240px;
  min-height: 100vh;
  background: var(--color-sidebar-bg);
  display: flex;
  flex-direction: column;
  transition: width 0.3s cubic-bezier(0.16, 1, 0.3, 1);
  overflow: hidden;
  flex-shrink: 0;
}

.app-sidebar.is-collapsed {
  width: 64px;
}

.sidebar-inner {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 100vh;
}

.sidebar-brand {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1.25rem 1rem;
  border-bottom: 1px solid var(--color-sidebar-border);
  min-height: 64px;
}

.brand-icon {
  width: 36px;
  height: 36px;
  background: linear-gradient(135deg, #3b82f6, #8b5cf6);
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.brand-icon svg {
  width: 20px;
  height: 20px;
  color: white;
}

.brand-name {
  font-weight: 700;
  font-size: 1rem;
  color: white;
  white-space: nowrap;
}

.collapse-toggle {
  position: absolute;
  top: 20px;
  right: -12px;
  width: 24px;
  height: 24px;
  background: #1e293b;
  border: 2px solid #0f172a;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  z-index: 10;
  transition: all 0.2s;
  padding: 0;
}

.collapse-toggle:hover {
  background: #334155;
  transform: scale(1.1);
}

.collapse-icon {
  width: 12px;
  height: 12px;
  color: #94a3b8;
  transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1);
}

.collapse-icon.is-rotated {
  transform: rotate(180deg);
}

.sidebar-nav {
  flex: 1;
  padding: 1rem 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  overflow-y: auto;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.625rem 0.875rem;
  border-radius: 8px;
  background: none;
  border: none;
  color: var(--color-sidebar-text);
  font-size: 0.875rem;
  cursor: pointer;
  transition: all 0.15s;
  text-align: left;
  width: 100%;
  white-space: nowrap;
  animation: slideInLeft 0.3s ease-out both;
}

.nav-item:hover {
  background: var(--color-sidebar-hover);
  color: var(--color-sidebar-text-active);
}

.nav-item.active {
  background: var(--color-sidebar-active);
  color: white;
  font-weight: 600;
}

.nav-icon {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
}

.nav-icon :deep(svg) {
  width: 18px;
  height: 18px;
}

.nav-label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
}

.nav-badge {
  background: rgba(255, 255, 255, 0.2);
  color: white;
  font-size: 0.7rem;
  font-weight: 700;
  padding: 0.1rem 0.4rem;
  border-radius: 9999px;
  min-width: 20px;
  text-align: center;
}

.sidebar-spacer {
  flex: 1;
}

.sidebar-bottom {
  border-top: 1px solid var(--color-sidebar-border);
  padding: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.theme-toggle {
  color: var(--color-sidebar-text);
}

.sidebar-user {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.5rem 0.875rem;
  border-radius: 8px;
  min-height: 52px;
}

.user-avatar {
  width: 32px;
  height: 32px;
  background: linear-gradient(135deg, #3b82f6, #8b5cf6);
  border-radius: 50%;
  color: white;
  font-size: 0.75rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.user-info {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.user-name {
  font-weight: 600;
  font-size: 0.8rem;
  color: white;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.user-role-tag {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.1rem 0.4rem;
  border-radius: 9999px;
  display: inline-block;
  margin-top: 0.15rem;
}

.role-admin    { background: rgba(245, 158, 11, 0.2); color: #fbbf24; }
.role-manager  { background: rgba(59, 130, 246, 0.2); color: #60a5fa; }
.role-user     { background: rgba(34, 197, 94, 0.2); color: #4ade80; }
.role-readonly { background: rgba(148, 163, 184, 0.2); color: #94a3b8; }

.logout-btn {
  width: 28px;
  height: 28px;
  background: none;
  border: none;
  color: #64748b;
  cursor: pointer;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  transition: all 0.15s;
  padding: 0;
}

.logout-btn:hover {
  background: rgba(239, 68, 68, 0.2);
  color: #ef4444;
}

.logout-btn svg {
  width: 16px;
  height: 16px;
}

/* Transition */
.fade-slide-enter-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}

.fade-slide-leave-active {
  transition: opacity 0.15s ease, transform 0.15s ease;
}

.fade-slide-enter-from {
  opacity: 0;
  transform: translateX(-8px);
}

.fade-slide-leave-to {
  opacity: 0;
  transform: translateX(-8px);
}

@keyframes slideInLeft {
  from {
    opacity: 0;
    transform: translateX(-12px);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
}

/* Mobile */
@media (max-width: 768px) {
  .app-sidebar {
    display: none;
  }
}
</style>
