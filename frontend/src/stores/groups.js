import { defineStore } from 'pinia'
import { ref } from 'vue'
import { groups } from '@/api.js'

export const useGroupsStore = defineStore('groups', () => {
  const groupsList = ref([])
  const currentGroup = ref(null)
  const isLoading = ref(false)
  const error = ref(null)

  async function fetchGroups() {
    isLoading.value = true
    error.value = null
    
    try {
      const data = await groups.list()
      
      if (data) {
        groupsList.value = data.groups || data || []
      } else {
        error.value = 'Failed to fetch groups'
      }
    } catch (e) {
      error.value = e.message || 'Network error'
    } finally {
      isLoading.value = false
    }
  }

  async function fetchGroup(id) {
    isLoading.value = true
    error.value = null
    
    try {
      const data = await groups.get(id)
      
      if (data) {
        currentGroup.value = data
        return data
      } else {
        error.value = 'Failed to fetch group'
        return null
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return null
    } finally {
      isLoading.value = false
    }
  }

  async function createGroup(groupData) {
    isLoading.value = true
    error.value = null
    
    try {
      const data = await groups.create(groupData)
      
      if (data) {
        groupsList.value.push(data)
        return { success: true, data }
      } else {
        error.value = 'Failed to create group'
        return { success: false, error: error.value }
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return { success: false, error: error.value }
    } finally {
      isLoading.value = false
    }
  }

  async function updateGroup(id, groupData) {
    isLoading.value = true
    error.value = null
    
    try {
      const data = await groups.update(id, groupData)
      
      if (data) {
        const index = groupsList.value.findIndex(g => g.id === id)
        if (index !== -1) {
          groupsList.value[index] = data
        }
        if (currentGroup.value?.id === id) {
          currentGroup.value = data
        }
        return { success: true, data }
      } else {
        error.value = 'Failed to update group'
        return { success: false, error: error.value }
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return { success: false, error: error.value }
    } finally {
      isLoading.value = false
    }
  }

  async function deleteGroup(id) {
    isLoading.value = true
    error.value = null
    
    try {
      const data = await groups.delete(id)
      
      if (data !== null) {
        groupsList.value = groupsList.value.filter(g => g.id !== id)
        if (currentGroup.value?.id === id) {
          currentGroup.value = null
        }
        return { success: true }
      } else {
        error.value = 'Failed to delete group'
        return { success: false, error: error.value }
      }
    } catch (e) {
      error.value = e.message || 'Network error'
      return { success: false, error: error.value }
    } finally {
      isLoading.value = false
    }
  }

  async function addDocumentsToGroup(groupId, documentIds) {
    try {
      const data = await groups.addDocuments(groupId, { documentIds })
      
      if (data) {
        return { success: true, data }
      } else {
        return { success: false, error: 'Failed to add documents' }
      }
    } catch (e) {
      return { success: false, error: e.message }
    }
  }

  async function assignUserToGroup(groupId, userId, permission, expiresAt = null) {
    try {
      const data = await groups.assign(groupId, {
        userId,
        permission,
        expiresAt
      })
      
      if (data) {
        return { success: true, data }
      } else {
        return { success: false, error: 'Failed to assign user' }
      }
    } catch (e) {
      return { success: false, error: e.message }
    }
  }

  async function revokeUserFromGroup(groupId, assignmentId) {
    try {
      const data = await groups.revokeAssignment(groupId, assignmentId)
      
      if (data !== null) {
        return { success: true }
      } else {
        return { success: false, error: 'Failed to revoke user' }
      }
    } catch (e) {
      return { success: false, error: e.message }
    }
  }

  function clearError() {
    error.value = null
  }

  return {
    groupsList,
    currentGroup,
    isLoading,
    error,
    fetchGroups,
    fetchGroup,
    createGroup,
    updateGroup,
    deleteGroup,
    addDocumentsToGroup,
    assignUserToGroup,
    revokeUserFromGroup,
    clearError
  }
})
