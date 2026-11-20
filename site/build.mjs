// Builds index.html: src/shell.html with src/page.css, src/page.html and every src/NN_*.js (in name order) inlined.
// The sources are plain scripts that share one module scope, so the page stays a single file that opens from disk.
//   node site/build.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url)), src = path.join(here, 'src');
const read = f => (fs.existsSync(path.join(src, f)) ? fs.readFileSync(path.join(src, f), 'utf8') : '');
const js = fs.readdirSync(src).filter(f => /^\d\d_.*\.js$/.test(f)).sort().map(f => `// ======== ${f} ========\n${read(f)}`).join('\n');
const out = read('shell.html').replace('/*__CSS__*/', () => read('page.css')).replace('<!--__HTML__-->', () => read('page.html')).replace('/*__SRC__*/', () => js);
fs.writeFileSync(path.join(here, 'index.html'), out);
console.log(`index.html  ${(out.length / 1024).toFixed(0)} KB`);
