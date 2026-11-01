import {test,expect} from '@playwright/test';
test.use({baseURL:'http://127.0.0.1:5173',viewport:{width:1440,height:900}});
test('town game interactions and presentation',async({page})=>{
 const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('/');await expect(page.getByRole('button',{name:'开始新的生活'})).toBeEnabled();await page.screenshot({path:'web/test-results/f10-start.png'});
 await page.getByRole('button',{name:'开始新的生活'}).click();await expect(page.locator('canvas')).toBeVisible();await expect(page.getByText('食品存量',{exact:true})).toBeVisible();
 await page.screenshot({path:'web/test-results/f10-world.png'});
 await page.getByRole('button',{name:'单步'}).click();await expect(page.locator('.date')).toContainText('回合 1');
 await page.getByRole('button',{name:/居民/}).click();await page.locator('.resident-list button').first().click();await expect(page.locator('.inspector')).toContainText('林舟');await expect(page.locator('meter')).toHaveCount(3);await page.screenshot({path:'web/test-results/f10-resident.png'});
 await page.getByRole('button',{name:'查看行动与资产凭据'}).click();await expect(page.locator('pre')).toContainText('决策');await page.keyboard.press('Escape');
 await page.getByRole('button',{name:/经济/}).click();await expect(page.locator('.trend')).toHaveCount(5);await page.screenshot({path:'web/test-results/f10-economy.png'});await page.keyboard.press('Escape');
 await page.mouse.move(650,480);await page.mouse.wheel(0,-400);await page.mouse.down();await page.mouse.move(730,530,{steps:8});await page.mouse.up();await page.keyboard.press('h');
 await page.getByRole('button',{name:'继续',exact:true}).click();await expect(page.getByRole('button',{name:'暂停',exact:true})).toBeVisible();await page.getByRole('button',{name:'暂停',exact:true}).click();
 const saved=await page.evaluate(()=>localStorage.getItem('eco-game-run'));await page.reload();await expect(page.locator('canvas')).toBeVisible();expect(await page.evaluate(()=>localStorage.getItem('eco-game-run'))).toBe(saved);
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:'web/test-results/f10-mobile.png'});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();expect(errors).toEqual([]);
});
