<template>
  <nav class="navbar">
    <div class="nav-inner">
      <!-- Left: Logo + Links -->
      <div class="nav-left">
        <router-link to="/" class="nav-logo">
          <div class="logo-icon">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
          </div>
          <span class="logo-text">GED Search</span>
        </router-link>

        <div class="nav-links">
          <router-link to="/" class="nav-link" :class="{ active: $route.path === '/' }">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>
            </svg>
            {{ t('nav.search') }}
          </router-link>

          <router-link to="/upload" class="nav-link" :class="{ active: $route.path === '/upload' }">
            <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12"/>
            </svg>
            {{ t('nav.upload') }}
          </router-link>
        </div>
      </div>

      <!-- Right: Language toggle + Notification bell + User menu -->
      <div class="nav-right">
        <!-- Language toggle -->
        <div class="lang-toggle" title="Changer de langue">
          <button v-for="lang in languages" :key="lang.code"
            :class="['lang-btn', { active: currentLang === lang.code }]"
            @click="setLang(lang.code)">
            {{ lang.label }}
          </button>
        </div>

        <!-- Notification bell (Admin only) -->
        <div v-if="isAdmin" class="notif-bell" @click="toggleNotifPanel" ref="notifRef">
          <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
              d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"/>
          </svg>
          <span v-if="unreadCount > 0" class="notif-badge">{{ unreadCount > 9 ? '9+' : unreadCount }}</span>

          <!-- Notification panel -->
          <div v-if="notifOpen" class="notif-panel">
            <div class="notif-header">
              <span class="notif-title">{{ t('notif.title') }}</span>
              <button v-if="notifications.length" @click.stop="clearNotifications" class="notif-clear">{{ t('notif.clear') }}</button>
            </div>
            <div v-if="notifications.length === 0" class="notif-empty">
              {{ t('notif.empty') }}
            </div>
            <div v-else class="notif-list">
              <div v-for="n in notifications" :key="n.id" :class="['notif-item', n.type]">
                <div class="notif-icon">{{ n.icon }}</div>
                <div class="notif-body">
                  <p class="notif-msg">{{ n.message }}</p>
                  <span class="notif-time">{{ formatTime(n.time) }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- User dropdown -->
        <div class="user-menu" @click="dropdownOpen = !dropdownOpen" ref="dropdownRef">
          <div class="user-avatar">
            {{ userInitials }}
          </div>
          <div class="user-info">
            <span class="user-name">{{ user?.fullName || user?.username || 'Utilisateur' }}</span>
            <span class="user-role" :class="roleClass">{{ roleName }}</span>
          </div>
          <svg class="chevron" :class="{ open: dropdownOpen }" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"/>
          </svg>

          <!-- Dropdown menu -->
          <div v-if="dropdownOpen" class="dropdown">
            <div class="dropdown-header">
              <p class="dropdown-name">{{ user?.fullName || user?.username }}</p>
              <p class="dropdown-email">{{ user?.username }}</p>
            </div>

            <div class="dropdown-divider"></div>

            <!-- Admin: user management -->
            <button v-if="isAdmin" @click="showUserManagement = true" class="dropdown-item">
              <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                  d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z"/>
              </svg>
              {{ t('nav.userMgmt') }}
            </button>

            <div class="dropdown-divider"></div>

            <button @click="logout" class="dropdown-item logout-item">
              <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                  d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1"/>
              </svg>
              {{ t('nav.logout') }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- User Management Modal (Admin only) -->
    <UserManagementModal
      v-if="showUserManagement"
      @close="showUserManagement = false"
    />
  </nav>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRouter } from 'vue-router'
import { auth } from '../api.js'
import { useNotifications } from '../composables/useNotifications.js'
import UserManagementModal from './UserManagementModal.vue'

const router = useRouter()
const dropdownOpen       = ref(false)
const showUserManagement = ref(false)
const dropdownRef        = ref(null)
const notifOpen         = ref(false)
const notifRef          = ref(null)

const { notifications, unreadCount, clear: clearNotifs } = useNotifications()

const currentLang = ref(localStorage.getItem('ged_lang') || 'fr')

const languages = [
  { code: 'fr', label: 'FR' },
  { code: 'en', label: 'EN' },
  { code: 'ar', label: 'AR' }
]

const translations = {
  fr: {
    nav: { search: 'Recherche', upload: 'Importer', userMgmt: 'Gestion des utilisateurs', logout: 'Se déconnecter' },
    notif: { title: 'Notifications', clear: 'Tout effacer', empty: 'Aucune notification' }
  },
  en: {
    nav: { search: 'Search', upload: 'Upload', userMgmt: 'User Management', logout: 'Sign out' },
    notif: { title: 'Notifications', clear: 'Clear all', empty: 'No notifications' }
  },
  ar: {
    nav: { search: 'البحث', upload: 'رفع', userMgmt: 'إدارة المستخدمين', logout: 'تسجيل الخروج' },
    notif: { title: 'الإشعارات', clear: 'مسح الكل', empty: 'لا توجد إشعارات' }
  }
}

const t = computed(() => translations[currentLang.value] || translations.fr)

const setLang = (code) => {
  currentLang.value = code
  localStorage.setItem('ged_lang', code)
  if (code === 'ar') {
    document.documentElement.dir = 'rtl'
  } else {
    document.documentElement.dir = 'ltr'
  }
}

const toggleNotifPanel = () => {
  notifOpen.value = !notifOpen.value
}

const formatTime = (d) => {
  const diff = Math.floor((Date.now() - new Date(d).getTime()) / 1000)
  if (diff < 60) return `${diff}s ago`
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`
  return new Date(d).toLocaleDateString()
}

const user    = computed(() => auth.getUser())
const isAdmin = computed(() => auth.isAdmin())

const userInitials = computed(() => {
  const name = user.value?.fullName || user.value?.username || '?'
  return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2)
})

const roleName = computed(() => {
  const roles = { Admin: 'Administrateur', Manager: 'Responsable', User: 'Utilisateur', ReadOnly: 'Lecture seule' }
  return roles[user.value?.role] || user.value?.role || ''
})

const roleClass = computed(() => {
  const classes = { Admin: 'role-admin', Manager: 'role-manager', User: 'role-user', ReadOnly: 'role-readonly' }
  return classes[user.value?.role] || ''
})

const logout = () => {
  dropdownOpen.value = false
  auth.logout()
}

const handleClickOutside = (e) => {
  if (dropdownRef.value && !dropdownRef.value.contains(e.target))
    dropdownOpen.value = false
  if (notifRef.value && !notifRef.value.contains(e.target))
    notifOpen.value = false
}

onMounted(() => document.addEventListener('click', handleClickOutside))
onBeforeUnmount(() => {})
</script>

<style scoped>
.navbar {
  background: rgba(255, 255, 255, 0.92);
  backdrop-filter: blur(12px);
  border-bottom: 1px solid #e5e7eb;
  position: sticky;
  top: 0;
  z-index: 100;
}

.nav-inner {
  max-width: 1280px;
  margin: 0 auto;
  padding: 0 1.5rem;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.nav-left { display: flex; align-items: center; gap: 2rem; }

.nav-logo {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  text-decoration: none;
}

.logo-icon {
  width: 36px;
  height: 36px;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  border-radius: 9px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.logo-icon svg { width: 20px; height: 20px; color: white; }

.logo-text {
  font-size: 1.1rem;
  font-weight: 700;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.nav-links { display: flex; gap: 0.25rem; }

.nav-link {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.4rem 0.75rem;
  border-radius: 8px;
  text-decoration: none;
  font-size: 0.875rem;
  font-weight: 500;
  color: #6b7280;
  transition: all 0.15s;
  position: relative;
}

.nav-link svg { width: 16px; height: 16px; }

.nav-link:hover { background: #f3f4f6; color: #111827; }
.nav-link.active { background: #eff6ff; color: #2563eb; }

.rag-link {
  background: linear-gradient(135deg, #faf5ff, #eff6ff);
  color: #6d28d9;
  border: 1px solid #e9d5ff;
}

.rag-link:hover { background: linear-gradient(135deg, #f3e8ff, #dbeafe); }
.rag-link.active { background: linear-gradient(135deg, #ede9fe, #dbeafe); color: #4c1d95; }

.badge-new {
  font-size: 0.65rem;
  font-weight: 700;
  background: linear-gradient(135deg, #6d28d9, #2563eb);
  color: white;
  padding: 0.1rem 0.4rem;
  border-radius: 9999px;
  letter-spacing: 0.02em;
}

/* User menu */
.nav-right { display: flex; align-items: center; }

.user-menu {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.3rem 0.75rem;
  border-radius: 10px;
  cursor: pointer;
  position: relative;
  transition: background 0.15s;
}

.user-menu:hover { background: #f3f4f6; }

.user-avatar {
  width: 34px;
  height: 34px;
  background: linear-gradient(135deg, #2563eb, #4f46e5);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: white;
  font-size: 0.75rem;
  font-weight: 700;
}

.user-info { display: flex; flex-direction: column; }

.user-name { font-size: 0.85rem; font-weight: 600; color: #111827; line-height: 1.2; }

.user-role {
  font-size: 0.7rem;
  font-weight: 500;
  padding: 0.1rem 0.4rem;
  border-radius: 9999px;
  width: fit-content;
}

.role-admin    { background: #fef3c7; color: #92400e; }
.role-manager  { background: #dbeafe; color: #1d4ed8; }
.role-user     { background: #d1fae5; color: #065f46; }
.role-readonly { background: #f3f4f6; color: #6b7280; }

.chevron { width: 16px; height: 16px; color: #6b7280; transition: transform 0.2s; }
.chevron.open { transform: rotate(180deg); }

/* Dropdown */
.dropdown {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 12px;
  box-shadow: 0 10px 25px -5px rgba(0,0,0,0.12);
  width: 220px;
  z-index: 200;
  overflow: hidden;
}

.dropdown-header { padding: 0.875rem 1rem; }
.dropdown-name   { font-weight: 600; font-size: 0.875rem; color: #111827; }
.dropdown-email  { font-size: 0.8rem; color: #6b7280; margin-top: 0.1rem; }

.dropdown-divider { height: 1px; background: #e5e7eb; }

.dropdown-item {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  width: 100%;
  padding: 0.625rem 1rem;
  background: none;
  border: none;
  font-size: 0.875rem;
  color: #374151;
  cursor: pointer;
  transition: background 0.15s;
  text-align: left;
}

.dropdown-item svg { width: 16px; height: 16px; }
.dropdown-item:hover { background: #f3f4f6; }

.logout-item { color: #dc2626; }
.logout-item:hover { background: #fef2f2; }

/* Language toggle */
.lang-toggle {
  display: flex;
  background: #f3f4f6;
  border-radius: 8px;
  padding: 2px;
  gap: 2px;
}
.lang-btn {
  padding: .2rem .45rem;
  border: none;
  border-radius: 6px;
  background: none;
  font-size: .72rem;
  font-weight: 700;
  color: #6b7280;
  cursor: pointer;
  transition: all .15s;
}
.lang-btn.active {
  background: white;
  color: #2563eb;
  box-shadow: 0 1px 3px rgba(0,0,0,.1);
}

/* Notification bell */
.notif-bell {
  position: relative;
  padding: .4rem .5rem;
  border-radius: 8px;
  cursor: pointer;
  color: #6b7280;
  transition: all .15s;
  display: flex;
  align-items: center;
}
.notif-bell:hover { background: #f3f4f6; color: #374151; }
.notif-bell svg { width: 18px; height: 18px; }
.notif-badge {
  position: absolute;
  top: 2px;
  right: 2px;
  background: #ef4444;
  color: white;
  font-size: .6rem;
  font-weight: 700;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 2px solid white;
}
.notif-panel {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 12px;
  box-shadow: 0 10px 25px rgba(0,0,0,.12);
  width: 340px;
  z-index: 300;
  overflow: hidden;
}
.notif-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: .875rem 1rem;
  border-bottom: 1px solid #f0f0f0;
}
.notif-title { font-size: .85rem; font-weight: 700; color: #111827; }
.notif-clear { font-size: .75rem; color: #2563eb; background: none; border: none; cursor: pointer; }
.notif-clear:hover { text-decoration: underline; }
.notif-empty { padding: 2rem 1rem; text-align: center; font-size: .82rem; color: #9ca3af; }
.notif-list { max-height: 360px; overflow-y: auto; }
.notif-item { display: flex; align-items: flex-start; gap: .6rem; padding: .75rem 1rem; border-bottom: 1px solid #f9fafb; }
.notif-item:hover { background: #f9fafb; }
.notif-item.success { border-left: 3px solid #10b981; }
.notif-item.error { border-left: 3px solid #ef4444; }
.notif-item.info { border-left: 3px solid #3b82f6; }
.notif-icon { font-size: 1rem; flex-shrink: 0; margin-top: 2px; }
.notif-body { flex: 1; }
.notif-msg { font-size: .8rem; color: #374151; line-height: 1.4; margin: 0; }
.notif-time { font-size: .7rem; color: #9ca3af; margin-top: 2px; display: block; }
</style>
