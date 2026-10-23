'use strict';
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const DEMO = new URLSearchParams(location.search).has('demo');
let host = null;
const send = (m) => host && host.postMessage(m);

const S = {
  settings: {}, moodle: 'checking', ai: null, courses: null, events: null, userName: '',
  course: null, sections: [], local: null, notability: null, open: new Set(), tab: 'lessons',
  chatBusy: false, chatHidden: false, view: 'home', site: 'moodle', jobs: new Map(), ready: { settings: false, ai: false }, theme: 'system'
};

// ---------- Theme ----------
const darkQuery = matchMedia('(prefers-color-scheme: dark)');
function applyTheme(t) {
  if (t) S.theme = t;
  const dark = S.theme === 'dark' || (S.theme === 'system' && darkQuery.matches);
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  const icon = $('themeIcon'); if (icon) icon.innerHTML = dark ? IC.sun : IC.moon;
  const text = $('themeText'); if (text) text.textContent = dark ? 'מצב בהיר' : 'מצב כהה';
  const sel = $('themeSelect'); if (sel) sel.value = S.theme;
}
darkQuery.addEventListener('change', () => applyTheme());
applyTheme('system');

// ---------- Small helpers ----------
function icons(root = document) {
  root.querySelectorAll('[data-ic]').forEach(el => { if (!el.dataset.done) { el.insertAdjacentHTML('afterbegin', IC[el.dataset.ic] || ''); el.dataset.done = 1; } });
}
function h(html) { const t = document.createElement('template'); t.innerHTML = html.trim(); icons(t.content); return t.content.firstElementChild; }
function firstName(n) { return (n || '').trim().split(/\s+/)[0] || ''; }
function greeting() {
  const hr = new Date().getHours();
  return hr < 5 ? 'לילה טוב' : hr < 12 ? 'בוקר טוב' : hr < 17 ? 'צהריים טובים' : hr < 22 ? 'ערב טוב' : 'לילה טוב';
}
const dayFmt = new Intl.DateTimeFormat('he-IL', { weekday: 'long', day: 'numeric', month: 'long' });
const monFmt = new Intl.DateTimeFormat('he-IL', { month: 'short' });
const timeFmt = new Intl.DateTimeFormat('he-IL', { hour: '2-digit', minute: '2-digit' });
function relDay(ts) {
  const d = new Date(ts * 1000), now = new Date();
  const days = Math.round((new Date(d.getFullYear(), d.getMonth(), d.getDate()) - new Date(now.getFullYear(), now.getMonth(), now.getDate())) / 86400000);
  if (days < 0) return 'עבר המועד';
  if (days === 0) return 'היום, ' + timeFmt.format(d);
  if (days === 1) return 'מחר, ' + timeFmt.format(d);
  if (days < 7) return `בעוד ${days} ימים`;
  return dayFmt.format(d);
}

function toast(text, opts = {}) {
  const t = h(`<div class="toast${opts.error ? ' error' : ''}"><span class="ti" data-ic="${opts.error ? 'alert' : 'check'}"></span><span class="grow" dir="auto"></span></div>`);
  t.querySelector('.grow').textContent = text;
  if (opts.action) { const b = h(`<button class="small soft"></button>`); b.textContent = opts.action; b.onclick = () => { opts.onAction(); t.remove(); }; t.append(b); }
  $('toasts').prepend(t);
  setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 300); }, opts.ms || 4800);
}

function md(text) {
  const blocks = [];
  let t = esc(text).replace(/```[\w-]*\n?([\s\S]*?)```/g, (_, code) => { blocks.push(code); return `\u0000${blocks.length - 1}\u0000`; });
  t = t.replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>').replace(/^#{1,4} (.*)$/gm, '<h4>$1</h4>');
  let html = '', list = null;
  for (const line of t.split('\n')) {
    const ul = line.match(/^\s*[-*•] (.*)/), ol = line.match(/^\s*\d+[.)] (.*)/);
    if (ul || ol) {
      const type = ul ? 'ul' : 'ol';
      if (list !== type) { if (list) html += `</${list}>`; html += `<${type}>`; list = type; }
      html += `<li>${(ul || ol)[1]}</li>`;
    } else {
      if (list) { html += `</${list}>`; list = null; }
      if (line.trim()) html += line.startsWith('<h4>') ? line : `<p>${line}</p>`;
    }
  }
  if (list) html += `</${list}>`;
  return html.replace(/\u0000(\d+)\u0000/g, (_, i) => `<pre>${blocks[i]}</pre>`);
}

// Gemini sign-in: Google's page opened in the browser; after signing in it shows a code, which goes here.
function showGeminiCode() {
  if ($('gemCode')) return;
  const card = h(`<div class="overlay" id="gemCode"><div class="wizard" style="width:min(480px,100%)">
    <div class="content"><h1 style="font-size:21px">התחברות ל-Gemini</h1>
    <p class="lead">נפתח דף ההתחברות של Google בדפדפן. התחבר לחשבון שלך, אשר, ו-Google יציג קוד. העתק אותו והדבק כאן.</p>
    <input type="text" class="ltr" placeholder="הדבק את הקוד מ-Google" autocomplete="off">
    <div class="foot"><div class="grow"></div><button class="ghost" data-x>ביטול</button><button class="primary big" data-go>התחבר</button></div></div></div></div>`);
  const go = () => { const v = card.querySelector('input').value.trim(); if (!v) return; send({ type: 'geminiCode', code: v }); card.remove(); toast('מתחבר ל-Gemini...'); };
  card.querySelector('[data-go]').onclick = go;
  card.querySelector('input').onkeydown = (e) => { if (e.key === 'Enter') go(); };
  card.querySelector('[data-x]').onclick = () => { send({ type: 'cancelGeminiLogin' }); card.remove(); };
  document.body.append(card);
  card.querySelector('input').focus();
}

// Usage limit: a small friendly card instead of an error, with a one-click switch to the other AI.
function showLimit(m) {
  if ($('limitCard')) return;
  const other = m.provider === 'claude' ? 'gemini' : 'claude';
  const otherName = other === 'gemini' ? 'Gemini' : 'Claude';
  const card = h(`<div class="overlay" id="limitCard"><div class="wizard" style="width:min(460px,100%)">
    <div class="art-top" style="height:150px">${ART.ai().replace('<svg', '<svg style="width:230px;height:140px"')}</div>
    <div class="content"><h1 style="font-size:21px">${m.provider === 'claude' ? 'Claude' : 'Gemini'} צריך הפסקה קטנה ☕</h1>
    <p class="lead">הגעת למגבלת השימוש לעכשיו${m.reset ? ' (מתאפס ב-' + esc(m.reset) + ')' : ''}. אפשר להמשיך מיד עם ${otherName}${m.otherReady ? '' : ', אחרי שמחברים אותו פעם אחת'}.</p>
    <div class="foot"><div class="grow"></div><button class="ghost" data-x>אחר כך</button><button class="primary big" data-go>המשך עם ${otherName}</button></div></div></div></div>`);
  card.querySelector('[data-x]').onclick = () => card.remove();
  card.querySelector('[data-go]').onclick = () => { card.remove(); send({ type: 'setProvider', provider: other }); if (!m.otherReady) openWizard(1); else toast(`עברנו ל-${otherName}. אפשר לנסות שוב.`); };
  document.body.append(card);
}

function confetti() {
  const box = h('<div class="confetti"></div>');
  const colors = ['#6366f1', '#8b5cf6', '#ec4899', '#fbbf24', '#34d399', '#38bdf8'];
  for (let i = 0; i < 90; i++) {
    const p = document.createElement('i');
    p.style.left = Math.random() * 100 + '%';
    p.style.background = colors[i % colors.length];
    p.style.animationDelay = Math.random() * .6 + 's';
    p.style.animationDuration = 1.4 + Math.random() * 1.2 + 's';
    box.append(p);
  }
  document.body.append(box);
  setTimeout(() => box.remove(), 3200);
}

// ---------- Navigation ----------
function show(view) {
  if (S.view === 'sites' && view !== 'sites') send({ type: 'hideSite' });
  S.view = view;
  document.querySelectorAll('.view').forEach(v => v.classList.toggle('active', v.id === 'view-' + view));
  const nav = view === 'course' ? 'courses' : view;
  document.querySelectorAll('.nav-item').forEach(n => n.classList.toggle('active', n.dataset.view === nav || (view === 'sites' && n.dataset.siteLink === S.site)));
  $('main').scrollTop = 0;
  if (view === 'sites') requestAnimationFrame(() => placeSite(true));
  if (view === 'home') renderHome();
  if (view === 'courses' && !S.courses) loadCourses();
}
document.querySelectorAll('.nav-item[data-view]').forEach(n => n.onclick = () => show(n.dataset.view));
// Sites open full-size inside the app, without a toolbar. Anything downloaded there goes straight into the course
// that's open (or the "הורדות" folder), to the folder that fits the site.
const siteFolder = { moodle: 'כללי', portal: 'מידע על הקורס', cs24: 'CS24', notability: 'מחברת', notebooklm: 'CS24' };
document.querySelectorAll('.nav-item[data-site-link]').forEach(n => n.onclick = () => {
  const site = n.dataset.siteLink;
  $('dlCourse').value = S.course ? S.course.name : '';
  $('dlFolder').value = siteFolder[site] || 'כללי';
  syncDownloadTarget();
  openSite(site);
});
// "My notebook": the Notability app on this PC if it's installed, otherwise Notability on the web inside the app.
document.querySelector('.nav-item[data-notes]').onclick = () => send({ type: 'openNotability', course: S.course ? S.course.name : '' });
$('brand').onclick = () => show('home');

// ---------- Status ----------
function aiName() { return (S.ai && S.ai.provider === 'gemini') ? 'Gemini' : 'Claude'; }
function setupText(s) {
  return { 'installing-claude': 'מתקין את Claude...', 'login-claude': 'ממתין להתחברות בדפדפן...', 'installing-node': 'מכין את Gemini...', 'installing-gemini': 'מתקין את Gemini...', 'login-gemini': 'ממתין להתחברות ל-Google בדפדפן...' }[s] || '';
}
function renderStatus() {
  const m = S.moodle;
  const mm = { ready: ['ok', 'מחובר'], connecting: ['wait', 'מתחבר...'], checking: ['wait', 'בודק...'], login: ['bad', 'לחץ כדי להתחבר'], error: ['bad', 'בעיה - לחץ לנסות שוב'] }[m] || ['bad', ''];
  $('moodleDot').className = 'dot ' + mm[0]; $('moodleSub').textContent = mm[1];
  $('moodleChip').className = 'chip ' + (mm[0] === 'ok' ? 'ok' : mm[0] === 'wait' ? 'warn' : 'bad'); $('moodleChip').textContent = mm[1];
  $('moodleConnect').textContent = m === 'ready' ? 'התחבר מחדש' : 'התחבר';

  const a = S.ai;
  $('aiText').textContent = aiName();
  let dot = 'wait', sub = 'בודק...';
  if (a) {
    if (a.setup) { dot = 'wait'; sub = setupText(a.setup); }
    else if (a.ready) { dot = 'ok'; sub = 'מחובר'; }
    else { dot = 'bad'; sub = 'לחץ כדי לחבר'; }
  }
  $('aiDot').className = 'dot ' + dot; $('aiSub').textContent = sub;
  // The sidebar only speaks up when something needs attention.
  $('moodlePill').hidden = mm[0] !== 'bad';
  $('aiPill').hidden = !(a && !a.ready && !a.setup);
  $('moodlePill').classList.add('alert'); $('aiPill').classList.add('alert');
  $('aiChip').className = 'chip ' + (dot === 'ok' ? 'ok' : dot === 'wait' ? 'warn' : 'bad'); $('aiChip').textContent = aiName() + ' · ' + sub;
  renderProviders();
  renderBanners();
  renderWizardMoodle();
}
$('moodlePill').onclick = () => { if (S.moodle === 'ready') toast('Moodle מחובר ✓'); else connectMoodle(); };
$('aiPill').onclick = () => { if (S.ai && S.ai.ready) toast(aiName() + ' מחובר ✓'); else openWizard(1); };
$('moodleConnect').onclick = connectMoodle;
function connectMoodle() { S.moodle = 'connecting'; renderStatus(); send({ type: 'connectMoodle' }); }

