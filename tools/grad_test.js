// Gradient heuristic validation on real pixels: obfuscated vs decrypted vs natural.
'use strict';
const fs = require('fs');
const A = process.env.LOCALAPPDATA + '/Temp/obfdet/';

function grad(file, W, H) {
  const b = fs.readFileSync(file);
  let d = 0, n = 0;
  for (let y = 0; y < H; y += 6) for (let x = 0; x < W - 1; x += 6) {
    const i = (y * W + x) * 4, j = (y * W + x + 1) * 4;
    d += Math.abs((b[i] + 2 * b[i + 1] + b[i + 2]) - (b[j] + 2 * b[j + 1] + b[j + 2])) / 4; n++;
  }
  for (let y = 0; y < H - 1; y += 6) for (let x = 0; x < W; x += 6) {
    const i = (y * W + x) * 4, j = ((y + 1) * W + x) * 4;
    d += Math.abs((b[i] + 2 * b[i + 1] + b[i + 2]) - (b[j] + 2 * b[j + 1] + b[j + 2])) / 4; n++;
  }
  return d / n;
}

// args: <label:W:H:file> ...
for (const spec of process.argv.slice(2)) {
  const [label, w, h, file] = spec.split('|');
  console.log(label.padEnd(24), grad(file, Number(w), Number(h)).toFixed(1));
}
