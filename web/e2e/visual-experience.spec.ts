import {test,expect} from '@playwright/test';
test.use({baseURL:'http://127.0.0.1:5173',viewport:{width:1440,height:900}});
test('regions, goods, organizations and failed activity remain observable',async({page})=>{
 const person={Id:'person',Kind:'Person',Location:{Region:'east',X:'1',Y:'1'},Health:'100',Satiety:'80',Energy:'80'};
 const company=(Id:string,Region:string)=>({Id,Kind:'Company',Location:{Region,X:'1',Y:'1'}});
 const snapshot={Turn:'1',Regions:[{Id:'east'},{Id:'west'}],Entities:[person,company('mill','east'),company('forge','west')],Accounts:{person:{Cash:'10'},mill:{Cash:'200'},forge:{Cash:'300'}},Jobs:[],Lots:[{Id:'ore-lot',Type:'iron-ore',Quantity:'7',Location:{Tile:{Region:'west',X:'1',Y:'1'}}}],ItemTypes:[{Id:'iron-ore',Tag:'ore'}],Markets:[{Region:'east',Tag:'food',ReferencePrice:'100'},{Region:'west',Tag:'ore',ReferencePrice:'250'}],Environment:{Cells:[],Buildings:[],Surfaces:[]}};
 const history={Input:{People:[{Person:'person',Action:{Kind:'Labor'},Reason:'工资不足'}]},Decisions:[],Settlement:{Results:[{Subject:'person',Kind:'action',Success:false,Reason:'工资不足'}],Reservations:[]}};
 await page.addInitScript(()=>localStorage.setItem('eco-game-run','fixture'));
 await page.route('**/game/start',r=>r.fulfill({json:{Name:'测试世界'}}));
 await page.route('**/runs/**',r=>r.fulfill({json:r.request().url().includes('/snapshot')?snapshot:r.request().url().includes('/history')?{Records:[{Metrics:{Turn:'0'},History:history}]}:{Turn:'1',Running:false,Speed:'1'}}));
 await page.goto('/');await expect(page.locator('canvas')).toBeVisible();
 await expect(page.locator('.resources')).toContainText('5.00 元');
 await page.getByRole('button',{name:/居民/}).click();await expect(page.locator('.resident-list')).toContainText('劳动 · 未完成');await page.keyboard.press('Escape');
 await page.getByLabel('观察区域').selectOption('west');await page.getByLabel('观察物品').selectOption('ore');
 await expect(page.locator('.resources')).toContainText('7');await expect(page.locator('.resources')).toContainText('2.50 元');
 await page.getByRole('button',{name:/经济/}).click();await expect(page.locator('.drawer')).toContainText('组织 · 2');await expect(page.locator('.drawer')).toContainText('forge');
 await page.screenshot({path:'web/test-results/f11-multi-world.png'});
});