function renderBanners() {
  const b = $('banners'); b.innerHTML = '';
  if (S.prep) b.append(bannerEl(`מכין את הקורסים שלך ברקע · ${S.prep.done + 1} מתוך ${S.prep.total}: ${esc(S.prep.course || '')}`, 'לקורסים', () => show('courses')));
  if (S.updateReady) b.append(bannerEl(`גרסה ${esc(S.updateReady)} מוכנה`, 'הפעל מחדש עכשיו', () => send({ type: 'restartForUpdate' })));
  else if (S.update && S.update.progress) b.append(bannerEl(esc(S.update.progress), S.update.error ? 'לדף ההורדה' : '...', () => S.update.error && send({ type: 'openLink', url: S.update.url }), S.update.error ? 'warn' : ''));
  else if (S.update) b.append(bannerEl(`גרסה חדשה זמינה: ${esc(S.update.version)}`, S.update.canAuto ? 'עדכן עכשיו' : 'להורדה', () => S.update.canAuto ? send({ type: 'installUpdate' }) : send({ type: 'openLink', url: S.update.url })));
  if (S.ai && !S.ai.ready && !S.ai.setup) b.append(bannerEl('חבר את המנוע החכם כדי לסכם שיעורים ולשאול שאלות', 'חבר עכשיו', () => openWizard(1), 'warn'));
  if (S.moodle === 'login') b.append(bannerEl('צריך להתחבר ל-Moodle כדי לראות את הקורסים', 'התחבר', connectMoodle, 'warn'));
}
function bannerEl(text, action, fn, cls = '') {
  const el = h(`<div class="banner ${cls}"><span data-ic="${cls ? 'alert' : 'sparkles'}"></span><span class="grow">${text}</span><button class="small">${esc(action)}</button></div>`);
  el.querySelector('button').onclick = fn;
  return el;
}

// ---------- Providers (settings + setup) ----------
function providerCard(kind) {
  const a = S.ai || { claude: {}, gemini: {} };
  const sel = (a.provider || S.settings.aiProvider || 'claude') === kind;
  const c = kind === 'claude' ? a.claude || {} : a.gemini || {};
  const busy = a.setup && ((kind === 'claude' && a.setup.includes('claude')) || (kind === 'gemini' && /gemini|node/.test(a.setup)));
  let state, act = '';
  if (kind === 'claude') {
    if (busy) state = `<span class="spinner"></span>${setupText(a.setup)}`;
    else if (c.loggedIn) state = `<span class="chip ok"><span data-ic="check"></span>מחובר</span><span class="muted ltr">${esc(c.email || '')}</span>`;
    else if (c.installed) { state = '<span class="chip warn">מותקן, לא מחובר</span>'; act = '<button class="primary small" data-act="setupClaude">התחבר לחשבון Claude</button>'; }
    else { state = '<span class="chip">לא מותקן</span>'; act = '<button class="primary small" data-act="setupClaude">התקן והתחבר</button>'; }
  } else {
    if (busy) state = `<span class="spinner"></span>${setupText(a.setup)}`;
    else if (c.installed && (c.hasKey || c.google)) state = `<span class="chip ok"><span data-ic="check"></span>מחובר</span><span class="muted">${c.google && !c.hasKey ? 'עם חשבון Google' : 'עם מפתח'}</span>`;
    else if (c.installed) state = '<span class="chip warn">מותקן, לא מחובר</span>';
    else { state = '<span class="chip">לא מותקן</span>'; act = '<button class="primary small" data-act="setupGemini">התקן את Gemini</button>'; }
    if (c.installed && !busy) act = `<button class="primary small" data-act="loginGemini">${c.google ? 'התחבר מחדש עם Google' : 'התחבר עם חשבון Google'}</button>
      <span class="muted" style="font-size:12px;width:100%">חינמי, או לפי המנוי שלך (Google AI Pro / Ultra). או במפתח API:</span>
      <div class="row" style="width:100%"><input type="password" class="ltr" placeholder="${c.hasKey ? 'המפתח שמור · אפשר להחליף' : 'מפתח API (לא חובה)'}" data-key><button class="small" data-act="saveKey">שמור</button></div>
      <button class="link" data-act="getKey">קבל מפתח מ-Google AI Studio</button>`;
  }
  const el = h(`<div class="provider${sel ? ' selected' : ''}" data-kind="${kind}">
    ${kind === 'claude' ? '<span class="chip accent tag">מומלץ</span>' : '<span class="chip ok tag">חינמי או מנוי</span>'}
    <div class="ph"><span class="pl ${kind}" data-ic="${kind}"></span><div><h3>${kind === 'claude' ? 'Claude' : 'Gemini'}</h3></div></div>
    <p>${kind === 'claude' ? 'הכי טוב בסיכומים ארוכים ובבניית דפי לימוד. דורש חשבון Claude (Pro ומעלה מומלץ).' : 'מתחברים עם חשבון Google: חינמי, או עם המנוי שלך (למשל Google AI Pro לסטודנטים). טוב מאוד לצ\'אט ולשאלות, וגם גיבוי כשנגמרת המכסה של Claude.'}</p>
    <div class="state">${state}</div><div class="act">${act}</div></div>`);
  el.onclick = (e) => {
    const t = e.target.closest('[data-act]');
    if (t) {
      e.stopPropagation();
      const actn = t.dataset.act;
      if (actn === 'setupClaude') { send({ type: 'setProvider', provider: 'claude' }); send({ type: 'setupClaude' }); toast('מתחבר ל-Claude. אם נפתח דפדפן, אשר שם את ההתחברות.'); }
      if (actn === 'setupGemini') { send({ type: 'setProvider', provider: 'gemini' }); send({ type: 'setupGemini' }); }
      if (actn === 'getKey') send({ type: 'openGeminiKeyPage' });
      if (actn === 'loginGemini') { send({ type: 'setProvider', provider: 'gemini' }); send({ type: 'loginGemini' }); toast('נפתח דפדפן להתחברות עם Google. אשר שם והאפליקציה תמשיך לבד.'); }
      if (actn === 'saveKey') { const v = el.querySelector('[data-key]').value.trim(); if (v.length < 20) return toast('המפתח נראה קצר מדי', { error: true }); send({ type: 'setGeminiKey', key: v }); send({ type: 'setProvider', provider: 'gemini' }); toast('המפתח נשמר בצורה מוצפנת'); }
      return;
    }
    if (e.target.closest('input')) return;
    if (!sel) send({ type: 'setProvider', provider: kind });
  };
  return el;
}
function renderProviders() {
  for (const id of ['providers', 'wizProviders']) {
    const box = $(id);
    const focused = box.contains(document.activeElement) && document.activeElement.tagName === 'INPUT';
    if (focused) continue;
    box.innerHTML = '';
    box.append(providerCard('claude'), providerCard('gemini'));
  }
}

// ---------- Timetable (from the portal export) ----------
const DAYS = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת'];
function renderTimetable() {
  const box = $('timetable'); box.innerHTML = '';
  const tt = S.timetable;
  if (!tt || !((tt.classes || []).length || (tt.exams || []).length)) {
    const e = h(`<div class="empty" style="border:none;padding:10px">${ART.calendar().replace('<svg', '<svg class="art"')}<b>הוסף את המערכת ואת לוח המבחנים</b>
      בפורטל מידע אישי פותחים את מערכת השעות ואת לוח הבחינות, ושומרים אותם כ-PDF או כצילום מסך. אחר כך בוחרים את הקבצים כאן.
      <div class="row"><button class="soft" data-p>פתח את הפורטל</button><button class="primary" data-i>בחר קבצים</button></div></div>`);
    e.querySelector('[data-p]').onclick = () => { $('dlCourse').value = ''; syncDownloadTarget(); openSite('portal'); };
    e.querySelector('[data-i]').onclick = () => send({ type: 'importTimetable' });
    box.append(e);
    return;
  }
  const today = DAYS[new Date().getDay()];
  const todays = (tt.classes || []).filter(c => c.day === today).sort((a, b) => (a.start || '').localeCompare(b.start || ''));
  const exams = (tt.exams || []).filter(x => x.date && new Date(x.date) >= new Date(new Date().toDateString())).sort((a, b) => a.date.localeCompare(b.date));
  const grid = h('<div class="info-grid"></div>');
  const day = h(`<div><h3 style="margin-bottom:8px">היום · יום ${today}</h3></div>`);
  if (!todays.length) day.append(h('<div class="muted">אין שיעורים היום.</div>'));
  todays.forEach(c => { const r = h(`<div class="deadline"><div class="cal"><b style="font-size:13px;margin-top:10px">${esc(c.start)}</b><small>${esc(c.end)}</small></div><div class="info"><div class="name" dir="auto"></div><div class="course" dir="auto"></div></div></div>`); r.querySelector('.name').textContent = c.course; r.querySelector('.course').textContent = [c.kind, c.room, c.lecturer].filter(Boolean).join(' · '); day.append(r); });
  const ex = h('<div><h3 style="margin-bottom:8px">מבחנים קרובים</h3></div>');
  if (!exams.length) ex.append(h('<div class="muted">אין מבחנים קרובים במערכת.</div>'));
  exams.slice(0, 6).forEach(x => { const d = new Date(x.date); const r = h(`<div class="deadline${(d - new Date()) < 14 * 86400000 ? ' soon' : ''}"><div class="cal"><b>${d.getDate()}</b><small>${esc(monFmt.format(d))}</small></div><div class="info"><div class="name" dir="auto"></div><div class="course"></div></div><span class="chip">מועד ${esc(x.moed || '')}</span></div>`); r.querySelector('.name').textContent = x.course; r.querySelector('.course').textContent = [x.time, x.room].filter(Boolean).join(' · '); ex.append(r); });
  grid.append(day, ex);
  box.append(grid);
}
$('ttImport').onclick = () => send({ type: 'importTimetable' });

// ---------- Home ----------
// For homework deadlines: whether the syllabus says it counts (known after "learn the course" read the syllabus).
function hwNote(e) {
  if (e.mod !== 'assign') return '';
  const c = (S.courses || []).find(x => x.id === e.courseId);
  const info = c && c.local && c.local.info;
  if (!info || info.homeworkRequired == null) return '';
  return info.homeworkRequired ? ' · <span class="chip warn">חובה להגשה</span>' : ' · <span class="chip">לא חובה, אבל מומלץ ללמידה</span>';
}

