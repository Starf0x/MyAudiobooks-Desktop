// A server for the desktop app to be tested against, end to end.
//
// The app in MyAudiobooks-Wpf talks to a server that is newer than the one in
// this checkout: it wants /api/account/signin and /api/favourites, and this
// tree has /login and no favourites at all. So it cannot be pointed at `npm
// start` here and be asked whether it works - the answers would be about the
// old routes, not about the app.
//
// So this builds the surface the app actually uses, over a throwaway
// collection of real MP3s, and writes down every request it gets. That log is
// the point: nobody can watch the window, so what the app *did* has to be
// readable afterwards. No sound, no click, no picture - only what came over
// the wire and what was written down.
//
//   node fixtures/app-probe.mjs
//
// Environment:
//   APP_PROBE_PORT    port, 8532 by default
//   APP_PROBE_LOG     request log, <root>/requests.log
//   APP_PROBE_KEEP    keep the collection directory
//   APP_PROBE_NO_FAVS answer /api/favourites with a plain express 404, the way
//                      a server without the route does
//   APP_PROBE_MP3     also answer /api/mp3/:trackId, with the 409-while-working
//                      and the mp3. Off by default, because the deployed server
//                      has no such route: it streams what lies on disk over
//                      /api/stream, which this fixture always does.
//   APP_PROBE_NO_STREAM
//                      answer /api/stream/:trackId with a plain express 404 as
//                      well, so that neither of the two audio routes exists.
//                      That is the case where the app has nothing left to try
//                      and still has to say so.
import express from 'express';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { spawnSync } from 'node:child_process';

const root = process.env.APP_PROBE_ROOT || fs.mkdtempSync(path.join(os.tmpdir(), 'app-probe-'));
const port = Number(process.env.APP_PROBE_PORT || 8532);
const logFile = process.env.APP_PROBE_LOG || path.join(root, 'requests.log');
fs.mkdirSync(root, { recursive: true });

// --- the collection ---------------------------------------------------------

const tone = (file, seconds, codec = ['-c:a', 'libmp3lame', '-q:a', '4']) => {
  const r = spawnSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y',
    '-f', 'lavfi', '-i', `sine=frequency=440:duration=${seconds}:sample_rate=44100`,
    '-ac', '2', ...codec, file], { encoding: 'utf8' });
  if (r.status !== 0) throw new Error(`ffmpeg kon ${path.basename(file)} niet maken: ${r.stderr}`);
  return file;
};

const media = path.join(root, 'media');
fs.mkdirSync(media, { recursive: true });
const files = {
  oneA: tone(path.join(media, 'deel1.mp3'), 45),
  oneB: tone(path.join(media, 'deel2.mp3'), 45),
  two: tone(path.join(media, 'boek-twee.mp3'), 45),
  // Een echt m4a, want de app heeft één decoder en die spreekt geen aac. Dit is
  // het geval waarvoor de webinterface een knop heeft: "Convert to MP3".
  m4a: tone(path.join(media, 'boek-vier.m4a'), 45, ['-c:a', 'aac', '-b:a', '64k']),
  // Een mp3 die de server niet kan meten. Drie seconden geluid in een mp3
  // zonder Xing-kop geeft `format.duration` undefined, en `scan.js` schrijft
  // dan 0 in de database. Dat is geen bug van de fixture maar van de server,
  // en het is de reden dat elk deel van een echt boek als 0:00 stond.
  noLength: tone(path.join(media, 'boek-vijf.mp3'), 90),
};

// --- the model --------------------------------------------------------------

