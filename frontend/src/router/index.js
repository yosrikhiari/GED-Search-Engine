import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('../views/LoginView.vue'),
    meta: { public: true }
  },
  {
    path: '/admin',
    name: 'AdminDashboard',
    component: () => import('../views/Admin.vue'),
    meta: { requiresAuth: true, roles: ['Admin'] }
  },
  {
    // Main user view — full smart search + document viewer (SearchView merged in)
    path: '/',
    name: 'UserHome',
    component: () => import('../views/User.vue'),
    meta: { requiresAuth: true, roles: ['Manager', 'User', 'ReadOnly'] }
  },
  {
    // /search redirects to / — old bookmarks still work
    path: '/search',
    redirect: '/'
  },
  {
    path: '/rag',
    name: 'RAG',
    component: () => import('../views/RagView.vue'),
    meta: { requiresAuth: true }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to, _from, next) => {
  const token = localStorage.getItem('ged_token')
  const isAuthenticated = !!token

  if (to.meta.requiresAuth && !isAuthenticated) {
    next({ name: 'Login', query: { redirect: to.fullPath } })
    return
  }

  if (to.name === 'Login' && isAuthenticated) {
    const user = JSON.parse(localStorage.getItem('ged_user') || '{}')
    next({ name: user.role === 'Admin' ? 'AdminDashboard' : 'UserHome' })
    return
  }

  if (to.meta.roles && isAuthenticated) {
    const user = JSON.parse(localStorage.getItem('ged_user') || '{}')
    if (!to.meta.roles.includes(user.role)) {
      if (user.role === 'Admin') {
        next({ name: 'AdminDashboard' })
      } else if (['Manager', 'User', 'ReadOnly'].includes(user.role)) {
        next({ name: 'UserHome' })
      } else {
        localStorage.removeItem('ged_token')
        localStorage.removeItem('ged_user')
        next({ name: 'Login' })
      }
      return
    }
  }

  next()
})

export default router