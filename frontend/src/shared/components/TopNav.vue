<template>
  <header class="top-nav">
    <div class="nav-left">
      <button
        class="mobile-menu-btn"
        @click="uiStore.toggleSidebar()"
      >
        <svg
          width="20"
          height="20"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M4 6h16M4 12h16M4 18h16"
          />
        </svg>
      </button>
      <div class="nav-brand">
        <div class="brand-icon">
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
            />
          </svg>
        </div>
        <span class="brand-name">GED Elise</span>
      </div>
      <nav class="nav-breadcrumb">
        <span
          v-for="(crumb, i) in breadcrumbs"
          :key="i"
          class="breadcrumb-item"
        >
          <svg
            v-if="i > 0"
            class="breadcrumb-sep"
            width="12"
            height="12"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M9 5l7 7-7 7"
            />
          </svg>
          <span :class="{ 'is-active': i === breadcrumbs.length - 1 }">
            {{ crumb.label }}
          </span>
        </span>
      </nav>
    </div>

    <div class="nav-center">
      <button
        class="search-trigger"
        @click="openCommandPalette"
      >
        <svg
          width="15"
          height="15"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
          />
        </svg>
        <span>Rechercher…</span>
        <kbd>⌘K</kbd>
      </button>
    </div>

    <div class="nav-right">
      <button
        class="nav-icon-btn"
        title="Notifications"
      >
        <svg
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
          />
        </svg>
      </button>

      <button
        class="nav-icon-btn"
        :title="uiStore.isDarkMode ? 'Mode clair' : 'Mode sombre'"
        @click="uiStore.toggleDarkMode()"
      >
        <svg
          v-if="!uiStore.isDarkMode"
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
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
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z"
          />
        </svg>
      </button>

      <div class="nav-user">
        <div class="user-avatar">
          {{ userInitials }}
        </div>
      </div>
    </div>
  </header>

  <!-- Mobile bottom nav -->
  <nav class="mobile-bottom-nav">
    <button
      v-for="tab in mobileTabs"
      :key="tab.id"
      class="mobile-tab"
      :class="{ active: activeTab === tab.id }"
      @click="$emit('navigate', tab.id)"
    >
      <span v-html="tab.icon" />
      <span class="mobile-tab-label">{{ tab.label }}</span>
    </button>
  </nav>
</template>

<script setup>
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth.js'
import { useUiStore } from '@/stores/ui.js'

defineProps({
  activeTab: {
    type: String,
    default: 'search'
  },
  breadcrumbs: {
    type: Array,
    default: () => []
  }
})

const emit = defineEmits(['navigate', 'open-command-palette'])

const authStore = useAuthStore()
const uiStore = useUiStore()

const user = computed(() => authStore.user)

const userInitials = computed(() => {
  const fn = user.value?.fullName || user.value?.username || ''
  const parts = fn.trim().split(' ')
  if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
  return fn.slice(0, 2).toUpperCase()
})

const mobileTabs = [
  {
    id: 'search',
    label: 'Recherche',
    icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/></svg>'
  },
  {
    id: 'documents',
    label: 'Documents',
    icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/></svg>'
  },
  {
    id: 'upload',
    label: 'Importer',
    icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/></svg>'
  },
  {
    id: 'profile',
    label: 'Profil',
    icon: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"/></svg>'
  }
]

function openCommandPalette() {
  emit('open-command-palette')
}
</script>

<style scoped>
.top-nav {
  display: none;
  align-items: center;
  justify-content: space-between;
  padding: 0 1.25rem;
  height: 56px;
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
  position: sticky;
  top: 0;
  z-index: 50;
  gap: 1rem;
}

@media (max-width: 768px) {
  .top-nav {
    display: flex;
  }
}

.nav-left {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  min-width: 0;
}

.mobile-menu-btn {
  width: 36px;
  height: 36px;
  background: none;
  border: 1px solid var(--color-border);
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  color: var(--color-text-secondary);
  transition: all 0.15s;
  flex-shrink: 0;
}

.mobile-menu-btn:hover {
  background: var(--color-surface-hover);
}

.nav-brand {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-shrink: 0;
}

.brand-icon {
  width: 28px;
  height: 28px;
  background: linear-gradient(135deg, #3b82f6, #8b5cf6);
  border-radius: 7px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: white;
}

.brand-name {
  font-weight: 700;
  font-size: 0.9rem;
  color: var(--color-text-primary);
}

.nav-breadcrumb {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  min-width: 0;
  overflow: hidden;
}

.breadcrumb-item {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  font-size: 0.8rem;
  color: var(--color-text-muted);
}

.breadcrumb-item .is-active {
  color: var(--color-text-primary);
  font-weight: 600;
}

.breadcrumb-sep {
  opacity: 0.5;
}

.nav-center {
  flex: 1;
  max-width: 400px;
  display: flex;
  justify-content: center;
}

.search-trigger {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  width: 100%;
  max-width: 320px;
  padding: 0.5rem 0.875rem;
  background: var(--color-surface-hover);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  cursor: pointer;
  color: var(--color-text-muted);
  font-size: 0.8rem;
  transition: all 0.15s;
}

.search-trigger:hover {
  border-color: var(--color-primary);
  background: var(--color-surface);
}

.search-trigger span {
  flex: 1;
  text-align: left;
}

.search-trigger kbd {
  font-size: 0.65rem;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 4px;
  padding: 0.1rem 0.3rem;
}

.nav-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.nav-icon-btn {
  width: 36px;
  height: 36px;
  background: none;
  border: none;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  color: var(--color-text-secondary);
  transition: all 0.15s;
}

.nav-icon-btn:hover {
  background: var(--color-surface-hover);
  color: var(--color-text-primary);
}

.nav-user {
  display: flex;
  align-items: center;
  margin-left: 0.25rem;
}

.user-avatar {
  width: 32px;
  height: 32px;
  background: linear-gradient(135deg, #3b82f6, #8b5cf6);
  border-radius: 50%;
  color: white;
  font-size: 0.7rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}

/* Mobile bottom nav */
.mobile-bottom-nav {
  display: none;
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  background: var(--color-surface);
  border-top: 1px solid var(--color-border);
  z-index: 50;
  padding: 0.5rem 0;
  padding-bottom: max(0.5rem, env(safe-area-inset-bottom));
}

@media (max-width: 768px) {
  .mobile-bottom-nav {
    display: flex;
    justify-content: space-around;
  }
}

.mobile-tab {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
  padding: 0.375rem 0.75rem;
  background: none;
  border: none;
  cursor: pointer;
  color: var(--color-text-muted);
  transition: color 0.15s;
  min-width: 56px;
}

.mobile-tab.active {
  color: var(--color-primary);
}

.mobile-tab-label {
  font-size: 0.65rem;
  font-weight: 600;
}

.mobile-tab :deep(svg) {
  width: 20px;
  height: 20px;
}
</style>
