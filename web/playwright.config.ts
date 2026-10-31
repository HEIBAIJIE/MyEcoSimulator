import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./e2e',timeout:240000,use:{baseURL:'http://localhost:5184',headless:true},workers:1});
