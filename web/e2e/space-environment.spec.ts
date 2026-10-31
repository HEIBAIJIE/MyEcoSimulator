import {test,expect} from '@playwright/test';
for(const variant of ['space','environment']) test(`${variant} regions and committed environment`,async({page})=>{
 const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('/');await page.getByLabel('场景',{exact:true}).selectOption(variant);await page.getByRole('button',{name:'创建运行',exact:true}).click();
 await expect(page.locator('canvas')).toBeVisible();await page.getByLabel('Region',{exact:true}).selectOption('R1');
 await page.getByText('Regional markets, transport and environment',{exact:true}).click();await expect(page.getByText('Regional markets, transport and environment',{exact:true}).locator('..')).toContainText('200');
 await page.getByRole('button',{name:'单步',exact:true}).click();await expect(page.getByRole('status')).toContainText('边界 1');
 await page.reload();await expect(page.locator('canvas')).toBeVisible();expect(errors).toEqual([]);
});
