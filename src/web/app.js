'use strict';
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const DEMO = new URLSearchParams(location.search).has('demo');
let host = null;
const send = (m) => host && host.postMessage(m);

const S = {
  settings: {}, moodle: 'checking', ai: null, courses: null, events: null, userName: '',
  course: null, sections: [], local: null, notability: null, open: new Set(), tab: 'lessons',
  chatBusy: false, chatHidden: false, view: 'home', site: 'moodle', jobs: new Map(), ready: { settings: false, ai: false }
};

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
  document.querySelectorAll('.nav-item').forEach(n => n.classList.toggle('active', n.dataset.view === nav));
  $('main').scrollTop = 0;
  if (view === 'sites') requestAnimationFrame(() => placeSite(true));
  if (view === 'home') renderHome();
  if (view === 'courses' && !S.courses) loadCourses();
}
document.querySelectorAll('.nav-item').forEach(n => n.onclick = () => show(n.dataset.view));
$('brand').onclick = () => show('home');

// ---------- Status ----------
function aiName() { return (S.ai && S.ai.provider === 'gemini') ? 'Gemini' : 'Claude'; }
function setupText(s) {
  return { 'installing-claude': 'מתקין את Claude...', 'login-claude': 'ממתין להתחברות בדפדפן...', 'installing-node': 'מכין את Gemini...', 'installing-gemini': 'מתקין את Gemini...' }[s] || '';
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
  if (S.update) b.append(bannerEl(`גרסה חדשה זמינה: ${esc(S.update.version)}`, 'להורדה', () => send({ type: 'openLink', url: S.update.url })));
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
    else if (c.installed && c.hasKey) state = '<span class="chip ok"><span data-ic="check"></span>מחובר</span>';
    else if (c.installed) state = '<span class="chip warn">צריך מפתח</span>';
    else { state = '<span class="chip">לא מותקן</span>'; act = '<button class="primary small" data-act="setupGemini">התקן את Gemini</button>'; }
    if (c.installed && !busy) act = `<div class="row" style="width:100%"><input type="password" class="ltr" placeholder="${c.hasKey ? 'המפתח שמור · אפשר להחליף' : 'הדבק כאן מפתח API'}" data-key><button class="small" data-act="saveKey">שמור</button></div>
      <button class="link" data-act="getKey">קבל מפתח חינמי מ-Google AI Studio</button>`;
  }
  const el = h(`<div class="provider${sel ? ' selected' : ''}" data-kind="${kind}">
    ${kind === 'claude' ? '<span class="chip accent tag">מומלץ</span>' : '<span class="chip ok tag">חינמי</span>'}
    <div class="ph"><span class="pl ${kind}" data-ic="${kind}"></span><div><h3>${kind === 'claude' ? 'Claude' : 'Gemini'}</h3></div></div>
    <p>${kind === 'claude' ? 'הכי טוב בסיכומים ארוכים ובבניית דפי לימוד. דורש חשבון Claude (Pro ומעלה מומלץ).' : 'מתאים למי שאין לו Claude. טוב מאוד לצ\'אט ולשאלות, קצת פחות מדויק בסיכומים ארוכים.'}</p>
    <div class="state">${state}</div><div class="act">${act}</div></div>`);
  el.onclick = (e) => {
    const t = e.target.closest('[data-act]');
    if (t) {
      e.stopPropagation();
      const actn = t.dataset.act;
      if (actn === 'setupClaude') { send({ type: 'setProvider', provider: 'claude' }); send({ type: 'setupClaude' }); toast('מתחבר ל-Claude. אם נפתח דפדפן, אשר שם את ההתחברות.'); }
      if (actn === 'setupGemini') { send({ type: 'setProvider', provider: 'gemini' }); send({ type: 'setupGemini' }); }
      if (actn === 'getKey') send({ type: 'openGeminiKeyPage' });
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

// ---------- Home ----------
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
        <div class="info"><div class="name" dir="auto">${esc(e.name)}</div><div class="course" dir="auto">${esc(e.course)}</div></div><span class="chip${e.overdue ? ' bad' : soon ? ' warn' : ''}">${esc(relDay(e.when))}</span></div>`);
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
$('heroPaste').innerHTML = IC.video + 'הורדת הקלטה';
$('heroPaste').onclick = () => { show('recordings'); send({ type: 'paste' }); };
$('allCourses').onclick = () => show('courses');
(function quick() {
  const items = [
    ['c1', 'clipboard', 'הדבק קישור Zoom', 'מורידים הקלטה עם תמלול', () => { show('recordings'); send({ type: 'paste' }); }],
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
  list.forEach((c, i) => g.append(courseCard(c, i)));
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
  resetChat();
  show('course');
  send({ type: 'openCourse', id: c.id, course: c.name });
}
$('backToCourses').onclick = () => { S.course = null; show('courses'); };
$('reloadCourse').onclick = () => S.course && openCourse(S.course);
$('openCourseFolder').onclick = () => S.local && send({ type: 'openPath', path: S.local.dir });
$('syncAll').onclick = () => {
  if (!S.sections.length) return;
  send({ type: 'syncCourse', course: S.course.name, sections: S.sections.map(s => ({ num: s.num, title: s.title, items: s.items })) });
  toast('מוריד את כל הקורס... אפשר להמשיך לעבוד בינתיים');
};
$('examPrepBtn').onclick = () => { setTab('exams'); };
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
  btn('plus', 'הוסף קבצים', 'ghost', () => send({ type: 'addFiles', course: course.name, folder: String(s.num) }));
  if (L.exists) btn('folder', 'תיקייה', 'ghost', () => send({ type: 'openPath', path: L.dir }));

  const st = el.querySelector('.status');
  st.className = 'status' + (s.statusError ? ' error' : '');
  st.innerHTML = (s.busy || s.aiBusy ? '<span class="spinner"></span>' : '') + `<span dir="auto">${esc(s.status || (!L.files && s.items.length ? 'לחץ "הורד" כדי לשמור את החומר במחשב, ואז "סכם את השיעור"' : ''))}</span>`;
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
    <div class="grow"><h2>המוח של המרצה</h2><p class="muted" style="margin:6px 0 14px">מוסיפים מבחנים משנים קודמות, עדיף עם פתרונות. ${aiName()} קורא אותם יחד עם ההרצאות והתרגילים, ולומד איך המרצה חושב: אילו נושאים חוזרים, אילו שאלות הוא נותן, ואיך הוא רוצה שתפתור. בסוף מקבלים פרופיל של המרצה ודף הכנה למבחן.</p>
    <div class="row wrap"><span class="chip ${L.exams ? 'ok' : ''}">${L.exams || 0} קבצי מבחנים</span>${L.lecturerProfile ? '<span class="chip ok">פרופיל מרצה מוכן ✓</span>' : ''}${L.examPrep ? '<span class="chip ok">דף הכנה מוכן ✓</span>' : ''}</div></div></div>
    <div class="row wrap" style="margin-top:18px" id="examActs"></div></div>`);
  p.append(card);
  const acts = card.querySelector('#examActs');
  const add = (ic, t, cls, fn, dis) => { const b = h(`<button class="${cls}"><span data-ic="${ic}"></span>${t}</button>`); b.onclick = fn; b.disabled = !!dis; acts.append(b); };
  add('exam', 'חפש מבחנים ב-CS24', 'soft', () => { $('dlCourse').value = S.course.name; $('dlFolder').value = 'מבחנים'; syncDownloadTarget(); openSite('cs24'); toast('כל קובץ שתוריד מ-CS24 יישמר בתיקיית המבחנים של הקורס'); });
  add('plus', 'הוסף מבחנים מהמחשב', 'ghost', () => send({ type: 'addFiles', course: S.course.name, folder: 'מבחנים' }));
  if (L.exams) add('folder', 'תיקיית המבחנים', 'ghost', () => send({ type: 'openPath', path: L.dir + '\\מבחנים' }));
  add('brain', busy ? 'עובד על זה...' : (L.lecturerProfile ? 'בנה מחדש' : 'בנה את המוח של המרצה'), 'primary', () => { S.examBusy = true; send({ type: 'examPrep', course: S.course.name }); renderExams(); }, busy || !L.exams);
  if (L.examPrep) add('external', 'דף ההכנה למבחן', 'primary', () => send({ type: 'openPath', path: L.examPrep }));
  if (L.lecturerProfile) add('user', 'פרופיל המרצה', '', () => send({ type: 'openPath', path: L.lecturerProfile }));
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
    return;
  }
  const opts = ['<option value="">בחר את הנושא ב-Notability...</option>'].concat((N.subjects || []).map(s => `<option ${s === (N.linked || N.guess) ? 'selected' : ''}>${esc(s)}</option>`)).join('');
  const card = h(`<div class="card pad-lg"><div class="row" style="gap:18px;align-items:flex-start"><div style="width:150px;flex:none">${ART.folders().replace('<svg', '<svg style="width:150px;height:96px"')}</div>
    <div class="grow"><h2>המחברת שלי</h2><p class="muted" style="margin:6px 0 14px">בחר את הנושא ב-Notability שמתאים לקורס. כל דף חדש שתכתוב בהרצאה יועתק לכאן לבד, ו-${aiName()} ישתמש בו בסיכומים.</p>
    <div class="row"><select id="notSubject">${opts}</select><button class="primary" id="notLink">${N.linked ? 'עדכן' : 'קשר'}</button></div>
    <div class="row wrap" style="margin-top:12px">${N.linked ? `<span class="chip ok">מקושר ל"${esc(N.linked)}"</span>` : N.guess ? `<span class="chip accent">הצעה: ${esc(N.guess)}</span>` : ''}<span class="chip">${N.notes || 0} דפי מחברת</span></div></div></div></div>`);
  p.append(card);
  $('notLink').onclick = () => send({ type: 'linkNotability', course: S.course.name, folder: $('notSubject').value });
  if (N.notes) { const b = h('<button class="ghost" style="margin-top:12px"><span data-ic="folder"></span>פתח את המחברת</button>'); b.onclick = () => send({ type: 'openPath', path: L.dir + '\\מחברת' }); p.append(b); }
}

