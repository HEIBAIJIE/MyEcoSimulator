import {test,expect} from '@playwright/test';
test.use({baseURL:'http://127.0.0.1:5173'});
test('cleared run returns to new game',async({page})=>{
 await page.addInitScript(()=>localStorage.setItem('eco-game-run','deleted-run'));
 await page.goto('/');
 await expect(page.getByRole('button',{name:'开始新的生活'})).toBeVisible();
 await expect.poll(()=>page.evaluate(()=>localStorage.getItem('eco-game-run'))).toBeNull();
 await expect(page.getByRole('alert')).toHaveCount(0);
 await page.getByRole('button',{name:'开始新的生活'}).click();
 await expect(page.locator('canvas')).toBeVisible();
});
