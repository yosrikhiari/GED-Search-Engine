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
    path: '/',
    name: 'UserHome',
    component: () => import('../views/User.vue'),
    // FIX 2: Added 'Admin' so admins aren't bounced when landing on /
    meta: { requiresAuth: true, roles: ['Admin', 'Manager', 'User', 'ReadOnly'] }
  },
  {
    path: '/search',
    redirect: '/'
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to, _from, next) => {
  // FIX 1: Auth is cookie-based — there is no ged_token in localStorage.
  //         Check ged_user instead, which LoginView.vue saves on successful login.
  const user = (() => {
    try { return JSON.parse(localStorage.getItem('ged_user') || 'null') }
    catch { return null }
  })()
  const isAuthenticated = !!user

  if (to.meta.requiresAuth && !isAuthenticated) {
    next({ name: 'Login', query: { redirect: to.fullPath } })
    return
  }

  if (to.name === 'Login' && isAuthenticated) {
    next({ name: user.role === 'Admin' ? 'AdminDashboard' : 'UserHome' })
    return
  }

  // Redirect root path to correct dashboard based on role
  if (to.path === '/' && user.role === 'Admin') {
    next({ name: 'AdminDashboard' })
    return
  }

  if (to.meta.roles && isAuthenticated) {
    if (!to.meta.roles.includes(user.role)) {
      if (user.role === 'Admin') {
        next({ name: 'AdminDashboard' })
      } else if (['Manager', 'User', 'ReadOnly'].includes(user.role)) {
        next({ name: 'UserHome' })
      } else {
        localStorage.removeItem('ged_user')
        next({ name: 'Login' })
      }
      return
    }
  }

  next()
})

export default router