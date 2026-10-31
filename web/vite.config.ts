import {defineConfig} from 'vite';
export default defineConfig({server:{proxy:{'/runs':'http://localhost:5184'}},build:{outDir:'../src/EcoSimulator.Api/wwwroot',emptyOutDir:true}});