// "תשפז_א" / "תשפ"ו א'" in a course name or code -> "תשפ״ז · סמסטר א"
function semesterOf(c) {
  const m = ((c.short || '') + ' ' + (c.name || '')).match(/(תש[א-ת]["״']?[א-ת])[_\s-]*([אבג])['׳]?(?![א-ת])/);
  if (!m) return c.current ? 'הסמסטר הנוכחי' : 'סמסטרים קודמים';
  const y = m[1].replace(/["״']/g, '');
  return `${y.slice(0, -1)}״${y.slice(-1)} · סמסטר ${m[2]}`;
}
function renderHome() {
  const name = firstName(S.userName || S.settings.userName);
  $('today').textContent = dayFmt.format(new Date());
  $('greeting').textContent = greeting() + (name ? `, ${name}` : '') + ' 👋';
  const ev = (S.events || []).filter(e => !e.overdue || Date.now() / 1000 - e.when < 3 * 86400);
  const week = ev.filter(e => e.when * 1000 - Date.now() < 7 * 86400000);
  $('heroLine').textContent = S.events == null ? (S.moodle === 'login' ? 'התחבר ל-Moodle כדי לראות מה מחכה לך.' : 'טוען את מה שמחכה לך השבוע...')
    : week.length ? `יש לך ${week.length} ${week.length === 1 ? 'משימה' : 'משימות'} בשבוע הקרוב. אפשר להתחיל ממה שהכי דחוף.`
    : 'אין מטלות בשבוע הקרוב. זמן מצוין לחזור על החומר.';
  $('deadlineCount').textContent = S.events ? ev.length + ' קרובות' : '';

  const box = $('deadlines'); box.innerHTML = '';
  if (S.events == null) {
    for (let i = 0; i < 4; i++) box.append(h('<div class="deadline"><div class="cal skeleton"></div><div class="info"><div class="skeleton" style="height:12px;width:60%"></div><div class="skeleton" style="height:10px;width:40%;margin-top:8px"></div></div></div>'));
  } else if (!ev.length) {
    box.append(h(`<div class="empty" style="border:none;padding:16px">${ART.calendar().replace('<svg', '<svg class="art"')}<b>הכל בשליטה</b>אין מטלות פתוחות בשבועות הקרובים.</div>`));
  } else {
    ev.slice(0, 7).forEach((e, i) => {
      const d = new Date(e.when * 1000);
      const soon = e.when * 1000 - Date.now() < 2 * 86400000;
      const el = h(`<div class="deadline${e.overdue ? ' over' : soon ? ' soon' : ''}" style="--i:${i}"><div class="cal"><b>${d.getDate()}</b><small>${esc(monFmt.format(d))}</small></div>
        <div class="info"><div class="name" dir="auto">${esc(e.name)}</div><div class="course" dir="auto">${esc(e.course)}${hwNote(e)}</div></div><span class="chip${e.overdue ? ' bad' : soon ? ' warn' : ''}">${esc(relDay(e.when))}</span></div>`);
      el.onclick = () => openSite('moodle', e.url);
      box.append(el);
    });
  }

  const hc = $('homeCourses'); hc.innerHTML = '';
  if (!S.courses) for (let i = 0; i < 4; i++) hc.append(h('<div class="skeleton" style="height:170px;border-radius:18px"></div>'));
  else {
    const cur = S.courses.filter(c => c.current);
    if (!cur.length) hc.append(h('<div class="empty" style="grid-column:1/-1">אין קורסים פעילים כרגע.</div>'));
    cur.forEach((c, i) => hc.append(courseCard(c, i)));
  }
}
$('heroArt').innerHTML = ART.hero();
$('heroCourses').innerHTML = IC.book + 'לקורסים שלי';
$('heroCourses').onclick = () => show('courses');
$('heroPaste').innerHTML = IC.exam + 'מבחנים ב-CS24';
$('heroPaste').onclick = () => openSite('cs24');
$('allCourses').onclick = () => show('courses');
(function quick() {
  const items = [
    ['c1', 'note', 'Notability', 'המחברות שלך מההרצאות', () => openSite('notability')],
    ['c2', 'exam', 'מבחנים ב-CS24', 'מבחנים וסיכומים משנים קודמות', () => openSite('cs24')],
    ['c3', 'user', 'פורטל מידע אישי', 'ציונים, מערכת שעות ובחינות', () => openSite('portal')],
    ['c4', 'folder', 'תיקיית הקורסים', 'כל החומר שהורדת', () => send({ type: 'openFolder', which: 'study' })]
  ];
  for (const [c, ic, t, s, fn] of items) {
    const b = h(`<button><span class="qi ${c}" data-ic="${ic}"></span><span>${t}<small style="display:block">${s}</small></span></button>`);
    b.onclick = fn; $('quick').append(b);
  }
})();

// ---------- Courses ----------
function courseCard(c, i) {
  const L = c.local || {};
  const pages = L.studyPages || 0;
  const pct = Math.min(100, Math.round(pages * 100 / 13));
  const el = h(`<button class="course-card" style="--i:${i}">
    <div class="band">${ART.band(c.id)}<span class="letter">${esc((c.name || '?').trim().charAt(0))}</span>${pages ? `<div class="ring" style="--p:${pct}" data-label="${pages}"></div>` : ''}</div>
    <div class="body"><div class="name" dir="auto">${esc(c.name)}</div><div class="muted" style="font-size:12px" dir="auto">${esc(c.category || '')}</div>
    <div class="meta">${L.exists ? '<span class="chip ok">במחשב</span>' : '<span class="chip">עוד לא הורד</span>'}${pages ? `<span class="chip accent">${pages} דפי לימוד</span>` : ''}${L.exams ? `<span class="chip warn">${L.exams} מבחנים</span>` : ''}</div></div></button>`);
  el.onclick = () => openCourse(c);
  return el;
}
function loadCourses() {
  $('coursesState').innerHTML = '';
  const g = $('courses'); g.innerHTML = '';
  for (let i = 0; i < 8; i++) g.append(h('<div class="skeleton" style="height:180px;border-radius:18px"></div>'));
  send({ type: 'loadCourses' });
}
function renderCourses() {
  const g = $('courses'); g.innerHTML = '';
  const list = (S.courses || []).filter(c => $('showAll').checked || c.current);
  if (!list.length) {
    $('coursesState').innerHTML = '';
    $('coursesState').append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>לא נמצאו קורסים פעילים</b>אפשר להציג גם קורסים קודמים.</div>`));
    return;
  }
  $('coursesState').innerHTML = '';
  // One group per semester, current semester first.
  const groups = new Map();
  list.forEach(c => { const k = semesterOf(c); if (!groups.has(k)) groups.set(k, []); groups.get(k).push(c); });
  const keys = [...groups.keys()].sort((a, b) => (groups.get(b).some(c => c.current) - groups.get(a).some(c => c.current)) || b.localeCompare(a, 'he'));
  let i = 0;
  keys.forEach(k => {
    g.append(h(`<h2 class="semester" style="grid-column:1/-1;margin:${i ? 18 : 0}px 2px 2px">${esc(k)}</h2>`));
    groups.get(k).forEach(c => g.append(courseCard(c, i++)));
  });
}
$('refreshCourses').onclick = loadCourses;
$('showAll').onchange = renderCourses;

// ---------- One course ----------
const modIcon = { resource: 'file', file: 'file', assign: 'exam', zoom: 'play', url: 'link', folder: 'folder', page: 'book', quiz: 'exam', forum: 'chat', book: 'book' };
const modLabel = { resource: 'קובץ', file: 'קובץ', assign: 'מטלה', zoom: 'Zoom', url: 'קישור', folder: 'תיקייה', page: 'דף', quiz: 'בוחן', forum: 'פורום', book: 'ספר', label: 'טקסט', lti: 'כלי חיצוני', choice: 'סקר', feedback: 'משוב', h5pactivity: 'תרגול אינטראקטיבי' };

function openCourse(c) {
  S.course = c; S.sections = []; S.open = new Set(); S.local = c.local || null; S.notability = null;
  $('courseName').textContent = c.name;
  $('courseBand').innerHTML = ART.band(c.id);
  $('lecturer').value = ''; $('lecturer').placeholder = 'מי המרצה?';
  $('courseCount').textContent = 'טוען...'; $('courseReady').hidden = true;
  $('courseStatus').hidden = true;
  setTab('lessons');
  $('lectures').innerHTML = '';
  for (let i = 0; i < 6; i++) $('lectures').append(h('<div class="skeleton" style="height:70px;border-radius:16px;margin-bottom:10px"></div>'));
  S.chatScope = 'course'; S.chatLogs = {}; S.pendingChat = null;
  resetChat();
  show('course');
  send({ type: 'openCourse', id: c.id, course: c.name });
}
$('backToCourses').onclick = () => { S.course = null; show('courses'); };
$('reloadCourse').onclick = () => S.course && openCourse(S.course);
$('openCourseFolder').onclick = () => S.local && send({ type: 'openPath', path: S.local.dir });
const plainSections = () => S.sections.map(s => ({ num: s.num, title: s.title, items: s.items }));
function syncCourse(quiet) {
  if (!S.sections.length) return;
  send({ type: 'syncCourse', course: S.course.name, sections: plainSections() });
  if (!quiet) toast('מעדכן את כל הקורס מה-Moodle... אפשר להמשיך לעבוד בינתיים');
}
$('syncAll').onclick = () => syncCourse(false);
$('learnAll').onclick = () => {
  if (!S.sections.length) return;
  send({ type: 'learnCourse', course: S.course.name, sections: plainSections() });
  toast(`${aiName()} מתחיל ללמוד את כל הקורס: מוריד הכל, קורא את הסילבוס, בונה דף לימוד לכל שיעור ולומד את המרצה מהמבחנים. זה לוקח זמן, אפשר להמשיך לעבוד.`, { ms: 8000 });
};
$('examPrepBtn').onclick = () => { setTab('exams'); setChatScope('exam'); };
$('toggleChat').onclick = () => { S.chatHidden = !S.chatHidden; $('courseBody').classList.toggle('no-chat', S.chatHidden); $('chat').hidden = S.chatHidden; };
$('lecturer').onchange = () => S.course && send({ type: 'setLecturer', course: S.course.name, name: $('lecturer').value.trim() });

document.querySelectorAll('#courseTabs button').forEach(b => b.onclick = () => setTab(b.dataset.tab));
function setTab(tab) {
  S.tab = tab;
  document.querySelectorAll('#courseTabs button').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
  document.querySelectorAll('.tab-pane').forEach(p => p.classList.toggle('active', p.id === 'pane-' + tab));
  if (tab === 'info') renderInfo();
  if (tab === 'exams') renderExams();
  if (tab === 'notes') renderNotes();
  if (tab === 'setup') renderSetup();
  if (tab === 'recs') renderRecs();
}

// ---------- Course setup: a few questions, then one "download everything" ----------
// A new course opens on this and nothing else. The app asks, the student answers, and only then it downloads
// everything at once and starts preparing the study material.
function moodleSummary() {
  const items = S.sections.flatMap(s => s.items);
  const isExam = (i) => /מבחן|בחינה|מועד [אבג]|exam/i.test(i.name) && i.mod !== 'quiz';
  return {
    files: items.filter(i => ['resource', 'file', 'folder', 'page', 'book'].includes(i.mod) && !isExam(i)).length,
    hw: items.filter(i => i.mod === 'assign').length,
    exams: items.filter(isExam).length,
    recs: items.filter(i => i.mod === 'zoom' && /zoom\.us\/rec/i.test(i.url || '')).length,
    zoomTool: items.some(i => i.mod === 'lti' && /zoom|זום/i.test(i.name))
  };
}
function setupAnswers() {
  const k = S.course ? S.course.name : '';
  S.setupAns = S.setupAns || {};
  return S.setupAns[k] = S.setupAns[k] || {};
}
function inSetup() { return !!(S.local && !S.local.setupDone && !S.local.studyPages); }
function applySetupMode() {
  const on = inSetup();
  document.querySelector('.course-actions').hidden = on;
  $('courseTabs').hidden = on;
  $('courseBody').classList.toggle('no-chat', on || S.chatHidden);
  $('chat').hidden = on || S.chatHidden;
  if (on) setTab('setup');
}

const SETUP_Q = [
  {
    key: 'lecturer',
    ask: () => 'מי המרצה של הקורס?',
    hint: () => $('lecturer').value ? 'מצאתי את השם ב-Moodle. אם זה לא נכון, תקן.' : 'זה עוזר לי לכתוב את החומר בסגנון שלו.',
    body: (done) => {
      const box = h('<div class="row wrap"></div>');
      const inp = h('<input type="text" dir="auto" placeholder="שם המרצה" style="max-width:300px">');
      inp.value = $('lecturer').value;
      const ok = h('<button class="primary">זה נכון</button>');
      ok.onclick = () => { $('lecturer').value = inp.value.trim(); $('lecturer').onchange(); done(inp.value.trim() || 'לא יודע'); };
      inp.onkeydown = (e) => { if (e.key === 'Enter') ok.onclick(); };
      box.append(inp, ok);
      return box;
    }
  },
  {
    key: 'moodle',
    ask: () => 'זה מה שמצאתי ב-Moodle. להוריד את הכל?',
    hint: () => {
      const m = moodleSummary();
      const parts = [`${m.files} מצגות וקבצים`, `${m.hw} מטלות ותרגילים`];
      if (m.exams) parts.push(`${m.exams} מבחנים`);
      if (m.recs) parts.push(`${m.recs} הקלטות`);
      let t = parts.join(' · ') + '.';
      if (!m.recs) t += m.zoomTool ? ' ההקלטות של הקורס נמצאות בכלי הזום של Moodle, שלא מאפשר הורדה, אז אכין הכל בלעדיהן.' : ' אין הקלטות בקורס, וזה בסדר.';
      return t;
    },
    body: (done) => {
      const box = h('<div class="row wrap"></div>');
      const yes = h('<button class="primary">כן, את הכל</button>'); yes.onclick = () => done('כן, את הכל');
      const more = h('<button class="ghost">יש לי עוד קבצים במחשב</button>'); more.onclick = () => send({ type: 'addFiles', course: S.course.name, folder: 'מידע על הקורס' });
      box.append(yes, more);
      return box;
    }
  },
  {
    key: 'exams',
    ask: () => 'יש לך מבחנים משנים קודמות, עדיף עם פתרונות?',
    hint: () => `הם הכי חשובים כדי להבין איך המרצה בוחן.${(S.local && S.local.exams) ? ` יש כבר ${S.local.exams} קבצי מבחנים.` : ''} ב-CS24 יש מבחנים של רוב הקורסים: כל קובץ שתוריד שם נשמר אצלי לבד.`,
    body: (done) => {
      const box = h('<div class="row wrap"></div>');
      const cs = h('<button class="primary">חפש לי ב-CS24</button>'); cs.onclick = () => { $('dlCourse').value = S.course.name; $('dlFolder').value = 'מבחנים'; syncDownloadTarget(); openSite('cs24'); done('מ-CS24'); };
      const pc = h('<button class="ghost">יש לי במחשב</button>'); pc.onclick = () => { send({ type: 'addFiles', course: S.course.name, folder: 'מבחנים' }); done('מהמחשב'); };
      const no = h('<button class="ghost">אין לי</button>'); no.onclick = () => done('אין');
      box.append(cs, pc, no);
      return box;
    }
  },
  {
    key: 'cs24',
    ask: () => 'רוצה גם סיכומים ומחברות של סטודנטים מ-CS24?',
    hint: () => 'לא חובה. מחברת טובה של סטודנט מהקורס עוזרת לי להבין מה באמת נלמד בכיתה.',
    body: (done) => {
      const box = h('<div class="row wrap"></div>');
      const cs = h('<button class="primary">פתח את CS24</button>'); cs.onclick = () => { $('dlCourse').value = S.course.name; $('dlFolder').value = 'CS24'; syncDownloadTarget(); openSite('cs24'); done('כן'); };
      const no = h('<button class="ghost">לא צריך</button>'); no.onclick = () => done('לא');
      box.append(cs, no);
      return box;
    }
  },
  {
    key: 'notes',
    ask: () => 'אתה כותב בהרצאות ב-Notability?',
    hint: () => 'אם כן, אשתמש גם במחברת שלך. מייצאים אותה ל-PDF לתיקייה "מחברת" של הקורס.',
    body: (done) => {
      const box = h('<div class="row wrap"></div>');
      const y = h('<button class="primary">כן, פתח את Notability</button>'); y.onclick = () => { send({ type: 'openNotability', course: S.course.name }); done('כן'); };
      const n = h('<button class="ghost">לא</button>'); n.onclick = () => done('לא');
      box.append(y, n);
      return box;
    }
  }
];

function renderSetup() {
  const p = $('pane-setup'); p.innerHTML = '';
  const ans = setupAnswers();
  const current = SETUP_Q.find(q => !(q.key in ans));
  const answered = SETUP_Q.filter(q => q.key in ans);
  $('tabSetup').textContent = current ? SETUP_Q.length - answered.length : '✓';
  const wrap = h(`<div class="card pad-lg setup-step"><div class="step-dots">${SETUP_Q.map(q => `<i class="${q.key in ans ? 'on' : q === current ? 'now' : ''}"></i>`).join('')}<i class="${current ? '' : 'now'}"></i></div><div class="qa"></div></div>`);
  const qa = wrap.querySelector('.qa');
  // What was already asked and answered, as a short conversation.
  answered.forEach(q => {
    const asked = h(`<div class="qa-row"><span class="qa-av">${ART.mascot()}</span><div class="qa-q"></div></div>`);
    asked.querySelector('.qa-q').textContent = q.ask();
    qa.append(asked);
    const a = h('<div class="qa-a" dir="auto"></div>'); a.textContent = ans[q.key]; qa.append(a);
  });
  if (current) {
    const row = h(`<div class="qa-row now"><span class="qa-av">${ART.mascot('wave')}</span><div><div class="qa-q"></div><div class="qa-hint muted" dir="auto"></div></div></div>`);
    row.querySelector('.qa-q').textContent = current.ask();
    row.querySelector('.qa-hint').textContent = current.hint();
    qa.append(row);
    const body = current.body((answer) => { ans[current.key] = answer; renderSetup(); });
    body.classList.add('qa-body');
    qa.append(body);
  } else {
    // Everything answered: one button downloads it all and starts preparing.
    const L = S.local || {};
    const m = moodleSummary();
    const busy = S.setupRunning && S.setupRunning[S.course.name];
    const list = [
      ['book', `מ-Moodle: ${m.files} מצגות וקבצים, ${m.hw} מטלות${m.exams ? `, ${m.exams} מבחנים` : ''}${m.recs ? `, ${m.recs} הקלטות` : ''}`],
      ['exam', `מבחנים קודמים: ${L.exams ? L.exams + ' קבצים' : ans.exams === 'אין' ? 'אין' : 'מה שתוריד מ-CS24 ייכנס לבד'}`],
      ['note', `CS24 ומחברת: ${(L.cs24 || 0) + (L.notes || 0)} קבצים`],
      ['sparkles', 'ואז: דף לימוד לכל שיעור, עזרה בשיעורי הבית והכנה למבחן']
    ];
    const row = h(`<div class="qa-row now"><span class="qa-av">${ART.mascot('wave')}</span><div><div class="qa-q">מעולה, יש לי את כל מה שצריך. זה מה שאני הולך לעשות:</div></div></div>`);
    qa.append(row);
    const ul = h('<div class="qa-body" style="display:grid;gap:8px"></div>');
    list.forEach(([ic, t]) => { const r = h(`<div class="check-row"><div class="ci" data-ic="${ic}"></div><div class="grow"><small style="color:var(--text);font-size:13.5px"></small></div></div>`); r.querySelector('small').textContent = t; ul.append(r); });
    const go = h(`<button class="primary big" style="margin-top:8px">${busy ? '<span class="spinner"></span> מוריד ומכין...' : '<span data-ic="download"></span>הורד הכל והתחל להכין'}</button>`);
    icons(go);
    go.disabled = !!busy;
    go.onclick = () => {
      S.setupRunning = S.setupRunning || {}; S.setupRunning[S.course.name] = true;
      S.learnAfterSync = S.learnAfterSync || {}; S.learnAfterSync[S.course.name] = true;
      send({ type: 'setupDone', course: S.course.name });
      syncCourse(true);
      renderSetup();
      toast('מוריד את כל החומר. כשזה ייגמר אתחיל להכין את דפי הלימוד, ואפשר להמשיך לעבוד בינתיים.', { ms: 7000 });
    };
    const back = h('<button class="ghost" style="margin-top:8px">שנה תשובות</button>');
    back.onclick = () => { S.setupAns[S.course.name] = {}; renderSetup(); };
    ul.append(h('<div class="row wrap"></div>'));
    ul.lastChild.append(go, back);
    qa.append(ul);
  }
  p.append(wrap);
}

// ---------- Recordings of the course ----------
function renderRecs() {
  const p = $('pane-recs'); p.innerHTML = '';
  const zoomTool = S.sections.flatMap(s => s.items).find(i => i.mod === 'lti' && /zoom|זום/i.test(i.name));
  if (zoomTool) {
    const c = h(`<div class="card pad-lg" style="margin-bottom:14px"><div class="row" style="gap:14px"><span class="ic zoom" data-ic="video"></span>
      <div class="grow"><b>הקלטות הזום של הקורס</b><div class="muted" style="font-size:12.5px">נפתח את עמוד הזום של הקורס כאן באפליקציה. נכנסים ל-Cloud Recordings, בוחרים שיעור ולוחצים להורדה. ההקלטה נשמרת בתיקיית ההקלטות של הקורס.</div></div>
      <button class="primary">פתח את הקלטות הזום</button></div></div>`);
    c.querySelector('button').onclick = () => { $('dlCourse').value = S.course.name; $('dlFolder').value = 'הקלטה'; syncDownloadTarget(); openSite('moodle', zoomTool.url); };
    p.append(c);
  }
  // Only recordings that really exist: the ones already downloaded, and the ones downloading now.
  const have = S.sections.filter(s => s.local && s.local.videos);
  const busy = [...S.jobs.values()].filter(j => j.course === S.course.name && (j.state === 'running' || j.state === 'queued'));
  if (!have.length && !busy.length) {
    p.append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>${zoomTool ? 'עוד לא הורדו הקלטות' : 'אין הקלטות בקורס הזה'}</b>${zoomTool ? 'לחץ "פתח את הקלטות הזום" ובחר את השיעור.' : 'זה בסדר, הסיכומים וההכנה למבחן עובדים גם בלי הקלטות.'}</div>`));
    return;
  }
  const list = h('<div class="card pad-lg"><h2>הקלטות שהורדו</h2><div class="rec-list" style="margin-top:12px"></div></div>');
  busy.forEach(j => {
    const row = h(`<div class="check-row"><div class="ci"><span class="spinner"></span></div><div class="grow"><b dir="auto"></b><small dir="auto"></small></div></div>`);
    row.querySelector('b').textContent = j.name; row.querySelector('small').textContent = j.status;
    list.querySelector('.rec-list').append(row);
  });
  have.forEach(s => {
    const row = h(`<div class="check-row ok"><div class="ci" data-ic="check"></div><div class="grow"><b dir="auto"></b><small>הקלטה ותמלול</small></div><button class="small">פתח</button></div>`);
    row.querySelector('b').textContent = 'שיעור ' + s.num + ' · ' + s.title;
    row.querySelector('button').onclick = () => send({ type: 'openPath', path: s.local.dir + '\\הקלטה' });
    list.querySelector('.rec-list').append(row);
  });
  p.append(list);
}

function renderCourseHeader() {
  const L = S.local || {};
  const n = S.sections.filter(s => s.num > 0 && s.items.length).length;
  $('courseCount').textContent = n + ' שיעורים';
  $('tabLessons').textContent = n;
  $('tabExams').textContent = L.exams || 0;
  $('tabNotes').textContent = L.notes || 0;
  $('courseReady').hidden = !L.studyPages;
  $('courseReady').textContent = (L.studyPages || 0) + ' דפי לימוד מוכנים';
}

function renderLectures() {
  const box = $('lectures'); box.innerHTML = '';
  const secs = S.sections.filter(s => s.items.length || (s.local && s.local.exists));
  if (!secs.length) { box.append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>לא נמצאו שיעורים</b>דף הקורס ב-Moodle ריק, או שהמבנה שלו לא מוכר.</div>`)); return; }
  secs.forEach((s, i) => { const el = lectureEl(s); el.style.setProperty('--i', i); box.append(el); });
  box.classList.add('stagger');
}

function lectureEl(s) {
  const el = h(`<div class="lecture" data-num="${s.num}">
    <div class="lecture-head"><div class="num"></div><div class="lecture-main"><div class="lecture-title" dir="auto"></div><div class="chips"></div></div><span class="chevron" data-ic="forward" style="transform:rotate(-90deg)"></span></div>
    <div class="lecture-body"><div><div class="lecture-inner"><ul class="items"></ul><div class="actions"></div><div class="status"></div><div class="ai-log"></div></div></div></div></div>`);
  el.querySelector('.chevron').style.transform = '';
  el.querySelector('.chevron').innerHTML = svgI('<path d="m6 9 6 6 6-6"/>');
  el.querySelector('.lecture-head').onclick = () => { el.classList.toggle('open'); el.classList.contains('open') ? S.open.add(s.num) : S.open.delete(s.num); };
  if (S.open.has(s.num)) el.classList.add('open');
  fillLecture(el, s);
  return el;
}

function fillLecture(el, s) {
  const L = s.local || {};
  const course = S.course;
  el.classList.toggle('ready', !!L.studyPage);
  el.querySelector('.num').textContent = s.num === 0 ? '★' : s.num;
  el.querySelector('.lecture-title').textContent = s.num === 0 ? 'כללי · ' + s.title : s.title;
  const files = s.items.filter(i => !['zoom', 'url', 'forum', 'quiz'].includes(i.mod)).length;
  const zooms = s.items.filter(i => i.mod === 'zoom').length;
  const assigns = s.items.filter(i => i.mod === 'assign').length;
  const chips = [];
  if (files) chips.push(`<span class="chip">${files} קבצים</span>`);
  if (assigns) chips.push(`<span class="chip warn">${assigns} מטלות</span>`);
  if (zooms) chips.push(`<span class="chip accent">${zooms} הקלטות</span>`);
  if (L.files) chips.push(`<span class="chip ok">במחשב · ${L.files}</span>`);
  if (L.videos) chips.push('<span class="chip ok">הקלטה במחשב</span>');
  if (L.studyPage) chips.push('<span class="chip ok">דף לימוד מוכן ✓</span>');
  el.querySelector('.chips').innerHTML = chips.join('');

  const items = el.querySelector('.items');
  items.innerHTML = '';
  if (!s.items.length) items.append(h('<li class="muted">אין פריטים ב-Moodle</li>'));
  s.items.forEach(i => {
    const li = h(`<li><span class="ic ${['resource', 'file', 'assign', 'zoom'].includes(i.mod) ? i.mod : 'other'}" data-ic="${modIcon[i.mod] || 'file'}"></span><span class="nm" dir="auto"></span>${i.dates ? '<span class="dates" dir="auto"></span>' : ''}<span class="kind"></span></li>`);
    li.querySelector('.nm').textContent = i.name;
    li.querySelector('.kind').textContent = modLabel[i.mod] || i.mod;
    if (i.dates) li.querySelector('.dates').textContent = i.dates;
    items.append(li);
  });

  const a = el.querySelector('.actions'); a.innerHTML = '';
  const btn = (ic, text, cls, fn, disabled) => { const b = h(`<button class="${cls}"><span data-ic="${ic}"></span>${text}</button>`); b.onclick = (e) => { e.stopPropagation(); fn(); }; b.disabled = !!disabled; a.append(b); return b; };
  btn('download', 'הורד', L.files ? '' : 'primary', () => send({ type: 'syncSection', course: course.name, section: { num: s.num, title: s.title, items: s.items } }), s.busy);
  if (s.aiBusy) btn('x', 'עצור', 'danger', () => send({ type: 'cancelAi', key: `sum|${course.name}|${s.num}` }));
  else btn('sparkles', L.studyPage ? 'בנה מחדש' : 'סכם את השיעור', L.studyPage || !L.files ? 'soft' : 'primary',
    () => send({ type: 'summarize', course: course.name, num: s.num, title: s.title }), s.busy || !(L.files || L.transcripts));
  if (L.studyPage) btn('external', 'פתח דף לימוד', 'primary', () => send({ type: 'openPath', path: L.studyPage }));
  if (L.exercises || assigns) btn('exam', 'הכן לשיעורי הבית', 'soft', () => send({ type: 'homework', course: course.name, num: s.num, title: s.title }), s.busy || s.aiBusy || !L.exercises);
  (L.pointers || []).forEach(p => btn('file', 'תרגיל עם מפנים: ' + esc(p.name.replace(' - מפנים', '')), '', () => send({ type: 'openPath', path: p.path })));
  btn('video', 'הוסף הקלטה', 'ghost', () => {
    if (el.querySelector('.rec-row')) return;
    const row = h(`<div class="row rec-row" style="margin-top:10px"><input type="text" class="ltr" placeholder="הדבק קישור להקלטת Zoom של השיעור" spellcheck="false"><button class="primary small">הורד</button></div>`);
    const go = () => { const v = row.querySelector('input').value.trim(); if (!v) return; send({ type: 'addRecording', course: course.name, num: s.num, url: v }); row.remove(); };
    row.querySelector('button').onclick = go;
    row.querySelector('input').onkeydown = (e) => { if (e.key === 'Enter') go(); };
    a.after(row); row.querySelector('input').focus();
  });
  btn('plus', 'הוסף קבצים', 'ghost', () => send({ type: 'addFiles', course: course.name, folder: String(s.num) }));
  if (L.exists) btn('folder', 'תיקייה', 'ghost', () => send({ type: 'openPath', path: L.dir }));
  if (s.num > 0) btn('chat', 'שאל על השיעור', 'ghost', () => setChatScope('lesson:' + s.num));

  const job = lessonJob(s.num);
  const st = el.querySelector('.status');
  st.className = 'status' + (s.statusError || (job && job.state === 'failed') ? ' error' : '');
  const text = s.status || (job ? 'הקלטה: ' + job.status : '') || (!L.files && s.items.length ? 'החומר יורד אוטומטית כשנכנסים לקורס. אפשר גם ללחוץ "הורד".' : '');
  st.innerHTML = (s.busy || s.aiBusy || (job && job.state !== 'failed') ? '<span class="spinner"></span>' : '') + `<span dir="auto">${esc(text)}</span>`;
}
function section(num) { return S.sections.find(s => s.num === num); }
function refreshSection(num) {
  const s = section(num); if (!s) return;
  const el = document.querySelector(`.lecture[data-num="${num}"]`);
  if (el) fillLecture(el, s); else renderLectures();
}

function courseStatus(text, busy, error) {
  const b = $('courseStatus');
  b.hidden = !text;
  b.className = 'status-bar' + (error ? ' error' : '');
  b.innerHTML = (busy ? '<span class="spinner"></span>' : error ? IC.alert : '') + `<span dir="auto">${esc(text || '')}</span>`;
}

function renderInfo() {
  const p = $('pane-info'); p.innerHTML = '';
  const L = S.local || {};
  const info = L.info;
  const busy = S.infoBusy;
  if (!info) {
    p.append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>עוד לא ניתחנו את הקורס</b>
      ${aiName()} יקרא את הסילבוס ואת המידע הכללי ויסכם: מרצה, הרכב הציון, האם שיעורי הבית חובה, מבנה המבחן ותאריכים חשובים.
      <div class="row"><button class="primary" id="analyzeBtn"><span data-ic="sparkles"></span>נתח את הקורס</button>
      <button class="ghost" id="addSyl"><span data-ic="plus"></span>הוסף סילבוס מהמחשב</button></div></div>`));
    $('analyzeBtn').disabled = busy || !L.exists;
    if (!L.exists) $('analyzeBtn').title = 'הורד קודם את הקורס';
    $('analyzeBtn').onclick = () => { S.infoBusy = true; send({ type: 'analyzeCourse', course: S.course.name }); renderInfo(); };
    $('addSyl').onclick = () => send({ type: 'addFiles', course: S.course.name, folder: 'מידע על הקורס' });
    if (!L.exists) p.querySelector('.empty').append(h('<div class="muted" style="margin-top:10px;font-size:12.5px">קודם לחץ "הורד את כל הקורס" למעלה.</div>'));
    return;
  }
  const hw = info.homeworkRequired === true ? ['bad', 'חובה'] : info.homeworkRequired === false ? ['ok', 'לא חובה'] : ['', 'לא צוין'];
  const colors = ['#6366f1', '#ec4899', '#f59e0b', '#10b981', '#0ea5e9', '#8b5cf6', '#ef4444'];
  const grading = (info.grading || []).filter(g => g && g.component);
  const total = grading.reduce((a, g) => a + (+g.weight || 0), 0) || 1;
  const wrap = h('<div class="stagger"></div>');
  wrap.append(h(`<div class="info-grid">
    <div class="card stat" style="--i:0"><span class="si ic zoom" data-ic="user"></span><div><small>מרצה</small><b dir="auto">${esc(info.lecturer || $('lecturer').value || 'לא צוין')}</b>${info.officeHours ? `<small dir="auto">שעות קבלה: ${esc(info.officeHours)}</small>` : ''}</div></div>
    <div class="card stat" style="--i:1"><span class="si ic assign" data-ic="exam"></span><div><small>שיעורי בית</small><b><span class="chip ${hw[0]}">${hw[1]}</span></b>${info.homeworkNote ? `<small dir="auto">${esc(info.homeworkNote)}</small>` : ''}</div></div>
    <div class="card stat" style="--i:2"><span class="si ic file" data-ic="calendar"></span><div><small>מבנה המבחן</small><b dir="auto" style="font-size:13.5px">${esc(info.examFormat || 'לא צוין')}</b>${info.materialsAllowed ? `<small dir="auto">חומר עזר: ${esc(info.materialsAllowed)}</small>` : ''}</div></div>
    ${info.attendance ? `<div class="card stat" style="--i:3"><span class="si ic other" data-ic="check"></span><div><small>נוכחות</small><b dir="auto" style="font-size:13.5px">${esc(info.attendance)}</b></div></div>` : ''}
  </div>`));
  if (grading.length) {
    const c = h(`<div class="card" style="margin-top:14px;--i:4"><h2>הרכב הציון</h2><div class="grade-bar">${grading.map((g, i) => `<span style="flex-grow:${+g.weight || 0};background:${colors[i % colors.length]}"></span>`).join('')}</div>
      <div class="legend">${grading.map((g, i) => `<div><i style="background:${colors[i % colors.length]}"></i><span dir="auto">${esc(g.component)}</span><b>${esc(g.weight)}%</b></div>`).join('')}</div></div>`);
    wrap.append(c);
  }
  const dates = (info.importantDates || []).filter(d => d && d.what);
  if (dates.length) wrap.append(h(`<div class="card" style="margin-top:14px;--i:5"><h2>תאריכים חשובים</h2><div class="dates-list" style="margin-top:8px">${dates.map(d => `<div><b class="ltr">${esc(d.date || '')}</b><span dir="auto">${esc(d.what)}</span></div>`).join('')}</div></div>`));
  const tips = (info.tips || []).filter(Boolean);
  if (tips.length) wrap.append(h(`<div class="card" style="margin-top:14px;--i:6"><h2>טיפים לקורס</h2><ul class="tips">${tips.map(t => `<li dir="auto">${esc(t)}</li>`).join('')}</ul></div>`));
  const row = h('<div class="row wrap" style="margin-top:14px"></div>');
  if (L.courseInfoMd) { const b = h(`<button class="primary"><span data-ic="external"></span>הסיכום המלא</button>`); b.onclick = () => send({ type: 'openPath', path: L.courseInfoMd }); row.append(b); }
  const r = h(`<button class="ghost"><span data-ic="refresh"></span>נתח מחדש</button>`); r.disabled = busy; r.onclick = () => { S.infoBusy = true; send({ type: 'analyzeCourse', course: S.course.name }); renderInfo(); }; row.append(r);
  wrap.append(row);
  p.append(wrap);
}

function renderExams() {
  const p = $('pane-exams'); p.innerHTML = '';
  const L = S.local || {};
  const busy = S.examBusy;
  const card = h(`<div class="card pad-lg">
    <div class="row" style="align-items:flex-start;gap:18px"><div style="width:150px;flex:none">${ART.chat().replace('<svg', '<svg style="width:150px;height:104px"')}</div>
    <div class="grow"><h2>הכנה למבחן</h2><p class="muted" style="margin:6px 0 14px">מוסיפים מבחנים משנים קודמות, עדיף עם פתרונות. ${aiName()} קורא אותם יחד עם ההרצאות והתרגילים ומבין אילו נושאים חוזרים, איזה שאלות המרצה נותן ואיך הוא רוצה שתפתור. בסוף מקבלים דף הכנה למבחן, ואפשר להתאמן בשאלות בסגנון שלו.</p>
    <div class="row wrap"><span class="chip ${L.exams ? 'ok' : ''}">${L.exams || 0} קבצי מבחנים</span>${L.lecturerProfile ? '<span class="chip ok">פרופיל מרצה מוכן ✓</span>' : ''}${L.examPrep ? '<span class="chip ok">דף הכנה מוכן ✓</span>' : ''}</div></div></div>
    <div class="row wrap" style="margin-top:18px" id="examActs"></div></div>`);
  p.append(card);
  const acts = card.querySelector('#examActs');
  const add = (ic, t, cls, fn, dis) => { const b = h(`<button class="${cls}"><span data-ic="${ic}"></span>${t}</button>`); b.onclick = fn; b.disabled = !!dis; acts.append(b); };
  add('exam', 'מבחנים מ-CS24', 'soft', () => { $('dlCourse').value = S.course.name; $('dlFolder').value = 'מבחנים'; syncDownloadTarget(); openSite('cs24'); toast('כל קובץ שתוריד מ-CS24 יישמר בתיקיית המבחנים של הקורס'); });
  add('book', 'סיכומים ומחברות מ-CS24', 'soft', () => { $('dlCourse').value = S.course.name; $('dlFolder').value = 'CS24'; syncDownloadTarget(); openSite('cs24'); toast('סיכומים ומחברות שתוריד מ-CS24 יישמרו בקורס, וה-AI ישתמש בהם'); });
  add('plus', 'הוסף מבחנים מהמחשב', 'ghost', () => send({ type: 'addFiles', course: S.course.name, folder: 'מבחנים' }));
  if (L.exams) add('folder', 'תיקיית המבחנים', 'ghost', () => send({ type: 'openPath', path: L.dir + '\\מבחנים' }));
  add('sparkles', busy ? 'עובד על זה...' : (L.examPrep ? 'בנה מחדש את דף ההכנה' : 'בנה דף הכנה למבחן'), 'primary', () => { S.examBusy = true; send({ type: 'examPrep', course: S.course.name }); renderExams(); }, busy || !L.exams);
  add('chat', 'התאמן בשאלות', 'soft', () => setChatScope('exam'), !L.exams);
  if (L.examPrep) add('external', 'דף ההכנה למבחן', 'primary', () => send({ type: 'openPath', path: L.examPrep }));
  if (L.lecturerProfile) add('user', 'פרופיל המרצה', '', () => send({ type: 'openPath', path: L.lecturerProfile }));
}

// Notability's own site inside the app: sign in once, open a note, and "Export as PDF" saves it into this course.
function notabilityWebButton() {
  const card = h(`<div class="card" style="margin-top:14px"><div class="row" style="gap:14px"><span class="ic other" data-ic="note"></span>
    <div class="grow"><b>או ישר מהחשבון שלך ב-Notability</b><div class="muted" style="font-size:12.5px">פותחים את Notability בתוך האפליקציה ומתחברים לחשבון. כל מחברת שמייצאים ל-PDF נשמרת לבד בתיקיית המחברת של הקורס.</div></div>
    <button class="primary">פתח את Notability</button></div></div>`);
  card.querySelector('button').onclick = () => { $('dlCourse').value = S.course.name; $('dlFolder').value = 'מחברת'; syncDownloadTarget(); openSite('notability'); };
  return card;
}

function renderNotes() {
  const p = $('pane-notes'); p.innerHTML = '';
  const N = S.notability || {};
  const L = S.local || {};
  if (!N.configured) {
    p.append(h(`<div class="empty">${ART.folders().replace('<svg', '<svg class="art" style="width:200px;height:120px"')}<b>חבר את המחברת מ-Notability</b>
      ב-Notability באייפד: הגדרות › גיבוי אוטומטי › Google Drive / OneDrive / Dropbox, בפורמט PDF.<br>כשהתיקייה מסונכרנת למחשב, בחר אותה כאן. המחברות של הקורס ייכנסו לסיכומים ולצ'אט.
      <div class="row"><button class="primary" id="pickNot"><span data-ic="folder"></span>בחר את תיקיית הגיבוי</button></div></div>`));
    $('pickNot').onclick = () => send({ type: 'chooseFolder', which: 'notability' });
    p.append(notabilityWebButton());
    return;
  }
  const opts = ['<option value="">בחר את הנושא ב-Notability...</option>'].concat((N.subjects || []).map(s => `<option ${s === (N.linked || N.guess) ? 'selected' : ''}>${esc(s)}</option>`)).join('');
  const card = h(`<div class="card pad-lg"><div class="row" style="gap:18px;align-items:flex-start"><div style="width:150px;flex:none">${ART.folders().replace('<svg', '<svg style="width:150px;height:96px"')}</div>
    <div class="grow"><h2>המחברת שלי</h2><p class="muted" style="margin:6px 0 14px">בחר את הנושא ב-Notability שמתאים לקורס. כל דף חדש שתכתוב בהרצאה יועתק לכאן לבד, ו-${aiName()} ישתמש בו בסיכומים.</p>
    <div class="row"><select id="notSubject">${opts}</select><button class="primary" id="notLink">${N.linked ? 'עדכן' : 'קשר'}</button></div>
    <div class="row wrap" style="margin-top:12px">${N.linked ? `<span class="chip ok">מקושר ל"${esc(N.linked)}"</span>` : N.guess ? `<span class="chip accent">הצעה: ${esc(N.guess)}</span>` : ''}<span class="chip">${N.notes || 0} דפי מחברת</span></div></div></div></div>`);
  p.append(card);
  $('notLink').onclick = () => send({ type: 'linkNotability', course: S.course.name, folder: $('notSubject').value });
  p.append(notabilityWebButton());
  if (N.notes) { const b = h('<button class="ghost" style="margin-top:12px"><span data-ic="folder"></span>פתח את המחברת</button>'); b.onclick = () => send({ type: 'openPath', path: L.dir + '\\מחברת' }); p.append(b); }
}

// ---------- Chat ----------
// Each course has separate conversations: the whole course, each lesson ("lesson:N"), and exam practice ("exam").
S.chatScope = 'course';
S.chatLogs = {};
function scopeTitle(scope) {
  if (scope === 'exam') return 'אימון למבחן';
  if (scope.startsWith('lesson:')) return 'שיעור ' + scope.slice(7);
  return 'כל הקורס';
}
function setChatScope(scope) {
  if (S.chatBusy) return toast('רגע, העוזר עוד עונה');
  S.chatScope = scope;
  if (S.chatHidden) $('toggleChat').click();
  resetChat();
  (S.chatLogs[scope] || []).forEach(x => addMsg(x.role, x.text, true));
  if (scope === 'exam' && !(S.chatLogs.exam || []).length) { $('chatText').value = 'בוא נתחיל אימון. תן לי שאלה ראשונה בסגנון המבחנים של המרצה.'; }
  $('chatText').focus();
}
function resetChat() {
  const scope = S.chatScope;
  $('chatTitle').textContent = 'העוזר של הקורס · ' + scopeTitle(scope);
  $('chatSub').textContent = `${aiName()} · ${scope === 'exam' ? 'שואל אותך שאלות בסגנון המבחנים' : 'מכיר את כל החומר שהורד'}`;
  $('chatBack').hidden = scope === 'course';
  $('msgs').innerHTML = '';
  const intro = scope === 'exam' ? '<b>אימון למבחן</b>אני אשאל אותך שאלות בסגנון של המרצה, לפי המבחנים הקודמים, ואבדוק את התשובות שלך.'
    : scope.startsWith('lesson:') ? `<b>שיחה על ${scopeTitle(scope)}</b>שאל על החומר של השיעור, על התרגיל או על מה שלא היה ברור בהרצאה.`
    : '<b>שאל אותי כל דבר על הקורס</b>אני קורא את המצגות, התרגילים, התמלולים והמבחנים. אם חסר משהו, אגיד לך בדיוק מאיפה להביא אותו.';
  const e = h(`<div class="chat-empty">${ART.chat().replace('<svg', '<svg class="art"')}${intro}<div class="suggest"></div></div>`);
  const qs = scope === 'exam' ? [['🎯', 'תן לי שאלה ראשונה בסגנון המבחנים של המרצה'], ['📊', 'מה הנושאים שהכי חוזרים במבחנים?']]
    : scope.startsWith('lesson:') ? [['📝', `תסכם לי את ${scopeTitle(scope)}`], ['💡', 'תעזור לי להתחיל את התרגיל של השיעור, רמזים בלבד'], ['❓', 'מה הכי חשוב לזכור מהשיעור הזה?']]
    : [['📝', 'מה למדנו עד עכשיו בקורס?'], ['🎯', 'מה הכי חשוב למבחן?'], ['🧭', 'מה עוד חסר לך כדי לעזור לי בקורס הזה?']];
  for (const [em, q] of qs) { const b = h(`<button class="ghost"></button>`); b.textContent = em + '  ' + q; b.onclick = () => { $('chatText').value = q; sendChat(); }; e.querySelector('.suggest').append(b); }
  $('msgs').append(e);
}
function addMsg(role, text, replay) {
  const empty = $('msgs').querySelector('.chat-empty'); if (empty) empty.remove();
  const d = document.createElement('div');
  d.className = 'msg ' + role; d.dir = 'auto';
  if (role === 'assistant') d.innerHTML = md(text); else d.textContent = text;
  $('msgs').append(d);
  $('msgs').scrollTop = $('msgs').scrollHeight;
  if (!replay && role !== 'tool') (S.chatLogs[S.chatScope] = S.chatLogs[S.chatScope] || []).push({ role, text });
  return d;
}
let thinking = null;
function setThinking(text) {
  if (!thinking) { thinking = addMsg('tool', ''); }
  thinking.innerHTML = '<span class="typing"><i></i><i></i><i></i></span>';
  thinking.append(document.createTextNode(text || 'חושב...'));
  $('msgs').scrollTop = $('msgs').scrollHeight;
}
function sendChat() {
  const text = $('chatText').value.trim();
  if (!text || S.chatBusy || !S.course) return;
  addMsg('user', text);
  $('chatText').value = ''; autosize();
  S.chatBusy = true; $('chatSend').disabled = true;
  setThinking('קורא את החומר...');
  send({ type: 'chat', course: S.course.name, scope: S.chatScope, text });
}
$('chatSend').onclick = sendChat;
$('chatText').addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendChat(); } });
const autosize = () => { const t = $('chatText'); t.style.height = 'auto'; t.style.height = Math.min(170, t.scrollHeight) + 'px'; };
$('chatText').addEventListener('input', autosize);
$('newChat').onclick = () => { if (!S.course || S.chatBusy) return; send({ type: 'chatReset', course: S.course.name, scope: S.chatScope }); S.chatLogs[S.chatScope] = []; resetChat(); };
$('chatBack').onclick = () => setChatScope('course');