// ---------- Chat ----------
function resetChat() {
  $('chatSub').textContent = `${aiName()} · מכיר את כל החומר שהורדת`;
  $('msgs').innerHTML = '';
  const e = h(`<div class="chat-empty">${ART.chat().replace('<svg', '<svg class="art"')}<b>שאל אותי כל דבר על הקורס</b>אני קורא את המצגות, התרגילים, התמלולים והמבחנים שהורדת.
    <div class="suggest"></div></div>`);
  const qs = [['📝', 'תסכם לי את השיעור האחרון'], ['🎯', 'מה הכי חשוב למבחן עד עכשיו?'], ['💡', 'תעזור לי להתחיל את שיעורי הבית האחרונים, רמזים בלבד'], ['🧠', 'איזה סוג שאלות המרצה הזה אוהב לשאול?']];
  for (const [em, q] of qs) { const b = h(`<button class="ghost"></button>`); b.textContent = em + '  ' + q; b.onclick = () => { $('chatText').value = q; sendChat(); }; e.querySelector('.suggest').append(b); }
  $('msgs').append(e);
}
function addMsg(role, text) {
  const empty = $('msgs').querySelector('.chat-empty'); if (empty) empty.remove();
  const d = document.createElement('div');
  d.className = 'msg ' + role; d.dir = 'auto';
  if (role === 'assistant') d.innerHTML = md(text); else d.textContent = text;
  $('msgs').append(d);
  $('msgs').scrollTop = $('msgs').scrollHeight;
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
  send({ type: 'chat', course: S.course.name, text });
}
$('chatSend').onclick = sendChat;
$('chatText').addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendChat(); } });
const autosize = () => { const t = $('chatText'); t.style.height = 'auto'; t.style.height = Math.min(170, t.scrollHeight) + 'px'; };
$('chatText').addEventListener('input', autosize);
$('newChat').onclick = () => { if (!S.course || S.chatBusy) return; send({ type: 'chatReset', course: S.course.name }); resetChat(); };