// The heart sits on one book, so the app has something to show and something to
// toggle, and the counters have to move when it does.
const books = [
  {
    id: 1, title: 'Het eerste boek', author: 'Auteursnaam', genre: 'Proef',
    series: 'De reeks', series_no: 1, narrator: 'Verteller', year: 2021,
    description: 'De synopsis van boek een. Een boek dat uit twee delen bestaat, '
      + 'zodat het volgende deel op volgogoed te horen is en niet alleen de volgende.',
    duration: 90, coverV: 1, heart: true,
    tracks: [
      { id: 11, idx: 0, title: 'Deel een', duration: 45, file: files.oneA },
      { id: 12, idx: 1, title: 'Deel twee', duration: 45, file: files.oneB },
    ],
  },
  {
    id: 2, title: 'Het tweede boek', author: 'Auteursnaam', genre: 'Proef',
    series: 'De reeks', series_no: 2, narrator: 'Verteller', year: 2023,
    description: 'De synopsis van boek twee. Eén deel, en dat is een ander geval: '
      + 'na het einde van het laatste deel moet het boek van de plank af.',
    duration: 45, coverV: 1, heart: false,
    tracks: [{ id: 21, idx: 0, title: 'Het hele boek', duration: 45, file: files.two }],
  },
];

// One book the way the deployed server sends the ones it knows least about: a
// book with no cover yet and no known length, so `coverV` and `duration` are
// null instead of a number. JavaScript has no numbers without a value, so that
// is what a real server sends, and C# cannot read null into an int. This book
// is the one that emptied the home page on the real server, so it is here too.
books.push({
  id: 4, title: 'Het boek zonder omslag en zonder duur', author: 'Anderenaam',
  genre: 'Proef', series: null, series_no: null, narrator: null, year: null,
  description: 'Een boek waar de server nog niets van weet: geen omslag, geen duur, '
    + 'geen serie, geen verteller. Leeg is niet hetzelfde als onbekend, en de app '
    + 'mag hier niet mee stoppen.',
  duration: null, coverV: null, heart: false,
  tracks: [{ id: 41, idx: 0, title: 'Het hele boek', duration: 45, file: files.two }],
});

// One track that is not MP3 and has to be made into MP3 first, so the app's
// "bezig met omzetten" line gets a real 409 to answer. The first two asks say
// busy, the third hands over the bytes: that is the whole three-way answer.
const converting = { asked: 0, trackId: 31 };
books.push({
  id: 3, title: 'Het boek dat nog omgezet moet worden', author: 'Anderenaam',
  genre: 'Proef', series: null, series_no: 0, narrator: '', year: 2024,
  description: 'Dit boek ligt als m4a op de schijf en de server maakt er mp3 van. '
    + 'De app moet daar zichtbaar mee bezig zijn en er niets van zeggen te horen.',
  duration: 45, coverV: 1, heart: false,
  tracks: [{ id: converting.trackId, idx: 0, title: 'Hoofdstuk 1', duration: 45, file: files.two }],
});

// Een boek dat niet omgezet is en dus geen mp3 is. De webinterface speelt het
// zonder problemen, en deze app niet: die heeft één decoder. De app moet daar
// iets zeggen dat te doen is, dus wat de server stuurde, en niet "het werkt
// niet".
books.push({
  id: 5, title: 'Het boek dat nog geen mp3 is', author: 'Anderenaam',
  genre: 'Proef', series: null, series_no: 0, narrator: '', year: 2025,
  description: 'Dit boek ligt als m4a op de schijf. De webinterface speelt het '
    + 'zonder moeite; deze app kan dat niet, en moet daarom zeggen dat het '
    + 'omgezet kan worden.',
  duration: 45, coverV: 1, heart: false,
  tracks: [{ id: 51, idx: 0, title: 'Hoofdstuk 1', duration: 45, file: files.m4a }],
});

// Een boek waarvan de server de duur niet kent. `duration: 0` is geen lege
// boek, het is een onbekende: scan.js zet 0 als de mp3 geen Xing-kop heeft, en
// dat doet de helft van een verzameling. De app moet dan de klok van de lezer
// nemen, en haar niet op 0 houden — anders springt alles naar 0 en bewaart de
// app overal 0, en dan "werkt" hervatten zonder dat er iets gebeurt.
books.push({
  id: 6, title: 'Het boek zonder duur', author: 'Auteursnaam',
  genre: 'Proef', series: 'De reeks', series_no: 3, narrator: 'Verteller', year: 2022,
  description: 'Twee delen van een minuut waarvan de server de lengte niet weet. '
    + 'De app moet de tijd toch laten lopen, en de plek toch bewaren.',
  duration: 0, coverV: 1, heart: false,
  tracks: [
    { id: 61, idx: 0, title: 'Deel een', duration: 0, file: files.noLength },
    { id: 62, idx: 1, title: 'Deel twee', duration: 0, file: files.noLength },
  ],
});

