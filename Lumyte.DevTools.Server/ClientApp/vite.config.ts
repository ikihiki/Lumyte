import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
export default defineConfig({plugins:[react()],build:{outDir:'../wwwroot',emptyOutDir:true},server:{port:5199,proxy:{'/devtools':{target:'ws://localhost:5198',ws:true},'/health':{target:'http://localhost:5198'}}},test:{environment:'jsdom',setupFiles:'./src/test/setup.ts',css:true,server:{deps:{inline:[/@fluentui/]}}}});
