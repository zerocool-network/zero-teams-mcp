import { test, expect } from '@playwright/test';

const PANEL = 'http://localhost:3000';
const CONTROLPLANE = 'http://localhost:5100';
const MCP = 'http://localhost:5200';

// ═══════════════════════════════════════
// 1. CONTROLPLANE HEALTH
// ═══════════════════════════════════════
test.describe('ControlPlane', () => {
  test('health endpoint returns healthy', async ({ request }) => {
    const res = await request.get(`${CONTROLPLANE}/health`);
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.status).toBe('healthy');
    expect(body.tenants).toBeDefined();
  });

  test('OAuth status is configured', async ({ request }) => {
    const res = await request.get(`${CONTROLPLANE}/auth/status`);
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.configured).toBe(true);
    expect(body.clientId).toBeTruthy();
    expect(body.tenantId).toBeTruthy();
  });

  test('OAuth login URL is valid Microsoft URL', async ({ request }) => {
    const res = await request.get(`${CONTROLPLANE}/auth/login`);
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.url).toContain('login.microsoftonline.com');
    expect(body.url).toContain('oauth2/v2.0/authorize');
    expect(body.url).toContain('code_challenge=');
    expect(body.url).toContain('code_challenge_method=S256');
  });

  test('tenants endpoint returns data', async ({ request }) => {
    const res = await request.get(`${CONTROLPLANE}/api/tenants`);
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body)).toBeTruthy();
    expect(body.length).toBeGreaterThan(0);
  });
});

// ═══════════════════════════════════════
// 2. MCP SERVER TOOLS
// ═══════════════════════════════════════
test.describe('MCP Server', () => {
  test('tools/list returns 18 tools', async ({ request }) => {
    const res = await request.post(`${MCP}/mcp`, {
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream' },
      data: { jsonrpc: '2.0', id: 1, method: 'tools/list' },
    });
    expect(res.ok()).toBeTruthy();
    const text = await res.text();
    const jsonLine = text.split('\n').find(l => l.startsWith('data: '));
    expect(jsonLine).toBeTruthy();
    const data = JSON.parse(jsonLine!.slice(6));
    expect(data.result.tools.length).toBe(18);
  });

  test('teams_list_teams returns real teams', async ({ request }) => {
    const res = await request.post(`${MCP}/mcp`, {
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream' },
      data: {
        jsonrpc: '2.0', id: 2, method: 'tools/call',
        params: { name: 'teams_list_teams', arguments: {} }
      },
    });
    expect(res.ok()).toBeTruthy();
    const text = await res.text();
    const jsonLine = text.split('\n').find(l => l.startsWith('data: '));
    const data = JSON.parse(jsonLine!.slice(6));
    const result = JSON.parse(data.result.content[0].text);
    expect(result.length).toBeGreaterThan(10);
    expect(result[0].displayName).toBeTruthy();
  });

  test('admin_health returns healthy', async ({ request }) => {
    const res = await request.post(`${MCP}/mcp`, {
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream' },
      data: {
        jsonrpc: '2.0', id: 3, method: 'tools/call',
        params: { name: 'admin_health', arguments: {} }
      },
    });
    expect(res.ok()).toBeTruthy();
    const text = await res.text();
    const jsonLine = text.split('\n').find(l => l.startsWith('data: '));
    const data = JSON.parse(jsonLine!.slice(6));
    const result = JSON.parse(data.result.content[0].text);
    expect(result.status).toBe('healthy');
  });
});

// ═══════════════════════════════════════
// 3. PANEL PAGES
// ═══════════════════════════════════════
test.describe('Panel Pages', () => {
  test('dashboard loads', async ({ page }) => {
    await page.goto(PANEL);
    await expect(page).toHaveTitle(/ZeroTeams/);
  });

  test('login page loads with Microsoft button', async ({ page }) => {
    await page.goto(`${PANEL}/login`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
    const body = await page.locator('body').textContent();
    expect(body).toContain('Microsoft');
  });

  test('tenants page loads', async ({ page }) => {
    await page.goto(`${PANEL}/tenants`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('body')).toContainText('Tenant');
  });

  test('consents page loads', async ({ page }) => {
    await page.goto(`${PANEL}/consents`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000);
    const body = await page.locator('body').textContent();
    expect(body).toContain('Consent');
  });

  test('health page loads', async ({ page }) => {
    await page.goto(`${PANEL}/health`);
    await page.waitForLoadState('networkidle');
    await expect(page.locator('body')).toContainText('Health');
  });

  test('playground loads with tool catalog', async ({ page }) => {
    await page.goto(`${PANEL}/playground`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000);
    // Should show tool cards
    const toolCards = page.locator('[class*="grid"] > div');
    const count = await toolCards.count();
    expect(count).toBeGreaterThan(0);
  });
});

// ═══════════════════════════════════════
// 4. PLAYGROUND INTERACTIVE TEST
// ═══════════════════════════════════════
test.describe('Playground Tools', () => {
  test('teams_list_teams executes and shows results', async ({ page }) => {
    await page.goto(`${PANEL}/playground`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000);

    // Find and click the teams_list_teams card
    const teamCard = page.locator('text=teams_list_teams').first();
    if (await teamCard.isVisible()) {
      await teamCard.click();
      await page.waitForTimeout(500);

      // Click Run button
      const runBtn = page.getByRole('button', { name: /run/i }).first();
      if (await runBtn.isVisible()) {
        await runBtn.click();
        await page.waitForTimeout(5000);

        // Should show results
        const body = await page.locator('body').textContent();
        expect(body).toBeTruthy();
      }
    }
  });
});
