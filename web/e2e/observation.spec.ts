import {test,expect} from '@playwright/test';
import fs from 'node:fs/promises';
test('六场景真实浏览器观察与720回合导出',async({page,request})=>{
 test.setTimeout(1200000); const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
 for(const variant of ['baseline','no-resident-cash','low-capital','productivity','demand','pause']){
 console.log('scenario',variant); await page.goto('/');await page.getByLabel('场景',{exact:true}).selectOption(variant);await page.getByRole('button',{name:'创建运行',exact:true}).click();
 await expect(page.locator('canvas')).toBeVisible({timeout:30000});const id=await page.getByLabel('运行标识').inputValue();
 await page.getByRole('button',{name:'单步',exact:true}).click();await expect(page.getByRole('status')).toContainText('边界 1');
 for(let i=0;i<20;i++){await page.getByRole('button',{name:P,exact:true}).click();}await page.getByRole('button',{name:'P19',exact:true}).click();await expect(page.getByRole('heading',{name:/P19 · 边界 1/})).toBeVisible();
 await page.getByLabel('缩放',{exact:true}).fill('1.2');await page.getByLabel('速度',{exact:true}).fill('1000');await page.getByRole('button',{name:'设置速度'}).click();await page.getByRole('button',{name:'继续',exact:true}).click();
 await expect.poll(async()=> (await (await request.get(`/runs/${id}`)).json()).Turn,{timeout:150000,intervals:[1000]}).toBe('720');
 await page.getByRole('button',{name:'暂停',exact:true}).click();await expect(page.getByRole('status')).toContainText('720');
 await page.getByLabel('subject',{exact:true}).fill('P00');await page.getByLabel('item',{exact:true}).fill('food');await page.getByLabel('market',{exact:true}).fill('R0:food');await page.getByRole('button',{name:'查询',exact:true}).click();await expect(page.getByRole('button',{name:'结算 0',exact:true})).toBeVisible();await page.getByRole('button',{name:'下一页',exact:true}).click();await expect(page.getByRole('button',{name:'结算 20',exact:true})).toBeVisible();await page.getByRole('button',{name:'结算 20',exact:true}).click();await expect(page.getByRole('heading',{name:/P19 · 边界 21/})).toBeVisible();
 await page.getByRole('button',{name:/^订单 /}).first().click();await expect(page.locator('details[open]')).not.toHaveCount(0);
 const downloadPromise=page.waitForEvent('download');await page.getByRole('link',{name:'导出 NDJSON'}).click();const download=await downloadPromise;const path=await download.path();const lines=(await fs.readFile(path!,'utf8')).trim().split('\n');expect(lines.length).toBe(722);expect(JSON.parse(lines[0]).Turn).toBe('720');
 await page.reload();await expect(page.getByRole('status')).toContainText('720');await expect(page.locator('canvas')).toBeVisible({timeout:30000});await page.screenshot({path:`test-results/${variant}.png`,fullPage:true});
 }
 expect(errors).toEqual([]);
});


