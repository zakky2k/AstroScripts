// SSR the chart component against the design probe's committed JSON payloads
// and diff it against the reviewed reference SVGs. If the port is faithful,
// every element and attribute matches render.py's output (modulo React's
// attribute serialisation).
//
// Run:  node --experimental-strip-types scripts/parity.ts   (from web/app)

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join, dirname } from "node:path";
import { renderToStaticMarkup } from "react-dom/server";
import React from "react";
// Extension kept explicit: the tsc build emits .js and node ESM needs it.
import { NightChart } from "../components/night-chart/NightChart.js";

const here = dirname(fileURLToPath(import.meta.url));
// .parity-build/scripts -> up to web/app, then repo root (web/app is two deep).
const root = join(here, "..", "..", "..", "..");

const CASES: [string, string][] = [
  ["prague", "chart-prague.svg"],
  ["summer", "chart-summer.svg"],
  ["sydney", "chart-sydney.svg"],
];

/** Normalise a markup string so React and Python serialisation agree.
 *  Numeric attributes are rounded to the render.py precision (2 dp for
 *  geometry, plain float for opacities) because React emits "0.3" where
 *  Python wrote "0.30", and attribute order is sorted away. */
function normAttr(name: string, v: string): string {
  const num = /^-?\d+(\.\d+)?$/.test(v);
  if (!num) return v;
  const n = parseFloat(v);
  // Geometry attrs in render.py are "%.2f"; opacities/widths are bare floats.
  if (/^-?\d+\.\d{3}$/.test(v)) return v; // JSON-sourced values: exact match
  if (["x", "y", "x1", "x2", "y1", "y2", "width", "height"].includes(name)) {
    // "%.2f" of the reference string: strip to 2 dp with Python's half-even
    // rule. The token is render.py's own decimal, so only trailing digits
    // need dropping (a ".xx5" there rounded even when written).
    const m = /^(-?)(\d+)(?:\.(\d+))?$/.exec(v)!;
    let frac = m[3] ?? "";
    if (frac.length > 2) {
      const rest = frac.slice(2);
      if (/^50*$/.test(rest) && +frac[1] % 2 === 1) frac = String(+frac.slice(0, 2) + 1).padStart(2, "0");
      else frac = frac.slice(0, 2);
    }
    return `${m[1]}${m[2]}.${frac.padEnd(2, "0")}`;
  }
  return String(n);
}
function tokenize(markup: string): string[] {
  const out: string[] = [];
  // Values may contain spaces ("1 6", "M 0,-9 ..."), so match to the closing quote.
  const tagRe = /<(\w+)((?:[^>"']|"[^"]*")*?)(\/?)>/g;
  let m: RegExpExecArray | null;
  while ((m = tagRe.exec(markup))) {
    const [, tag, attrsRaw] = m;
    if (tag === "g") continue; // React's grouping wrappers carry no semantics
    const attrs: string[] = [];
    const attrRe = /(\w[\w-]*)="([^"]*)"/g;
    let a: RegExpExecArray | null;
    while ((a = attrRe.exec(attrsRaw))) {
      attrs.push(`${a[1]}=${normAttr(a[1], a[2])}`);
    }
    attrs.sort();
    out.push(`<${tag} ${attrs.join(" ")}/>`);
  }
  // text content matters too (labels); Python writes degrees as &#176;
  const textRe = />([^<>]+)</g;
  while ((m = textRe.exec(markup))) {
    const t = m[1].replace(/&#176;/g, "°").trim();
    if (t) out.push(`text:${t}`);
  }
  return out.sort();
}

let failed = 0;
for (const [name, ref] of CASES) {
  const payload = JSON.parse(
    readFileSync(join(root, "design", "output", `night-${name}.json`), "utf8"),
  );
  // The reference SVGs carry no role/aria-label (accessibility was added in
  // the app port); strip them from my markup before diffing geometry.
  const mine = renderToStaticMarkup(React.createElement(NightChart as any, { payload }))
    .replace(/ role="img" aria-label="[^"]*"/, "");
  const theirs = readFileSync(join(root, "design", "output", ref), "utf8");
  const a = tokenize(mine);
  const b = tokenize(theirs);
  const missing = b.filter((t) => !a.includes(t));
  const extra = a.filter((t) => !b.includes(t));
  // Multiset compare: count occurrences.
  const count = (arr: string[]) => {
    const map = new Map<string, number>();
    for (const t of arr) map.set(t, (map.get(t) ?? 0) + 1);
    return map;
  };
  const ca = count(a), cb = count(b);
  // Diff by tag group: a near-miss on one attribute shows up as an extra and
  // a missing token with the same tag, which is far easier to read than a
  // flat list.
  const diffs: string[] = [];
  const keys = new Set([...ca.keys(), ...cb.keys()]);
  for (const k of keys) {
    const d = (cb.get(k) ?? 0) - (ca.get(k) ?? 0);
    if (d > 0) diffs.push(`missing ${d}x ${k}`);
    if (d < 0) diffs.push(`extra   ${-d}x ${k}`);
  }
  if (diffs.length === 0) {
    console.log(`PASS  ${name}: ${a.length} tokens identical`);
  } else {
    failed++;
    // Pair missing/extra by tag so each near-miss reads as one line.
    const miss = diffs.filter((d) => d.startsWith("missing")).map((d) => d.slice(10));
    const extr = diffs.filter((d) => d.startsWith("extra")).map((d) => d.slice(10));
    console.log(`FAIL  ${name}: ${miss.length} mismatched tokens`);
    const byTag = (arr: string[]) => {
      const m = new Map<string, string[]>();
      for (const t of arr) {
        const tag = /^<(\w+)/.exec(t)?.[1] ?? "text";
        (m.get(tag) ?? m.set(tag, []).get(tag)!).push(t);
      }
      return m;
    };
    const bm = byTag(miss), be = byTag(extr);
    for (const tag of new Set([...bm.keys(), ...be.keys()])) {
      const L = bm.get(tag) ?? [], R = be.get(tag) ?? [];
      for (let i = 0; i < Math.max(L.length, R.length); i++) {
        console.log(`  ref : ${L[i] ?? ""}`);
        console.log(`  mine: ${R[i] ?? ""}`);
        if (i >= 5) { console.log("  ..."); break; }
      }
    }
  }
}
process.exit(failed ? 1 : 0);