const place = new Map();   // bookId -> { trackIdx, position }
const done = new Set();
const sessions = new Set();
const noFavourites = process.env.APP_PROBE_NO_FAVS === '1';
const hasMp3 = process.env.APP_PROBE_MP3 === '1';
const noStream = process.env.APP_PROBE_NO_STREAM === '1';

const log = (line) => {
  fs.appendFileSync(logFile, `${new Date().toISOString()}  ${line}\n`);
};

// --- a cover, made here so the whole cover path is real ---------------------

// A PNG written by hand, because a book with no cover is a different test and
// one that hides the decoding behind an empty box.
const png = (w, h, [r, g, b]) => {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) {
    const row = y * (w * 3 + 1);
    raw[row] = 0; // filter: none
    for (let x = 0; x < w; x++) {
      const p = row + 1 + x * 3;
      // a gold band with a darker foot, so a stretched image is not a flat
      // colour by accident
      const k = y < h * 0.2 ? 0.45 : 1;
      raw[p] = Math.round(r * k); raw[p + 1] = Math.round(g * k); raw[p + 2] = Math.round(b * k);
    }
  }
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'latin1'), data]);
    const crcTable = png.crcTable || (png.crcTable = (() => {
      const t = new Int32Array(256);
      for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        t[n] = c;
      }
      return t;
    })());
    let c = -1;
    for (const b of body) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8);
    const crc = Buffer.alloc(4); crc.writeInt32BE(c ^ -1);
    return Buffer.concat([len, body, crc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 2; // 8 bits, truecolour
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
};
const coverPng = png(200, 280, [240, 180, 41]);

// --- the server -------------------------------------------------------------

const app = express();
app.use(express.json());

const who = (req) => {
  const name = req.query.user || req.body?.user || '';
  return String(name);
};

const asBook = (b, user) => {
  const p = place.get(b.id);
  const into = p ? b.tracks.slice(0, p.trackIdx).reduce((n, t) => n + t.duration, 0) + p.position : 0;
  return {
    id: b.id, title: b.title, author: b.author, genre: b.genre,
    series: b.series, series_no: b.series_no, narrator: b.narrator, year: b.year,
    description: b.description, cover: 'cover.jpg', coverV: b.coverV,
    duration: b.duration, tagged: '', started: !!p, finished: done.has(b.id), done: done.has(b.id),
    track_idx: p ? p.trackIdx : null, position: p ? p.position : null,
    into: into || null, percent: b.duration ? Math.round((into / b.duration) * 100) : 0,
  };
};

app.post('/api/account/signin', (req, res) => {
  const { name, password } = req.body || {};
  // One account, and the password is said out loud, because this is a fixture
  // and there is nobody to keep a secret from.
  if (!name || password !== 'probe') {
    log(`signin Mislukt ${name || '(leeg)'}`);
    return res.status(401).json({ error: 'verkeerd wachtwoord' });
  }
  const session = `sess-${name}-${sessions.size + 1}`;
  sessions.add(session);
  res.setHeader('Set-Cookie', `mabc_session=${session}; Path=/; HttpOnly; SameSite=Lax`);
  log(`signin gelukt ${name}`);
  res.json({ ok: true, user: name });
});

app.post('/api/account/signout', (req, res) => {
  log('signout');
  res.setHeader('Set-Cookie', 'mabc_session=; Path=/; Max-Age=0');
  res.json({ ok: true });
});

app.get('/api/account/me', (req, res) => {
  const cookie = String(req.headers.cookie || '');
  const session = /mabc_session=([^;]+)/.exec(cookie)?.[1];
  const name = session && sessions.has(session) ? session.split('-')[1] : '';
  if (!name) {
    log('/api/account/me 401');
    return res.status(401).json({ error: 'niet aangemeld' });
  }
  log(`/api/account/me 200 ${name}`);
  res.json({ user: name, name });
});

app.get('/api/home', (req, res) => {
  const u = who(req);
  log(`/api/home 200 ${u}`);
  res.json({
    continue: books.filter((b) => place.has(b.id) && !done.has(b.id)).map((b) => asBook(b, u)),
    recent: books.map((b) => asBook(b, u)),
  });
});

app.get('/api/stats', (req, res) => {
  log('/api/stats 200');
  res.json({
    books: books.length,
    files: books.reduce((n, b) => n + b.tracks.length, 0),
    done: done.size,
    todo: books.length - done.size,
    version: 'probe',
  });
});

app.get('/api/listened', (req, res) => {
  log('/api/listened 200');
  res.json(books.filter((b) => done.has(b.id)).map((b) => asBook(b)));
});

// With or without the route. What matters is the shape of the answer: a server
// that has the route says JSON, and a server that has not answers with the
// plain express 404, which is an HTML page. The app tells those apart, so the
// 404 has to be an HTML page and not a JSON one, and it has to be a *route
// level* answer - a catch-all here would also swallow /api/genres.
app.get('/api/favourites', (req, res) => {
  if (noFavourites) {
    log('/api/favourites 404 HTML, zoals een server zonder de route');
    return res.status(404).type('html').send('<!doctype html><title>Error</title>Cannot GET /api/favourites');
  }
  log(`/api/favourites 200 ${who(req)}`);
  res.json(books.filter((b) => b.heart).map((b) => asBook(b)));
});
app.post('/api/favourites/:id', (req, res) => {
  if (noFavourites) {
    log('/api/favourites/:id 404 HTML');
    return res.status(404).type('html').send('<!doctype html><title>Error</title>Cannot POST');
  }
  const b = books.find((x) => x.id === Number(req.params.id));
  if (!b) return res.status(404).json({ error: 'onbekend boek' });
  b.heart = !!req.body?.on;
  log(`/api/favourites/${b.id} -> ${b.heart}`);
  res.json({ ok: true, on: b.heart });
});

app.get('/api/genres', (req, res) => {
  log('/api/genres 200');
  res.json([{
    name: 'Proef',
    books: books.length,
    series: [{ name: 'De reeks', books: books.filter((b) => b.series).length }],
  }]);
});

app.get('/api/authors', (req, res) => {
  const genre = String(req.query.genre || '');
  const names = [...new Set(books.filter((b) => b.genre === genre).map((b) => b.author))];
  log(`/api/authors 200 ${genre} -> ${names.length}`);
  res.json(names.map((name) => ({ name, books: books.filter((b) => b.author === name).length })));
});

app.get('/api/books', (req, res) => {
  const { genre, author, series } = req.query;
  const found = books.filter((b) => (!genre || b.genre === genre)
    && (!author || b.author === author)
    && (!series || b.series === series));
  log(`/api/books 200 ${JSON.stringify({ genre, author, series })} -> ${found.length}`);
  res.json({
    books: found.map((b) => asBook(b)),
    series: series === 'De reeks' ? [{
      name: 'De reeks', author: 'Auteursnaam', books: 2, highest: 2,
      missing: [3], unnumbered: 0,
      says: 'Deel 3 ontbreekt.',
    }] : [],
  });
});

app.get('/api/search', (req, res) => {
  const q = String(req.query.q || '').toLowerCase();
  const found = books.filter((b) => b.title.toLowerCase().includes(q) || b.author.toLowerCase().includes(q));
  log(`/api/search 200 "${q}" -> ${found.length}`);
  res.json(found.map((b) => asBook(b)));
});

app.get('/api/books/:id', (req, res) => {
  const b = books.find((x) => x.id === Number(req.params.id));
  if (!b) return res.status(404).json({ error: 'onbekend boek' });
  log(`/api/books/${b.id} 200 ${who(req)}`);
  res.json({
    id: b.id, title: b.title, author: b.author, genre: b.genre,
    series: b.series, series_no: b.series_no, narrator: b.narrator, year: b.year,
    description: b.description, cover: 'cover.jpg', coverV: b.coverV,
    duration: b.duration, finished: done.has(b.id),
    tracks: b.tracks.map((t) => ({ id: t.id, idx: t.idx, title: t.title, duration: t.duration })),
    progress: place.get(b.id) || null,
  });
});

app.post('/api/progress', (req, res) => {
  const { bookId, trackIdx, position } = req.body || {};
  const b = books.find((x) => x.id === Number(bookId));
  if (!b) return res.status(404).json({ error: 'onbekend boek' });
  const length = b.tracks[trackIdx]?.duration ?? 0;
  const atEnd = length > 0 && position >= length - 1;
  if (atEnd) done.add(b.id);
  place.set(b.id, { trackIdx, position });
  log(`/api/progress ${who(req)} boek ${bookId} deel ${trackIdx} op ${position}${atEnd ? ' KLAAR' : ''}`);
  res.json({ ok: true, done: atEnd, cleared: false });
});

app.post('/api/listened', (req, res) => {
  const { bookId, done: isDone } = req.body || {};
  if (isDone) done.add(Number(bookId)); else done.delete(Number(bookId));
  log(`/api/listened boek ${bookId} -> ${!!isDone}`);
  res.json({ ok: true, done: !!isDone });
});

app.get('/api/cover/:id', (req, res) => {
  log(`/api/cover/${req.params.id}`);
  res.type('png').send(coverPng);
});

// The route the web player uses, and the only audio route the deployed server
// has: `player.js:61` sets `audio.src = /api/stream/${t.id}`. It sends the file
// as it lies on disk, so the content type follows the extension — which is the
// whole reason a native app cannot simply copy what the browser does.
const streamKind = (file) => {
  const ext = path.extname(file).toLowerCase();
  if (ext === '.mp3') return 'audio/mpeg';
  if (ext === '.m4a' || ext === '.m4b') return 'audio/mp4';
  return 'application/octet-stream';
};

app.get('/api/stream/:trackId', (req, res) => {
  const id = Number(req.params.trackId);
  if (noStream) {
    // Ook deze route weg. Dan heeft de app geen van beide wegen, en moet hij
    // dat zeggen in plaats van stil te blijven of te doen alsof er niets is.
    log(`/api/stream/${id} 404 HTML, net als /api/mp3`);
    return res.status(404).send('<!DOCTYPE html>\n<html lang="en">\n<head><meta charset="utf-8"><title>Error</title></head>\n<body>\n<pre>Cannot GET /api/stream/' + id + '</pre>\n</body>\n</html>\n');
  }
  const track = books.flatMap((b) => b.tracks).find((t) => t.id === id);
  if (!track) {
    log(`/api/stream/${id} 404 weg`);
    return res.status(404).end();
  }
  const body = fs.readFileSync(track.file);
  log(`/api/stream/${id} 200 ${streamKind(track.file)}, ${body.length} bytes`);
  res.setHeader('Content-Type', streamKind(track.file));
  res.setHeader('Accept-Ranges', 'bytes');
  const range = String(req.headers.range || '');
  const m = /^bytes=(\d*)-(\d*)$/.exec(range);
  if (!m) return res.status(200).send(body);
  const from = m[1] === '' ? Math.max(0, body.length - Number(m[2])) : Number(m[1]);
  const to = m[2] === '' || m[1] === '' ? body.length - 1 : Math.min(body.length - 1, Number(m[2]));
  if (from > to || from >= body.length) {
    res.setHeader('Content-Range', `bytes */${body.length}`);
    return res.status(416).end();
  }
  res.status(206);
  res.setHeader('Content-Range', `bytes ${from}-${to}/${body.length}`);
  res.setHeader('Content-Length', String(to - from + 1));
  res.end(body.subarray(from, to + 1));
});

// `/api/mp3` exists only on the server copy that has the rendition route in it.
// The deployed one does not have it, so by default this fixture does not either
// and every test runs against a server that looks like the real one. Only the
// tests of the mp3 route itself ask for it by name.
app.get('/api/mp3/:trackId', (req, res) => {
  const id = Number(req.params.trackId);
  if (!hasMp3) {
    // De express-404 van een server zonder deze route: HTML, geen JSON. De app
    // moet hier iets zeggen dat waar is, en niet "de server is te oud"
    // roepen zonder het te weten.
    log(`/api/mp3/${id} 404 HTML, zoals een server zonder de route`);
    return res.status(404).send('<!DOCTYPE html>\n<html lang="en">\n<head><meta charset="utf-8"><title>Error</title></head>\n<body>\n<pre>Cannot GET /api/mp3/' + id + '</pre>\n</body>\n</html>\n');
  }
  const track = books.flatMap((b) => b.tracks).find((t) => t.id === id);
  if (!track) {
    log(`/api/mp3/${id} 404 weg`);
    return res.status(404).json({ error: 'dit spoor bestaat niet meer' });
  }
  if (id === converting.trackId) {
    converting.asked++;
    if (converting.asked <= 2) {
      log(`/api/mp3/${id} 409 bezig (${converting.asked})`);
      return res.status(409).json({ state: 'working', done: 12 + converting.asked * 8, total: 45 });
    }
    log(`/api/mp3/${id} 200 klaar na ${converting.asked}x vragen`);
  } else {
    log(`/api/mp3/${id} 200`);
  }
  const body = fs.readFileSync(track.file);
  const range = String(req.headers.range || '');
  const m = /^bytes=(\d*)-(\d*)$/.exec(range);
  if (!m) {
    res.setHeader('Content-Type', 'audio/mpeg');
    res.setHeader('Accept-Ranges', 'bytes');
    return res.status(200).send(body);
  }
  const from = m[1] === '' ? Math.max(0, body.length - Number(m[2])) : Number(m[1]);
  const to = m[2] === '' || m[1] === '' ? body.length - 1 : Math.min(body.length - 1, Number(m[2]));
  if (from > to || from >= body.length) {
    res.setHeader('Content-Range', `bytes */${body.length}`);
    return res.status(416).end();
  }
  const piece = body.subarray(from, to + 1);
  res.status(206);
  res.setHeader('Content-Type', 'audio/mpeg');
  res.setHeader('Accept-Ranges', 'bytes');
  res.setHeader('Content-Range', `bytes ${from}-${to}/${body.length}`);
  res.setHeader('Content-Length', String(piece.length));
  res.end(piece);
});

// Anything the app asks that is not here is a hole in this fixture, and it says
// so in the log rather than answering a plausible lie.
app.use((req, res) => {
  log(`ONBEKEND ${req.method} ${req.originalUrl}`);
  res.status(501).json({ error: `de fixture kent ${req.method} ${req.path} niet` });
});

app.listen(port, '127.0.0.1', () => {
  console.log(`app-probe op http://127.0.0.1:${port}`);
  console.log(`  aanmelden met iedere naam en het wachtwoord "probe"`);
  console.log(`  log:  ${logFile}`);
  console.log(`  mp3:  ${books.flatMap((b) => b.tracks).map((t) => `${t.id}->${path.basename(t.file)}`).join('  ')}`);
  console.log(`  boeken: ${books.map((b) => `${b.id} ${b.title} (${b.tracks.length} dele${b.tracks.length === 1 ? 'n' : 'n'})`).join('  ')}`);
  console.log(`  favorieten: ${noFavourites ? 'uit (404, zoals een server zonder de route)' : 'aan'}`);
  console.log(`  /api/mp3:  ${hasMp3 ? 'aan, met 409 tijdens het omzetten' : 'uit, zoals de draaiende server'}`);
  console.log(`  /api/stream: ${noStream ? 'uit (404, net als /api/mp3)' : 'aan, met het formaat van het bestand zoals het op schijf ligt'}`);
  if (process.env.APP_PROBE_KEEP !== '1') {
    // The collection is 45 seconds of tone three times over. Leaving that behind
    // in the temp folder is litter, and nothing here needs it twice.
    process.on('exit', () => { try { fs.rmSync(root, { recursive: true, force: true }); } catch { /* best effort */ } });
  }
});
