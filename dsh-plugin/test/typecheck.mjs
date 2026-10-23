// Optional strict check. All dependencies/cache live in a fresh isolated temp dir.
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
const temporary = mkdtempSync(join(tmpdir(), 'gpulcd-plugin-types-'));
const root = fileURLToPath(new URL('../', import.meta.url));
console.log('Isolated typecheck directory:', temporary);
try {
 const args = ['install', '--prefix', temporary, '--cache', join(temporary,'cache'), '--ignore-scripts', '--no-audit', '--no-fund', 'typescript@5.9.3', '@types/node@24.10.1'];
 const install = spawnSync(process.platform === 'win32' ? 'npm.cmd' : 'npm', args, { cwd:temporary, stdio:'inherit', shell:process.platform === 'win32', env:{...process.env, npm_config_userconfig:join(temporary,'no-user-config'), npm_config_globalconfig:join(temporary,'no-global-config')} });
 if (install.status !== 0) throw new Error('Isolated dependency install failed: ' + install.status);
 const check = spawnSync(process.execPath,[join(temporary,'node_modules/typescript/bin/tsc'),'-p',resolve(root,'tsconfig.json'),'--typeRoots',join(temporary,'node_modules/@types'),'--noEmit'],{cwd:root,stdio:'inherit'});
 if (check.status !== 0) throw new Error('Strict typecheck failed: ' + check.status);
 console.log('Strict TypeScript check passed (no shims)');
} finally { rmSync(temporary,{recursive:true,force:true}); }
