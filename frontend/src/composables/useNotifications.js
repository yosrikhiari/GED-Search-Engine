import { reactive } from 'vue'

const state = reactive({
  notifications: [],
  listeners: []
})

export function useNotifications() {
  return {
    notifications: state.notifications,
    unreadCount: () => state.notifications.length,
    notify(message, type = 'info', icon = '🔔') {
      state.notifications.unshift({
        id: Date.now() + Math.random(),
        message,
        type,
        icon,
        time: new Date()
      })
      if (state.notifications.length > 50) state.notifications.pop()
      state.listeners.forEach(fn => fn(state.notifications))
    },
    clear() {
      state.notifications = []
    }
  }
}

window.__gedNotify = (message, type, icon) => {
  const { notify } = useNotifications()
  notify(message, type, icon)
}