// ---------- Recordings ----------
const jobIcons = { queued: 'clock', running: 'spin', done: 'check', failed: 'alert' };
function addJob() {
  const url = $('url').value.trim();
  if (!url) { $('url').focus(); return; }
  send({ type: 'add', url, name: $('name').value.trim(), transcript: $('transcript').checked });
  $('url').value = ''; $('name').value = ''; $('url').focus();
}
function renderJob(j) {
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
  if (!S.jobs.size) e.append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>אין הורדות עדיין</b>מעתיקים קישור להקלטה מה-Moodle והוא מופיע כאן לבד. הקלטות של שיעורים יורדות גם מתוך דף הקורס.</div>`));
  const active = [...S.jobs.values()].filter(j => j.state === 'running' || j.state === 'queued').length;
  $('jobCount').hidden = !active; $('jobCount').textContent = active;
}
function jobLog(id, line) {
  const pre = document.querySelector(`.job[data-id="${id}"] pre`); if (!pre) return;
  pre.textContent += line + '\n';
  const lines = pre.textContent.split('\n');
  if (lines.length > 300) pre.textContent = lines.slice(-300).join('\n');
  pre.scrollTop = pre.scrollHeight;
}
$('go').onclick = addJob;
$('url').addEventListener('keydown', e => { if (e.key === 'Enter') addJob(); });
$('name').addEventListener('keydown', e => { if (e.key === 'Enter') addJob(); });
$('paste').onclick = () => send({ type: 'paste' });

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
  $('versionText').textContent = 'HIT Student ' + (st.version || '');
  $('wizStudy').textContent = st.studyRoot;
  $('wizNot').textContent = st.notabilityDir || 'אם Notability מגבה את המחברות לתיקייה במחשב, בחר אותה כאן';
  $('wizNotRow').classList.toggle('ok', !!st.notabilityDir);
}
$('changeStudy').onclick = $('wizStudyBtn').onclick = () => send({ type: 'chooseFolder', which: 'study' });
$('openStudy').onclick = () => send({ type: 'openFolder', which: 'study' });
$('changeRec').onclick = () => send({ type: 'chooseFolder', which: 'recordings' });
$('openRec').onclick = () => send({ type: 'openFolder', which: 'recordings' });
$('changeNotability').onclick = $('wizNotBtn').onclick = () => send({ type: 'chooseFolder', which: 'notability' });
$('showWelcome').onclick = () => openWizard(0);

// ---------- First-run setup ----------
let wizStep = 0, wizPoll = null;
const wizArt = ['welcome', 'ai', 'campus', 'folders', 'done'];
const wizNext = ['בוא נתחיל', 'המשך', 'המשך', 'המשך', 'לאפליקציה'];
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
  $('wizBack').hidden = n === 0 || n === 4;
  $('wizSkip').hidden = n === 4;
  clearInterval(wizPoll);
  if (n === 1 || n === 2) wizPoll = setInterval(() => send({ type: 'checkStatus' }), 5000);
  if (n === 4) confetti();
  renderProviders(); renderWizardMoodle();
}
$('wizNext').onclick = () => {
  if (wizStep === 1 && !(S.ai && S.ai.ready)) toast('אפשר לחבר את המנוע החכם גם אחר כך, מהסטטוס בצד', { ms: 3500 });
  if (wizStep >= 4) { closeWizard(); show('home'); return; }
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
      if (m.state === 'login' && S.view === 'courses' && !S.courses) { $('courses').innerHTML = ''; $('coursesState').innerHTML = ''; $('coursesState').append(h(`<div class="empty">${ART.campus().replace('<svg', '<svg class="art" style="width:220px;height:130px"')}<b>צריך להתחבר ל-Moodle</b>מתחברים פעם אחת עם המשתמש של HIT.<div class="row"><button class="primary" id="cm">התחבר</button></div></div>`)); $('cm').onclick = connectMoodle; }
      if (S.view === 'home') renderHome();
      break;
    case 'home': S.events = m.events || []; if (m.name) S.userName = m.name; renderHome(); renderWizardMoodle(); break;
    case 'courses':
      S.courses = m.data || []; renderCourses(); fillDownloadCourses();
      if (S.view === 'home') renderHome();
      break;
    case 'course': {
      if (!S.course || S.course.id !== m.id) break;
      S.sections = m.sections.map(s => Object.assign(s, { status: '', busy: false, aiBusy: false }));
      S.local = m.local; S.notability = m.notability;
      $('lecturer').value = m.lecturer || '';
      if (!m.lecturer && m.lecturerGuess && m.lecturerGuess.length) { $('lecturer').value = m.lecturerGuess[0]; send({ type: 'setLecturer', course: S.course.name, name: m.lecturerGuess[0] }); toast(`זיהיתי את המרצה: ${m.lecturerGuess[0]}. אפשר לתקן למעלה.`); }
      else if (!m.lecturer) setTimeout(() => $('lecturer').focus(), 600);
      renderCourseHeader(); renderLectures();
      if (S.tab !== 'lessons') setTab(S.tab);
      break;
    }
    case 'courseError':
      if (S.course && S.course.id === m.id) { $('lectures').innerHTML = ''; $('lectures').append(h(`<div class="empty">${ART.empty().replace('<svg', '<svg class="art"')}<b>לא הצלחתי לקרוא את הקורס</b><span dir="auto">${esc(m.message)}</span></div>`)); }
      break;
    case 'sectionStatus': {
      if (!S.course || S.course.name !== m.course) break;
      const s = section(m.num); if (!s) break;
      s.status = m.text; s.statusError = m.error; if (!s.aiBusy) s.busy = m.busy;
      refreshSection(m.num); break;
    }
    case 'sectionLocal': { if (!S.course || S.course.name !== m.course) break; const s = section(m.num); if (s) { s.local = m.local; refreshSection(m.num); } break; }
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
      break;
    case 'aiRun': {
      const parts = m.key.split('|');
      if (!S.course || S.course.name !== parts[1]) break;
      if (parts[0] === 'sum') {
        const s = section(+parts[2]); if (!s) break;
        s.aiBusy = m.busy; if (!m.busy) s.busy = false;
        refreshSection(s.num);
        const log = document.querySelector(`.lecture[data-num="${s.num}"] .ai-log`);
        if (log && m.busy) { log.innerHTML = ''; log.classList.add('show'); }
        if (log && !m.busy && m.result) { const d = document.createElement('div'); d.className = 'final'; d.dir = 'auto'; d.innerHTML = md(m.result); log.append(d); log.scrollTop = log.scrollHeight; }
      } else if (!m.busy && m.result && !m.error) toast(parts[0] === 'info' ? 'ניתוח הקורס מוכן' : 'המוח של המרצה מוכן');
      break;
    }
    case 'aiLog': {
      const parts = m.key.split('|');
      if (!S.course || S.course.name !== parts[1] || parts[0] !== 'sum') break;
      const log = document.querySelector(`.lecture[data-num="${parts[2]}"] .ai-log`); if (!log) break;
      log.classList.add('show');
      const d = document.createElement('div'); d.dir = 'auto'; d.textContent = m.text; log.append(d); log.scrollTop = log.scrollHeight;
      break;
    }
    case 'chat':
      if (!S.course || S.course.name !== m.course) break;
      if (thinking) { thinking.remove(); thinking = null; }
      addMsg(m.role, m.text);
      if (S.chatBusy && m.role === 'assistant') setThinking('ממשיך...');
      break;
    case 'chatTool': if (S.course && S.course.name === m.course) setThinking(m.text); break;
    case 'chatDone': if (thinking) { thinking.remove(); thinking = null; } S.chatBusy = false; $('chatSend').disabled = false; break;
    case 'job': renderJob(m); break;
    case 'log': jobLog(m.id, m.line); break;
    case 'clipboard':
      if (m.force || !$('url').value.trim()) {
        $('url').value = m.url;
        $('url').classList.remove('flash'); void $('url').offsetWidth; $('url').classList.add('flash');
        if (!m.force && S.view !== 'recordings') toast('העתקת קישור להקלטת Zoom', { action: 'הורד', onAction: () => show('recordings') });
      }
      break;
    case 'toast': toast(m.text, { error: m.error }); break;
    case 'update': S.update = m; renderBanners(); break;
    case 'notabilitySubjects': if (S.course) send({ type: 'openCourse', id: S.course.id, course: S.course.name }); break;
    case 'siteNav': $('siteUrl').textContent = m.url || ''; $('siteBack').disabled = !m.canBack; $('siteForward').disabled = !m.canForward; break;
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
  $('splashLogo').innerHTML = IC.grad; $('navLogo').innerHTML = IC.grad;
  renderHome(); refreshJobs(); renderStatus();
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
