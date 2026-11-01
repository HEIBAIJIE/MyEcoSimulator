import {test,expect} from '@playwright/test';
test.use({baseURL:'http://127.0.0.1:5173',viewport:{width:1440,height:900}});
test('committed clock, lighting, recovery and narrow screen',async({page,request})=>{
 await page.goto('/');await page.getByRole('button',{name:'开始新的生活'}).click();await expect(page.locator('canvas')).toBeVisible();
 const id=await page.evaluate(()=>localStorage.getItem('eco-game-run'));
 for(const hour of [0,5,8,17,20,23,24]){
  const status=await (await request.get(`http://localhost:5184/runs/${id}`)).json();
  for(let t=Number(status.Turn);t<hour;t++)await request.post(`http://localhost:5184/runs/${id}/commands`,{data:{RequestId:crypto.randomUUID(),Operation:'step',Speed:null}});
  await expect(page.locator('.timepiece')).toContainText(`第${Math.floor(hour/24)+1}天 ${String(hour%24).padStart(2,'0')}:00`,{timeout:10000});
  await page.screenshot({path:`../artifacts/f12/hour-${hour}.png`});
 }
 await page.reload();await expect(page.locator('.timepiece')).toContainText('第2天 00:00');
 await page.getByRole('button',{name:/居民/}).click();await page.locator('.resident-list button').first().click();
 await page.setViewportSize({width:390,height:844});await page.emulateMedia({reducedMotion:'reduce'});
 await expect(page.locator('.time-hud .time-controls')).toBeVisible();await expect(page.locator('footer.timebar')).toHaveCount(0);
 const clock=await page.locator('.time-hud').boundingBox(),panel=await page.locator('.inspector').boundingBox();expect(clock!.y+clock!.height).toBeLessThanOrEqual(panel!.y);
 await page.screenshot({path:'../artifacts/f12/mobile.png'});await page.keyboard.press('Escape');await expect(page.locator('.inspector')).toHaveCount(0);
});
