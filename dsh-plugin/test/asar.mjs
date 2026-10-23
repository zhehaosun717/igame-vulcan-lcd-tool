import { openSync, readSync, closeSync } from 'node:fs';
export const defaultArchive = 'C:/Users/Sun_Local/AppData/Local/Programs/DeepSeek Harness/resources/app.asar';
export function readAsar(path, name) {
  const fd = openSync(path, 'r');
  try {
    const b = Buffer.alloc(16); readSync(fd, b, 0, 16, 0);
    const h = Buffer.alloc(b.readUInt32LE(12)); readSync(fd, h, 0, h.length, 16);
    let entry = JSON.parse(h);
    for (const part of name.split('/')) entry = entry.files?.[part];
    if (!entry || entry.unpacked) throw new Error('Packed ASAR entry not found: ' + name);
    if (entry.files) return JSON.stringify(entry);
    const content = Buffer.alloc(Number(entry.size));
    readSync(fd, content, 0, content.length, 8 + b.readUInt32LE(4) + Number(entry.offset));
    return content.toString('utf8');
  } finally { closeSync(fd); }
}