// ---------- Recordings ----------
const jobIcons = { queued: 'clock', running: 'spin', done: 'check', failed: 'alert' };
function renderJob(j) {
  S.jobs.set(j.id, j);
  if (j.course && S.course && S.course.name === j.course) refreshSection(j.num);
  let el = document.querySelector(`.job[data-id="${j.id}"]`);
  if (!el) {
    el = h(`<li class="job" data-id="${j.id}"><div class="job-top"><div class="badge-ic"></div><div class="job-main"><div class="job-name" dir="auto"></div><div class="job-status" dir="auto"></div></div><div class="job-buttons"></div></div>
      <div class="bar"><i></i></div><details><summary>פרטים</summary><pre class="log"></pre></details></li>`);
    $('jobs').prepend(el);
  }
  el.className = 'job ' + j.state;
  el.querySelector('.badge-ic').innerHTML = j.state === 'running' ? '<span class="spinner"></span>' : IC[jobIcons[j.state]] || '';
  el.querySelector('.job-name').textContent = j.name || 'הקלטת Zoom';
  el.querySelector('.job-status').textContent = j.status;
  const bar = el.querySelector('.bar');
  bar.classList.toggle('indeterminate', j.progress < 0);
  bar.querySelector('i').style.width = j.progress < 0 ? '' : j.progress + '%';
  const b = el.querySelector('.job-buttons'); b.innerHTML = '';
  const btn = (label, fn, cls = 'small') => { const x = h(`<button class="${cls}">${label}</button>`); x.onclick = fn; b.append(x); };
  if (j.hasVideo) btn(IC.play + 'נגן', () => send({ type: 'openFile', id: j.id, which: 'video' }));
  if (j.hasTranscript) btn(IC.file + 'תמלול', () => send({ type: 'openFile', id: j.id, which: 'transcript' }));
  if (j.hasVideo || j.hasTranscript) btn(IC.folder, () => send({ type: 'showFile', id: j.id }), 'small icon');
  if (j.state === 'failed') btn(IC.refresh + 'נסה שוב', () => send({ type: 'retry', id: j.id }));
  if (j.state === 'done' || j.state === 'failed') btn(IC.x, () => { send({ type: 'remove', id: j.id }); el.remove(); S.jobs.delete(j.id); refreshJobs(); }, 'small icon ghost');
  S.jobs.set(j.id, j);
  refreshJobs();
}
function refreshJobs() {
  const e = $('jobsEmpty'); e.innerHTML = '';
  if (!S.jobs.size) e.append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>אין הורדות עכשיו</b>הקלטה של שיעור מוסיפים מתוך השיעור, בכפתור "הוסף הקלטה".</div>`));
  const active = [...S.jobs.values()].filter(j => j.state === 'running' || j.state === 'queued').length;
  $('jobCount').hidden = !active; $('jobCount').textContent = active;
  $('navDownloads').hidden = true;   // recordings live inside each course now
  if (S.view === 'course' && S.tab === 'recs') renderRecs();
}
// The recording that is downloading for a lesson right now, if any.
function lessonJob(num) {
  if (!S.course) return null;
  return [...S.jobs.values()].reverse().find(j => j.course === S.course.name && j.num === num && (j.state === 'running' || j.state === 'queued' || j.state === 'failed')) || null;
}
function jobLog(id, line) {
  const pre = document.querySelector(`.job[data-id="${id}"] pre`); if (!pre) return;
  pre.textContent += line + '\n';
  const lines = pre.textContent.split('\n');
  if (lines.length > 300) pre.textContent = lines.slice(-300).join('\n');
  pre.scrollTop = pre.scrollHeight;
}

// ---------- Sites ----------
function openSite(site, url) {
  S.site = site;
  document.querySelectorAll('#siteTabs button').forEach(b => b.classList.toggle('active', b.dataset.site === site));
  S.siteUrl = url || null;
  show('sites');
}
function placeSite(navigate) {
  if (S.view !== 'sites') return;
  const r = $('siteHost').getBoundingClientRect();
  const m = { x: r.left, y: r.top, w: r.width, h: r.height, vw: window.innerWidth };
  if (navigate) { send(Object.assign({ type: 'showSite', site: S.site, url: S.siteUrl || '' }, m)); S.siteUrl = null; }
  else send(Object.assign({ type: 'placeSite' }, m));
}
new ResizeObserver(() => placeSite(false)).observe($('siteHost'));
window.addEventListener('resize', () => placeSite(false));
document.querySelectorAll('#siteTabs button').forEach(b => b.onclick = () => openSite(b.dataset.site));
$('siteBack').onclick = () => send({ type: 'siteAction', action: 'back' });
$('siteForward').onclick = () => send({ type: 'siteAction', action: 'forward' });
$('siteReload').onclick = () => send({ type: 'siteAction', action: 'reload' });
$('siteExternal').onclick = () => send({ type: 'siteAction', action: 'external' });
function syncDownloadTarget() { send({ type: 'setDownloadTarget', course: $('dlCourse').value, folder: $('dlFolder').value }); $('dlFolder').disabled = !$('dlCourse').value; }
$('dlCourse').onchange = syncDownloadTarget;
$('dlFolder').onchange = syncDownloadTarget;
function fillDownloadCourses() {
  const cur = $('dlCourse').value;
  $('dlCourse').innerHTML = '<option value="">תיקיית "הורדות"</option>' + (S.courses || []).filter(c => c.current).map(c => `<option>${esc(c.name)}</option>`).join('');
  $('dlCourse').value = cur;
  $('dlFolder').disabled = !$('dlCourse').value;
}

// ---------- Settings ----------
function renderSettings() {
  const st = S.settings;
  $('studyRoot').textContent = st.studyRoot; $('recDir').textContent = st.recordingsDir;
  $('notabilityDir').textContent = st.notabilityDir || 'לא נבחרה';
  $('versionText').textContent = 'SummarIT ' + (st.version || '');
  $('wizStudy').textContent = st.studyRoot;
  $('wizNot').textContent = st.notabilityDir || 'אם Notability מגבה את המחברות לתיקייה במחשב, בחר אותה כאן';
  $('wizNotRow').classList.toggle('ok', !!st.notabilityDir);
  $('optFallback').checked = st.autoFallback !== false;
  $('optSync').checked = st.autoSync !== false; $('optRec').checked = st.autoRecordings !== false; $('optUpdate').checked = st.autoUpdate !== false;
  $('versionText').textContent = `SummarIT ${st.version || ''} · מתעדכנת לבד כשיוצאת גרסה חדשה`;
  applyTheme(st.theme || 'system');
}
$('changeStudy').onclick = $('wizStudyBtn').onclick = () => send({ type: 'chooseFolder', which: 'study' });
$('openStudy').onclick = () => send({ type: 'openFolder', which: 'study' });
$('changeRec').onclick = () => send({ type: 'chooseFolder', which: 'recordings' });
$('openRec').onclick = () => send({ type: 'openFolder', which: 'recordings' });
$('changeNotability').onclick = $('wizNotBtn').onclick = () => send({ type: 'chooseFolder', which: 'notability' });
$('showWelcome').onclick = () => openWizard(0);
$('themeSelect').onchange = () => { applyTheme($('themeSelect').value); send({ type: 'setOption', name: 'theme', value: S.theme }); };
$('themeToggle').onclick = () => {
  const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
  applyTheme(next); send({ type: 'setOption', name: 'theme', value: next });
};
[['optSync', 'autoSync'], ['optRec', 'autoRecordings'], ['optUpdate', 'autoUpdate'], ['optFallback', 'autoFallback']].forEach(([id, name]) =>
  $(id).onchange = () => send({ type: 'setOption', name, value: $(id).checked }));

// ---------- First-run setup ----------
let wizStep = 0, wizPoll = null;
// Order: welcome -> Moodle -> pick this semester's courses -> AI -> folders -> done (downloads start in the background).
const wizArt = ['welcome', 'campus', 'folders', 'ai', 'folders', 'done'];
const wizNext = ['בוא נתחיל', 'המשך', 'המשך', 'המשך', 'המשך', 'לאפליקציה'];
const LAST = wizNext.length - 1;
function renderWizardCourses() {
  const box = $('wizCourses'); if (!box) return;
  box.innerHTML = '';
  if (S.moodle !== 'ready') { box.append(h('<div class="check-row"><div class="ci" data-ic="alert"></div><div class="grow"><b>קודם צריך להתחבר ל-Moodle</b><small>חזור שלב אחד אחורה ולחץ "התחבר".</small></div></div>')); return; }
  if (!S.courses) { box.append(h('<div class="row" style="gap:10px"><span class="spinner"></span>מחפש את הקורסים שלך...</div>')); return; }
  const cur = S.courses.filter(c => c.current);
  if (!cur.length) { box.append(h('<div class="muted">לא מצאתי קורסים פעילים ב-Moodle. אפשר להמשיך, ולבחור קורסים אחר כך.</div>')); return; }
  S.wizPick = S.wizPick || new Set(cur.map(c => c.id));
  cur.forEach(c => {
    const row = h(`<label class="check-row" style="cursor:pointer"><input type="checkbox" style="width:18px;height:18px" ${S.wizPick.has(c.id) ? 'checked' : ''}><div class="grow"><b dir="auto"></b><small dir="auto"></small></div></label>`);
    row.querySelector('b').textContent = c.name; row.querySelector('small').textContent = c.category || '';
    row.querySelector('input').onchange = (e) => { e.target.checked ? S.wizPick.add(c.id) : S.wizPick.delete(c.id); };
    box.append(row);
  });
}
function openWizard(step) { $('wizard').hidden = false; goStep(step || 0); }
function closeWizard() {
  $('wizard').hidden = true; clearInterval(wizPoll); wizPoll = null;
  send({ type: 'finishOnboarding' });
}
function goStep(n) {
  wizStep = n;
  document.querySelectorAll('.step').forEach(s => s.classList.toggle('active', +s.dataset.step === n));
  document.querySelectorAll('#wizDots i').forEach((d, i) => d.classList.toggle('on', i === n));
  $('wizArt').innerHTML = ART[wizArt[n]]();
  $('wizNext').textContent = wizNext[n];
  $('wizBack').hidden = n === 0 || n === LAST;
  $('wizSkip').hidden = n === LAST;
  clearInterval(wizPoll);
  if (n === 1 || n === 3) wizPoll = setInterval(() => send({ type: 'checkStatus' }), 5000);
  if (n === 2 && S.moodle === 'ready' && !S.courses) send({ type: 'loadCourses' });
  if (n === LAST) confetti();
  renderProviders(); renderWizardMoodle(); renderWizardCourses();
}
$('wizNext').onclick = () => {
  if (wizStep === 1 && S.moodle !== 'ready') { toast('קודם מתחברים ל-Moodle, ואז ממשיכים'); return; }
  if (wizStep === 3 && !(S.ai && S.ai.ready)) toast('אפשר לחבר את המנוע החכם גם אחר כך, מהסטטוס בצד', { ms: 3500 });
  if (wizStep >= LAST) {
    // Start downloading the chosen courses in the background.
    const picked = (S.courses || []).filter(c => S.wizPick && S.wizPick.has(c.id)).map(c => ({ id: c.id, name: c.name }));
    if (picked.length) send({ type: 'prepareCourses', courses: picked });
    closeWizard(); show('home'); return;
  }
  goStep(wizStep + 1);
};
$('wizBack').onclick = () => goStep(Math.max(0, wizStep - 1));
$('wizSkip').onclick = closeWizard;
$('wizMoodleBtn').onclick = connectMoodle;
function renderWizardMoodle() {
  const ok = S.moodle === 'ready';
  $('wizMoodle').classList.toggle('ok', ok);
  $('wizMoodleTitle').textContent = ok ? `מחובר${S.userName ? ' · ' + S.userName : ''}` : S.moodle === 'connecting' ? 'מתחבר...' : 'עוד לא מחובר';
  $('wizMoodleSub').textContent = ok ? 'הכל מוכן. Moodle, Zoom והפורטל מחוברים.' : 'לחץ על "התחבר". חלון ההתחברות של HIT ייפתח כאן באפליקציה.';
  $('wizMoodleBtn').hidden = ok;
}

// ---------- Messages from the app ----------
function onMessage(m) {
  switch (m.type) {
    case 'settings':
      S.settings = m; S.ready.settings = true;
      if (m.userName) S.userName = m.userName;
      renderSettings(); renderStatus();
      if (!m.onboarded && $('wizard').hidden) openWizard(0);
      maybeHideSplash();
      break;
    case 'aiStatus': S.ai = m.status; S.ready.ai = true; renderStatus(); maybeHideSplash(); if (S.view === 'course') { $('chatSub').textContent = `${aiName()} · מכיר את כל החומר שהורדת`; } break;
    case 'moodle':
      S.moodle = m.state; renderStatus();
      if (m.state === 'error' && m.message) toast(m.message, { error: true });
      if (m.state === 'ready' && !S.courses) send({ type: 'loadCourses' });
      if (!$('wizard').hidden) renderWizardCourses();
      if (m.state === 'login' && S.view === 'courses' && !S.courses) { $('courses').innerHTML = ''; $('coursesState').innerHTML = ''; $('coursesState').append(h(`<div class="empty">${ART.campus().replace('<svg', '<svg class="art" style="width:220px;height:130px"')}<b>צריך להתחבר ל-Moodle</b>מתחברים פעם אחת עם המשתמש של HIT.<div class="row"><button class="primary" id="cm">התחבר</button></div></div>`)); $('cm').onclick = connectMoodle; }
      if (S.view === 'home') renderHome();
      break;
    case 'home': S.events = m.events || []; if (m.name) S.userName = m.name; renderHome(); renderWizardMoodle(); break;
    case 'courses':
      S.courses = m.data || []; renderCourses(); fillDownloadCourses();
      if (S.view === 'home') renderHome();
      if (!$('wizard').hidden) renderWizardCourses();
      break;
    case 'course': {
      if (!S.course || S.course.id !== m.id) break;
      S.sections = m.sections.map(s => Object.assign(s, { status: '', busy: false, aiBusy: false }));
      S.local = m.local; S.notability = m.notability;
      if (document.activeElement !== $('lecturer')) $('lecturer').value = m.lecturer || '';
      // The lecturer found in Moodle is offered in the setup questions, not announced with a pop-up.
      if (!m.lecturer && m.lecturerGuess && m.lecturerGuess.length && !$('lecturer').value) $('lecturer').value = m.lecturerGuess[0];
      renderCourseHeader(); renderLectures();
      // A course that hasn't been set up yet shows only the setup questions; afterwards the lessons.
      if (inSetup()) applySetupMode();
      else { applySetupMode(); if (!m.cached || S.tab === 'setup') setTab(S.tab === 'setup' || !S.tab ? 'lessons' : S.tab); }
      // Once a course is set up, new material is pulled from Moodle whenever it opens.
      if (!m.cached && !inSetup() && S.settings.autoSync !== false) syncCourse(true);
      break;
    }
    case 'learnStatus':
      if (!S.course || S.course.name !== m.course) break;
      courseStatus(m.text, m.busy, false);
      $('learnAll').disabled = m.busy;
      break;
    case 'courseError':
      if (S.course && S.course.id === m.id) { $('lectures').innerHTML = ''; $('lectures').append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>לא הצלחתי לקרוא את הקורס</b><span dir="auto">${esc(m.message)}</span></div>`)); }
      break;
    case 'sectionStatus': {
      if (!S.course || S.course.name !== m.course) break;
      const s = section(m.num); if (!s) break;
      s.status = m.text; s.statusError = m.error; if (!s.aiBusy) s.busy = m.busy;
      refreshSection(m.num); break;
    }
    case 'sectionLocal': {
      if (!S.course || S.course.name !== m.course) break;
      const s = section(m.num); if (s) { s.local = m.local; refreshSection(m.num); }
      if (S.tab === 'setup' || S.tab === 'recs') setTab(S.tab);
      break;
    }
    case 'courseLocal':
      if (!S.course || S.course.name !== m.course) break;
      S.local = m.local; S.notability = m.notability; renderCourseHeader();
      if (S.tab !== 'lessons') setTab(S.tab);
      if (S.courses) { const c = S.courses.find(x => x.name === m.course); if (c) c.local = m.local; }
      break;
    case 'courseStatus':
      if (!S.course || S.course.name !== m.course) break;
      courseStatus(m.text, m.busy, m.error);
      if (!m.busy) { S.infoBusy = false; S.examBusy = false; if (S.tab !== 'lessons') setTab(S.tab); }
      // After the setup questions: everything is downloaded, so start preparing the study material.
      if (m.synced && S.learnAfterSync && S.learnAfterSync[m.course]) {
        delete S.learnAfterSync[m.course];
        if (S.setupRunning) delete S.setupRunning[m.course];
        if (S.local) S.local.setupDone = true;
        applySetupMode(); setTab('lessons');
        send({ type: 'learnCourse', course: S.course.name, sections: plainSections() });
      }
      if (m.synced && S.pendingChat) {
        const p = S.pendingChat; S.pendingChat = null;
        if (p.scope !== S.chatScope) setChatScope(p.scope);
        S.chatBusy = true; $('chatSend').disabled = true; setThinking('החומר ירד. קורא אותו...');
        send({ type: 'chat', course: S.course.name, scope: p.scope, text: p.text });
      }
      break;
    case 'aiRun': {
      const parts = m.key.split('|');
      if (!S.course || S.course.name !== parts[1]) break;
      if (parts[0] === 'sum') {
        const s = section(+parts[2]); if (!s) break;
        s.aiBusy = m.busy; if (!m.busy) s.busy = false;
        refreshSection(s.num);
        const log = document.querySelector(`.lecture[data-num="${s.num}"] .ai-log`);
        // Plain steps while it works; at the end just "ready" - the AI's own notes aren't for the student.
        if (log && m.busy) { log.innerHTML = '<div class="phase now">מתחיל...</div>'; log.classList.add('show'); }
        if (log && !m.busy) { log.querySelectorAll('.phase').forEach(p => p.classList.remove('now')); log.append(h(`<div class="phase ${m.error ? 'bad' : 'done'}">${m.error ? 'לא הצליח. אפשר לנסות שוב.' : 'מוכן ✓'}</div>`)); }
      } else if (!m.busy && !m.error) toast(parts[0] === 'info' ? 'המידע על הקורס מוכן' : 'ההכנה למבחן מוכנה');
      break;
    }
    case 'aiLog': {
      const parts = m.key.split('|');
      if (!S.course || S.course.name !== parts[1] || parts[0] !== 'sum') break;
      const log = document.querySelector(`.lecture[data-num="${parts[2]}"] .ai-log`); if (!log) break;
      log.classList.add('show');
      log.querySelectorAll('.phase').forEach(p => { p.classList.remove('now'); if (!p.classList.contains('bad')) p.classList.add('done'); });
      const d = document.createElement('div'); d.className = 'phase now'; d.textContent = m.text; log.append(d);
      break;
    }
    case 'chat':
      if (!S.course || S.course.name !== m.course) break;
      if ((m.scope || 'course') !== S.chatScope) { (S.chatLogs[m.scope] = S.chatLogs[m.scope] || []).push({ role: m.role, text: m.text }); break; }
      if (thinking) { thinking.remove(); thinking = null; }
      if (m.role === 'limit') { addMsg('error', m.text); break; }
      addMsg(m.role, m.text);
      if (S.chatBusy && m.role === 'assistant') setThinking('ממשיך...');
      break;
    case 'chatTool': if (S.course && S.course.name === m.course && (m.scope || 'course') === S.chatScope) setThinking(m.text); break;
    case 'chatDone': if (thinking) { thinking.remove(); thinking = null; } S.chatBusy = false; $('chatSend').disabled = false; break;
    case 'needSync':
      // The assistant had nothing to read yet: download the course, then ask the same question again.
      if (!S.course || S.course.name !== m.course) break;
      S.pendingChat = { scope: m.scope, text: m.text };
      syncCourse(true);
      break;
    case 'aiLimit': showLimit(m); break;
    case 'geminiCode': showGeminiCode(); break;
    case 'job': renderJob(m); break;
    case 'log': jobLog(m.id, m.line); break;
    case 'clipboard': {
      // A copied Zoom link goes to the lesson that's open, or the latest lesson of the open course.
      if (!S.course || !S.sections.length) { toast('העתקת קישור להקלטת Zoom. פתח את הקורס ולחץ "הוסף הקלטה" בשיעור המתאים.'); break; }
      const open = [...S.open].filter(n => n > 0).sort((a, b) => b - a)[0];
      const latest = S.sections.filter(s => s.num > 0 && s.items.length).map(s => s.num).sort((a, b) => b - a)[0];
      const num = open || latest;
      if (num) toast(`העתקת קישור להקלטת Zoom`, { action: `הוסף לשיעור ${num}`, ms: 9000, onAction: () => send({ type: 'addRecording', course: S.course.name, num, url: m.url }) });
      break;
    }
    case 'toast': toast(m.text, { error: m.error }); break;
    case 'update': S.update = m; renderBanners(); break;
    case 'updateProgress': S.update = Object.assign(S.update || {}, { progress: m.text, error: m.error }); renderBanners(); break;
    case 'updateReady': S.updateReady = m.version; renderBanners(); toast(`העדכון לגרסה ${m.version} מוכן`, { action: 'הפעל מחדש', onAction: () => send({ type: 'restartForUpdate' }), ms: 12000 }); break;
    case 'notabilitySubjects': if (S.course) send({ type: 'openCourse', id: S.course.id, course: S.course.name }); break;
    case 'siteNav': $('siteUrl').textContent = m.url || ''; $('siteBack').disabled = !m.canBack; $('siteForward').disabled = !m.canForward; break;
    case 'timetable': S.timetable = m.data; renderTimetable(); break;
    case 'prepStatus':
      S.prep = m.busy ? m : null; renderBanners();
      if (!m.busy && m.total) toast(`הקורסים שבחרת מוכנים במחשב (${m.total})`);
      break;
    case 'timetableStatus':
      $('ttStatus').innerHTML = m.busy ? '<span class="spinner"></span> קורא את המערכת...' : '';
      if (!m.busy && m.text) toast(m.text, { error: m.error });
      break;
    case 'openSite':
      $('dlCourse').value = S.course ? S.course.name : ''; $('dlFolder').value = siteFolder[m.site] || 'כללי'; syncDownloadTarget();
      openSite(m.site);
      break;
    case 'siteDownload':
      if (m.state === 'done') toast(`נשמר: ${m.name}`, { action: 'פתח תיקייה', onAction: () => send({ type: 'openPath', path: m.dir }) });
      else if (m.state === 'failed') toast(`ההורדה של ${m.name} נכשלה`, { error: true });
      break;
  }
}

