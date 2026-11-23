// Google Calendar Desktop Widget
// Copyright (C) 2026 Mahdi Kazemiesfahani
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Builds the data for the private stats page (docs/stats/). Reads the CSV snapshots
// in the current directory (the "stats" branch), adds the website numbers from
// GoatCounter, and writes dashboard.enc.json encrypted with STATS_PASSWORD
// (PBKDF2-SHA256 key, AES-256-GCM). The page decrypts it in the browser with the
// same password, so the published file is unreadable without it.
// Runs daily in .github/workflows/download-stats.yml.
//
// Env: STATS_PASSWORD (required), GOATCOUNTER_SITE + GOATCOUNTER_TOKEN (website numbers).

import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { createCipheriv, pbkdf2Sync, randomBytes } from 'node:crypto';

// The salt stays fixed so a key remembered on a device keeps working from day to day;
// it only has to be unique to this page, not secret. The page reads both from the file.
const KDF_SALT = Buffer.from('google-calendar-desktop-widget/stats/v1');
const KDF_ITERATIONS = 600_000;
const RANGES = [7, 30, 90];  // days, the page's range filter
const OUTPUT = 'dashboard.enc.json';

const password = process.env.STATS_PASSWORD;
if (!password) {
  console.log('::warning::STATS_PASSWORD secret is not set; skipping the stats page data');
  process.exit(0);
}

// our own CSVs: comma-separated, a field may be "quoted" with "" as an escaped quote
function parseLine(line) {
  const cells = [];
  let cell = '', quoted = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (quoted) {
      if (c === '"' && line[i + 1] === '"') { cell += '"'; i++; }
      else if (c === '"') quoted = false;
      else cell += c;
    } else if (c === '"') quoted = true;
    else if (c === ',') { cells.push(cell); cell = ''; }
    else cell += c;
  }
  cells.push(cell);
  return cells;
}

function readCsv(file) {
  if (!existsSync(file)) return [];
  const [header, ...lines] = readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean);
  const cols = parseLine(header);
  return lines.map(line => Object.fromEntries(parseLine(line).map((v, i) =>
    [cols[i], /^\d+$/.test(v) ? Number(v) : v])));
}

// referrers and paths keep one 14-day snapshot per day; the page shows the newest
function newestSnapshot(rows) {
  const newest = rows.reduce((max, r) => (r.date > max ? r.date : max), '');
  return rows.filter(r => r.date === newest);
}

async function goatcounter() {
  const site = process.env.GOATCOUNTER_SITE, token = process.env.GOATCOUNTER_TOKEN;
  if (!site || !token) return { error: 'The GOATCOUNTER_TOKEN secret is not set.' };

  const hour = 3600e3, day = 24 * hour;
  const iso = ms => new Date(ms).toISOString().replace('.000Z', 'Z');
  const end = Math.ceil(Date.now() / hour) * hour;  // the API wants whole hours
  const today = Math.floor(Date.now() / day) * day;
  const start = days => today - (days - 1) * day;

  async function get(endpoint, days, query = '') {
    await new Promise(r => setTimeout(r, 300));  // the API allows 4 requests a second
    const url = `${site}/api/v0/${endpoint}?start=${iso(start(days))}&end=${iso(end)}${query}`;
    const res = await fetch(url, { headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } });
    if (!res.ok) throw new Error(`${endpoint}: HTTP ${res.status} ${(await res.text()).slice(0, 200)}`);
    return res.json();
  }
  const named = list => (list || []).map(s => ({ name: s.name, count: s.count }));

  // each request on its own: one failing endpoint shouldn't blank the others
  const errors = [];
  let calls = 0;
  async function tryGet(endpoint, days, query) {
    calls++;
    try {
      return await get(endpoint, days, query);
    } catch (e) {
      errors.push(`${e.message} (last ${days} days)`);
      console.log(`::warning::GoatCounter ${e.message} (last ${days} days)`);
      return null;
    }
  }

  const total = await tryGet('stats/total', Math.max(...RANGES));
  const result = {
    daily: (total?.stats || []).map(s => ({ date: s.day, visits: s.daily })),
    ranges: {},
  };
  for (const days of RANGES) {
    const hits = await tryGet('stats/hits', days, '&limit=100');
    const locations = await tryGet('stats/locations', days, '&limit=20');
    const referrers = await tryGet('stats/toprefs', days, '&limit=20');
    const systems = await tryGet('stats/systems', days, '&limit=10');
    result.ranges[days] = {
      // link clicks and demo use are GoatCounter "events"; their path is the event name
      events: (hits?.hits || []).filter(h => h.event).map(h => ({ name: h.path, title: h.title, count: h.count })),
      locations: named(locations?.stats),
      referrers: named(referrers?.stats),
      systems: named(systems?.stats),
    };
  }
  if (errors.length === calls) return { error: errors[0] };
  if (errors.length) result.warnings = errors;
  return result;
}

function encrypt(data) {
  const key = pbkdf2Sync(password, KDF_SALT, KDF_ITERATIONS, 32, 'sha256');
  const iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', key, iv);
  // WebCrypto expects the GCM tag appended to the ciphertext
  const sealed = Buffer.concat([cipher.update(JSON.stringify(data), 'utf8'), cipher.final(), cipher.getAuthTag()]);
  return {
    v: 1,
    updated: data.generatedAt,
    kdf: { name: 'PBKDF2', hash: 'SHA-256', iterations: KDF_ITERATIONS, salt: KDF_SALT.toString('base64') },
    cipher: 'AES-GCM',
    iv: iv.toString('base64'),
    data: sealed.toString('base64'),
  };
}

const data = {
  generatedAt: new Date().toISOString(),
  github: {
    downloads: readCsv('downloads.csv'),  // cumulative count per release asset per day
    views: readCsv('views.csv'),
    clones: readCsv('clones.csv'),
    referrers: newestSnapshot(readCsv('referrers.csv')),
    paths: newestSnapshot(readCsv('paths.csv')),
  },
  goatcounter: await goatcounter(),
};
writeFileSync(OUTPUT, JSON.stringify(encrypt(data)) + '\n');
console.log(`${OUTPUT} written; GoatCounter: ${data.goatcounter.error ? 'unavailable' : 'ok'}`);
