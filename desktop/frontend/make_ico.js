// 把多尺寸 PNG 打包成一个 .ico（ICO 容器 + PNG 负载，Vista 以上均支持）
const fs = require('fs');
const path = require('path');

const sizes = [16, 24, 32, 48, 64, 128, 256];
const dir = process.argv[2] || '.';
const out = process.argv[3] || 'app.ico';

const images = sizes.map(function (s) {
  const p = path.join(dir, 'ico_' + s + '.png');
  return { size: s, data: fs.readFileSync(p) };
});

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);              // reserved
header.writeUInt16LE(1, 2);              // type: icon
header.writeUInt16LE(images.length, 4);  // count

const entries = [];
let offset = 6 + images.length * 16;

images.forEach(function (img) {
  const e = Buffer.alloc(16);
  e.writeUInt8(img.size >= 256 ? 0 : img.size, 0);   // 0 表示 256
  e.writeUInt8(img.size >= 256 ? 0 : img.size, 1);
  e.writeUInt8(0, 2);        // 调色板数
  e.writeUInt8(0, 3);        // 保留
  e.writeUInt16LE(1, 4);     // planes
  e.writeUInt16LE(32, 6);    // 位深
  e.writeUInt32LE(img.data.length, 8);
  e.writeUInt32LE(offset, 12);
  offset += img.data.length;
  entries.push(e);
});

fs.writeFileSync(out, Buffer.concat([header].concat(entries, images.map(function (i) { return i.data; }))));
console.log('wrote ' + out + ' (' + fs.statSync(out).size + ' bytes, ' + images.length + ' sizes)');
