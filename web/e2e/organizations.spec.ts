import {test,expect} from '@playwright/test';
test('organizations filters and committed results',async({page})=>{
 const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('/');await page.locator('select').first().selectOption('organizations');await page.locator('section').first().getByRole('button').first().click();
 await expect(page.locator('canvas')).toBeVisible({timeout:30000});
 await page.getByRole('button',{name:'G',exact:true}).click();
 await page.getByLabel('Company',{exact:true}).selectOption('G');await page.getByLabel('Job',{exact:true}).selectOption({index:1});
 await expect(page.getByRole('button',{name:'P00',exact:true})).toBeVisible();
 await page.locator('section').first().getByRole('button').nth(2).click();
 await expect(page.getByRole('status')).toContainText('1');
 await page.reload();await expect(page.locator('canvas')).toBeVisible({timeout:30000});expect(errors).toEqual([]);
});
