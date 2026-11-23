// tsc leaves "@/..." path-alias imports as-is (Next resolves them itself, but
// the plain-Node parity runner cannot). Rewrite them to relative specifiers in
// the emitted .parity-build tree: components/night-chart/X.js -> ../../lib/Y.
import { readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join, relative, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const build = resolve(dirname(fileURLToPath(import.meta.url)), "..", ".parity-build");

function walk(dir) {
  for (const e of readdirSync(dir)) {
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walk(p);
    else if (p.endsWith(".js")) rewrite(p);
  }
}
function rewrite(file) {
  let src = readFileSync(file, "utf8");
  const from = dirname(file);
  src = src.replace(/(from\s+")(@\/[^"]+)(")/g, (_m, a, spec, c) => {
    // "@/lib/palette" -> file under .parity-build/lib/palette.js
    let rel = relative(from, resolve(build, spec.slice(2) + ".js")).replace(/\\/g, "/");
    if (!rel.startsWith(".")) rel = "./" + rel;
    return a + rel + c;
  });
  writeFileSync(file, src);
}
walk(build);
console.log("alias rewrite done");
