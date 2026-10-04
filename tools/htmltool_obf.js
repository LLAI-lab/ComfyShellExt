// Verbatim port of the Gilbert-curve pixel shuffle used by 图片混淆批量版V0.0.html
// (the inline web worker), operating on raw 4-bytes-per-pixel dumps.
'use strict';
const fs = require('fs');

function gilbert2d(width, height) {
    const coordinates = new Int32Array(width * height); // packed x + y * width
    let index = 0;
    function generate2d(x, y, ax, ay, bx, by) {
        const w = Math.abs(ax + ay);
        const h = Math.abs(bx + by);
        const dax = Math.sign(ax), day = Math.sign(ay);
        const dbx = Math.sign(bx), dby = Math.sign(by);
        if (h === 1) { for (let i = 0; i < w; i++) { coordinates[index++] = x + y * width; x += dax; y += day; } return; }
        if (w === 1) { for (let i = 0; i < h; i++) { coordinates[index++] = x + y * width; x += dbx; y += dby; } return; }
        let ax2 = Math.floor(ax / 2), ay2 = Math.floor(ay / 2);
        let bx2 = Math.floor(bx / 2), by2 = Math.floor(by / 2);
        if (2 * w > 3 * h) {
            if ((Math.abs(ax2 + ay2) % 2) && (w > 2)) { ax2 += dax; ay2 += day; }
            generate2d(x, y, ax2, ay2, bx, by);
            generate2d(x + ax2, y + ay2, ax - ax2, ay - ay2, bx, by);
        } else {
            if ((Math.abs(bx2 + by2) % 2) && (h > 2)) { bx2 += dbx; by2 += dby; }
            generate2d(x, y, bx2, by2, ax2, ay2);
            generate2d(x + bx2, y + by2, ax, ay, bx - bx2, by - by2);
            generate2d(x + (ax - dax) + (bx2 - dbx), y + (ay - day) + (by2 - dby),
                -bx2, -by2, -(ax - ax2), -(ay - ay2));
        }
    }
    if (width >= height) generate2d(0, 0, width, 0, 0, height);
    else generate2d(0, 0, 0, height, width, 0);
    return coordinates;
}

const [, , width, height, type, inFile, outFile] = process.argv;
const w = Number(width), h = Number(height);
const buf = fs.readFileSync(inFile);
const totalPixels = w * h;
const offset = Math.floor((Math.sqrt(5) - 1) / 2 * totalPixels) % totalPixels;
const curve = gilbert2d(w, h);
if (curve.length !== totalPixels) throw new Error('curve incomplete: ' + curve.length);
const out = Buffer.alloc(buf.length);
for (let i = 0; i < totalPixels; i++) {
    const p1 = type === 'encrypt' ? curve[i] : curve[(i + offset) % totalPixels];
    const p2 = type === 'encrypt' ? curve[(i + offset) % totalPixels] : curve[i];
    const src = 4 * p1, dst = 4 * p2;
    out[dst] = buf[src]; out[dst + 1] = buf[src + 1]; out[dst + 2] = buf[src + 2]; out[dst + 3] = buf[src + 3];
}
fs.writeFileSync(outFile, out);
console.log(type, w + 'x' + h, 'offset=' + offset, 'total=' + totalPixels);
