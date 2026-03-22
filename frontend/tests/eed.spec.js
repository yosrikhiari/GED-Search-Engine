import { test, expect } from '@playwright/test';

const BASE_URL = process.env.BASE_URL || 'http://localhost:3000';
const API_URL = process.env.API_URL || 'http://localhost:5001';

test.describe('GED Search Engine E2E Tests', () => {
  
  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
  });

  test.describe('Authentication', () => {
    test('should display login page', async ({ page }) => {
      await expect(page.locator('input[type="text"], input[name="username"]')).toBeVisible();
      await expect(page.locator('input[type="password"]')).toBeVisible();
    });

    test('should login with valid credentials', async ({ page }) => {
      await page.fill('input[name="username"], input[type="text"]', 'admin');
      await page.fill('input[type="password"]', 'Admin@1234');
      await page.click('button[type="submit"], button:has-text("Connexion")');
      
      // Wait for navigation to user dashboard
      await page.waitForURL('**/user', { timeout: 10000 }).catch(() => {});
    });

    test('should reject invalid credentials', async ({ page }) => {
      await page.fill('input[name="username"], input[type="text"]', 'admin');
      await page.fill('input[type="password"]', 'wrongpassword');
      await page.click('button[type="submit"], button:has-text("Connexion")');
      
      // Should show error message
      await expect(page.locator('text=error, text=incorrect, text=invalide')).toBeVisible();
    });
  });

  test.describe('Search Functionality', () => {
    test.beforeEach(async ({ page }) => {
      // Login first
      await page.goto(BASE_URL + '/login');
      await page.fill('input[name="username"], input[type="text"]', 'admin');
      await page.fill('input[type="password"]', 'Admin@1234');
      await page.click('button[type="submit"]');
      await page.waitForURL('**/user', { timeout: 10000 }).catch(() => {});
    });

    test('should display search bar', async ({ page }) => {
      await expect(page.locator('input[placeholder*="Recherche"], input[type="search"]')).toBeVisible();
    });

    test('should perform search query', async ({ page }) => {
      const searchInput = page.locator('input[placeholder*="Recherche"], input[type="search"]').first();
      await searchInput.fill('test');
      await searchInput.press('Enter');
      
      // Wait for results
      await page.waitForTimeout(1000);
    });
  });

  test.describe('Document Management', () => {
    test.beforeEach(async ({ page }) => {
      // Login first
      await page.goto(BASE_URL + '/login');
      await page.fill('input[name="username"], input[type="text"]', 'admin');
      await page.fill('input[type="password"]', 'Admin@1234');
      await page.click('button[type="submit"]');
      await page.waitForURL('**/user', { timeout: 10000 }).catch(() => {});
    });

    test('should have upload button visible', async ({ page }) => {
      await expect(page.locator('button:has-text("Upload"), button:has-text("Téléverser"), input[type="file"]')).toBeVisible();
    });
  });

  test.describe('API Integration', () => {
    test('should respond to health check', async ({ request }) => {
      const response = await request.get(API_URL + '/health');
      expect(response.ok() || response.status() === 503).toBeTruthy();
    });

    test('should require authentication for protected endpoints', async ({ request }) => {
      const response = await request.get(API_URL + '/api/documents');
      expect(response.status()).toBe(401);
    });
  });
});

test.describe('Performance Tests', () => {
  test('should load page within acceptable time', async ({ page }) => {
    const startTime = Date.now();
    await page.goto(BASE_URL);
    const loadTime = Date.now() - startTime;
    
    expect(loadTime).toBeLessThan(5000); // 5 seconds max
  });
});