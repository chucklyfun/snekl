import fs from 'node:fs';
import path from 'node:path';

const map = {
  Subforums: 'Subnests',
  subforums: 'subnests',
  Subforum: 'Subnest',
  subforum: 'subnest',
  Forums: 'Nests',
  forums: 'nests',
  Forum: 'Nest',
  forum: 'nest',
};

const wordRe =
  /(?<![A-Za-z0-9_./-])(Subforums|subforums|Subforum|subforum|Forums|forums|Forum|forum)(?![A-Za-z0-9_/-])(?!\.[A-Za-z0-9_])(?!:)/g;

function renameProse(text) {
  return text.replace(wordRe, (m) => map[m]);
}

function renameTsString(content) {
  if (content === 'forum') return content;
  return renameProse(content);
}

function scanStrings(src, rewrite) {
  let out = '';
  let i = 0;
  let changed = 0;
  while (i < src.length) {
    const c = src[i];
    const next = src[i + 1];
    if (c === '/' && next === '/') {
      const end = src.indexOf('\n', i);
      const stop = end === -1 ? src.length : end;
      out += src.slice(i, stop);
      i = stop;
      continue;
    }
    if (c === '/' && next === '*') {
      const end = src.indexOf('*/', i + 2);
      const stop = end === -1 ? src.length : end + 2;
      out += src.slice(i, stop);
      i = stop;
      continue;
    }
    if (c === "'" || c === '"') {
      const quote = c;
      let j = i + 1;
      let body = '';
      while (j < src.length) {
        if (src[j] === '\\') {
          body += src[j] + (src[j + 1] ?? '');
          j += 2;
          continue;
        }
        if (src[j] === quote) break;
        body += src[j];
        j++;
      }
      const nextBody = rewrite(body);
      if (nextBody !== body) changed++;
      out += quote + nextBody + (src[j] === quote ? quote : '');
      i = src[j] === quote ? j + 1 : j;
      continue;
    }
    if (c === '`') {
      let j = i + 1;
      let chunk = '';
      const parts = [];
      while (j < src.length) {
        if (src[j] === '\\') {
          chunk += src[j] + (src[j + 1] ?? '');
          j += 2;
          continue;
        }
        if (src[j] === '`') break;
        if (src[j] === '$' && src[j + 1] === '{') {
          parts.push({ type: 'text', value: chunk });
          chunk = '';
          let depth = 1;
          j += 2;
          let expr = '${';
          while (j < src.length && depth > 0) {
            if (src[j] === '{') depth++;
            else if (src[j] === '}') depth--;
            expr += src[j];
            j++;
          }
          parts.push({ type: 'expr', value: expr });
          continue;
        }
        chunk += src[j];
        j++;
      }
      parts.push({ type: 'text', value: chunk });
      let rebuilt = '`';
      for (const part of parts) {
        if (part.type === 'text') {
          const nextText = rewrite(part.value);
          if (nextText !== part.value) changed++;
          rebuilt += nextText;
        } else {
          rebuilt += part.value;
        }
      }
      rebuilt += src[j] === '`' ? '`' : '';
      out += rebuilt;
      i = src[j] === '`' ? j + 1 : j;
      continue;
    }
    out += c;
    i++;
  }
  return { out, changed };
}

function rewriteJsxText(src) {
  let changed = 0;
  const out = src.replace(/>([^<>{}]*[Ff]orum[^<>{}]*)</g, (full, inner) => {
    if (/[=;()[\]]/.test(inner)) return full;
    const next = renameProse(inner);
    if (next === inner) return full;
    changed++;
    return `>${next}<`;
  });
  return { out, changed };
}

function walk(dir, acc) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'dist' || entry.name === 'wwwroot') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, acc);
    else acc.push(full);
  }
}

const root = process.cwd();
const files = [];
walk(path.join(root, 'client', 'snekl-web', 'src'), files);
walk(path.join(root, 'src'), files);

let fileCount = 0;
for (const file of files) {
  const ext = path.extname(file);
  const isTs = ext === '.ts' || ext === '.tsx';
  const isCs = ext === '.cs';
  if (!isTs && !isCs) continue;
  const src = fs.readFileSync(file, 'utf8');
  const rewrite = isCs
    ? (content) => (/\s/.test(content) ? renameProse(content) : content)
    : renameTsString;
  const strings = scanStrings(src, rewrite);
  const jsx = isTs ? rewriteJsxText(strings.out) : { out: strings.out, changed: 0 };
  const total = strings.changed + jsx.changed;
  if (total > 0 && jsx.out !== src) {
    fs.writeFileSync(file, jsx.out);
    fileCount++;
    console.log(`${path.relative(root, file)} (${total})`);
  }
}

const readmePath = path.join(root, 'README.md');
const readme = fs.readFileSync(readmePath, 'utf8');
const nextReadme = renameProse(readme);
if (nextReadme !== readme) {
  fs.writeFileSync(readmePath, nextReadme);
  console.log('README.md');
}
console.log(`updated files: ${fileCount}`);