// ---------- Start ----------
let splashTips = ['מכין את סביבת הלימוד שלך...', 'מתחבר ל-Moodle...', 'בודק את המנוע החכם...', 'כמעט מוכן...'], tipI = 0;
const tipTimer = setInterval(() => { $('splashText').textContent = splashTips[++tipI % splashTips.length]; }, 1400);
function maybeHideSplash(force) {
  if (!force && !(S.ready.settings && S.ready.ai)) return;
  clearInterval(tipTimer);
  $('splash').classList.add('hide');
}
setTimeout(() => maybeHideSplash(true), 7000);

function start() {
  host = DEMO ? window.DemoHost : (window.chrome && window.chrome.webview);
  if (!host) return;
  host.addEventListener('message', e => onMessage(e.data));
  icons();
  $('splashLogo').innerHTML = ART.mascot(); $('navLogo').innerHTML = ART.mascot();
  renderHome(); renderTimetable(); refreshJobs(); renderStatus();
  send({ type: 'ready' });
}
if (DEMO) {
  const s = document.createElement('script'); s.src = 'demo.js';
  s.onload = () => {
    start();
    // ?view=... opens a given screen, for screenshots.
    const q = new URLSearchParams(location.search), v = q.get('view'), tab = q.get('tab');
    setTimeout(() => {
      if (v === 'course') { openCourse(S.courses[0]); setTimeout(() => { if (tab) setTab(tab); else { S.open.add(1); renderLectures(); } if (q.has('chat')) { $('chatText').value = 'מה למדנו בשיעור 4?'; sendChat(); } }, 1200); }
      else if (v) show(v);
    }, 1700);
  };
  document.body.append(s);
}
else start();