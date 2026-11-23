'use strict';

/* ============ أدوات عامة ============ */
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = (s = '') => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const icon = (name, cls = '') => `<svg class="i ${cls}"><use href="#i-${name}"/></svg>`;

const store = {
  get(k, d = null) { try { const v = localStorage.getItem(k); return v === null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch {} },
  del(k) { try { localStorage.removeItem(k); } catch {} }
};

/* ============ الحالة ============ */
const MAX_VOICE_SECONDS = 60;
const THEMES = [
  { id: 'dark',  name: 'داكن',   colors: ['#0b0b0c', '#121213', '#f1f1f1'] },
  { id: 'sea',   name: 'بحري',   colors: ['#0b1a28', '#11243a', '#4fc3b1'] },
  { id: 'auto',  name: 'تلقائي', colors: ['#f3f2ef', '#0b0b0c', '#2a5b8c'] },
  { id: 'light', name: 'فاتح',   colors: ['#f3f2ef', '#ffffff', '#2a5b8c'] },
  { id: 'paper', name: 'ورقي',   colors: ['#e9dfca', '#f6efdd', '#8a4a26'] },
  { id: 'olive', name: 'زيتوني', colors: ['#e6e9dc', '#f5f7ee', '#4b6a2c'] }
];
const TOPICS = ['السيارات', 'الطبخ', 'التقنية', 'الكتب', 'العمل', 'الصحة', 'السفر', 'المال'];
const INTERESTS = ['الدين والقيم', 'الأسرة', 'النصائح والتجارب', 'الكتب', 'العمل', 'التقنية', 'الصحة', 'السفر', 'المال', 'الطبخ', 'السيارات', 'الرياضة'];
const GUEST_SUFFIX = '@guest.invalid';

let token = store.get('shoura_token');
let user = store.get('shoura_user');
let settings = Object.assign({ theme: 'dark', size: 'md', voiceOff: false }, store.get('shoura_settings', {}));
let posts = [];            // آخر ما جُلب من الخادم
let view = 'home';
let tab = 'latest';
let query = '';            // نص البحث الحالي
let authMode = 'login';
let attached = null;       // { file, type }
let openThreads = new Set();
let activeContact = null;
let contacts = [];
let convoTimer = null;
let prefs = store.get('shoura_prefs', []);
let pendingPublish = false;
let regPicks = new Set();

const likedKey = () => `shoura_liked_${user?.id || 'guest'}`;
const repostedKey = () => `shoura_reposted_${user?.id || 'guest'}`;
const liked = () => new Set(store.get(likedKey(), []));
const reposted = () => new Set(store.get(repostedKey(), []));

const isGuest = () => !!(user && user.email && user.email.endsWith(GUEST_SUFFIX));

function randomHex(bytes) {
  const a = new Uint8Array(bytes); crypto.getRandomValues(a);
  return [...a].map(b => b.toString(16).padStart(2, '0')).join('');
}

function saveSession(r) {
  token = r.accessToken; user = r.user;
  store.set('shoura_token', token); store.set('shoura_user', user);
}

async function guestRelogin() {
  const g = store.get('shoura_guest');
  if (!g) return false;
  try {
    const r = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: g.email, password: g.password, acceptTerms: true }) });
    if (!r.ok) return false;
    saveSession(await r.json());
    return true;
  } catch { return false; }
}

async function createGuest() {
  const id = randomHex(6);
  const creds = { email: `g${id}${GUEST_SUFFIX}`, password: randomHex(16) };
  const r = await api('/api/auth/register', { method: 'POST', body: JSON.stringify({ name: 'زائر', handle: `guest_${id}`, email: creds.email, password: creds.password, acceptTerms: true }) });
  store.set('shoura_guest', creds);
  saveSession(r);
}

/* ============ الشبكة ============ */
async function api(path, opt = {}) {
  opt.headers = { ...(opt.headers || {}) };
  if (token) opt.headers.Authorization = `Bearer ${token}`;
  if (opt.body && !(opt.body instanceof FormData)) opt.headers['Content-Type'] = 'application/json';
  let r;
  try { r = await fetch(path, opt); } catch { throw new Error('تعذر الاتصال بالخادم.'); }
  const data = await r.json().catch(() => ({}));
  if (r.status === 401 && token) {
    if (isGuest() && !opt._retried && await guestRelogin()) return api(path, { ...opt, _retried: true, headers: {} });
    logout(true); throw new Error('انتهت الجلسة، سجّل الدخول من جديد.');
  }
  if (!r.ok) throw new Error(data.message || 'تعذر إتمام الطلب، حاول مرة أخرى.');
  return data;
}

let toastTimer;
function toast(t) {
  const el = $('#toast');
  el.textContent = t;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('show'), 2600);
}

/* ============ الإعدادات والثيمات ============ */
const darkMq = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

function applySettings() {
  const theme = settings.theme === 'auto' ? (darkMq && darkMq.matches ? 'dark' : 'light') : settings.theme;
  document.documentElement.setAttribute('data-theme', theme);
  document.documentElement.setAttribute('data-size', settings.size);
  const bg = getComputedStyle(document.documentElement).getPropertyValue('--bg').trim();
  $('meta[name=theme-color]')?.setAttribute('content', bg || '#f3f2ef');
  renderThemeGrid();
  $$('#sizeSeg button').forEach(b => b.classList.toggle('on', b.dataset.size === settings.size));
  $('#voiceOff').checked = !!settings.voiceOff;
  applyVoiceSetting();
}

function saveSettings(patch) {
  settings = { ...settings, ...patch };
  store.set('shoura_settings', settings);
  applySettings();
}

if (darkMq) darkMq.addEventListener?.('change', () => { if (settings.theme === 'auto') applySettings(); });

function renderThemeGrid() {
  $('#themeGrid').innerHTML = THEMES.map(t => `
    <button type="button" class="theme ${t.id === settings.theme ? 'on' : ''}" data-theme-id="${t.id}" aria-pressed="${t.id === settings.theme}">
      <div class="swatch">${t.colors.map(c => `<i style="background:${c}"></i>`).join('')}</div>${t.name}
    </button>`).join('');
}

/* عند تعطيل الصوت: يختفي زر الميكروفون ويتوقف أي تسجيل جارٍ */
function applyVoiceSetting() {
  const off = !!settings.voiceOff;
  $('#recordBtn').classList.toggle('hidden', off);
  $('#recordTimer').classList.toggle('hidden', off);
  $$('.js-voice-btn').forEach(b => b.classList.toggle('hidden', off));
  if (off) {
    stopAllRecording(true);
    if (attached?.type === 'audio') clearAttachment();
  }
}

/* ============ التسجيل الصوتي ============ */
const rec = { recorder: null, stream: null, chunks: [], timer: null, seconds: 0, onDone: null, onTick: null, discard: false };

function recording() { return rec.recorder && rec.recorder.state === 'recording'; }

async function startRecording(onTick, onDone) {
  if (settings.voiceOff) return;
  if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) {
    toast('المتصفح لا يدعم التسجيل الصوتي.');
    return;
  }
  try {
    rec.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
  } catch {
    toast('يحتاج التسجيل إلى إذن الميكروفون واتصال آمن (HTTPS).');
    return;
  }
  if (settings.voiceOff) { rec.stream.getTracks().forEach(t => t.stop()); return; }
  rec.chunks = []; rec.seconds = 0; rec.discard = false; rec.onDone = onDone; rec.onTick = onTick;
  rec.recorder = new MediaRecorder(rec.stream);
  rec.recorder.ondataavailable = e => { if (e.data.size) rec.chunks.push(e.data); };
  rec.recorder.onstop = () => {
    clearInterval(rec.timer);
    rec.stream?.getTracks().forEach(t => t.stop());
    const type = rec.recorder.mimeType || 'audio/webm';
    const seconds = rec.seconds;
    const done = rec.onDone, discard = rec.discard;
    rec.recorder = null; rec.stream = null;
    if (discard || !rec.chunks.length) { onTick(null); return; }
    const blob = new Blob(rec.chunks, { type });
    const ext = type.includes('ogg') ? 'ogg' : type.includes('mp4') ? 'm4a' : 'webm';
    done(new File([blob], `voice-${Date.now()}.${ext}`, { type }), seconds);
  };
  rec.recorder.start();
  onTick(0);
  rec.timer = setInterval(() => {
    rec.seconds++;
    onTick(rec.seconds);
    if (rec.seconds >= MAX_VOICE_SECONDS) { stopRecording(); toast('وصل التسجيل إلى الحد الأقصى: 60 ثانية.'); }
  }, 1000);
}

function stopRecording() { if (recording()) rec.recorder.stop(); }
function stopAllRecording(discard) {
  if (recording()) { rec.discard = !!discard; rec.recorder.stop(); }
  else rec.stream?.getTracks().forEach(t => t.stop());
}
const clock = s => `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;

/* ============ تنسيق ============ */
const rtf = new Intl.RelativeTimeFormat('ar', { numeric: 'auto' });
function ago(iso) {
  const d = new Date(iso.endsWith('Z') || /[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z');
  const diff = (d - Date.now()) / 1000;
  const abs = Math.abs(diff);
  if (abs < 60) return 'الآن';
  if (abs < 3600) return rtf.format(Math.round(diff / 60), 'minute');
  if (abs < 86400) return rtf.format(Math.round(diff / 3600), 'hour');
  if (abs < 86400 * 7) return rtf.format(Math.round(diff / 86400), 'day');
  return new Intl.DateTimeFormat('ar', { dateStyle: 'medium' }).format(d);
}
function formatText(s) {
  return esc(s).replace(/(^|\s)#([\p{L}\d_]+)/gu, '$1<a class="hashtag" href="#" data-hash="$2">#$2</a>').replace(/\n/g, '<br>');
}
function avatar(name, handle, cls = '') {
  const seed = [...(handle || name || 'x')].reduce((a, c) => a + c.charCodeAt(0), 0);
  const hue = (seed * 47) % 360;
  return `<div class="avatar ${cls}" style="background:hsl(${hue} 32% 40%)">${esc((name || 'ش').trim().slice(0, 1))}</div>`;
}

/* التصنيفات القديمة كانت تبدأ برمز تعبيري؛ نزيله عند العرض لتبقى المنشورات القديمة نظيفة */
const LEADING_SYMBOLS = /^[\p{Extended_Pictographic}\p{Emoji_Modifier}\uFE0F\u200D\u20E3\s]+/u;
const KNOWN_TAGS = ['مستعجل', 'حيرة ومقارنة', 'قرار شرائي', 'مشكلة تحتاج حلاً', 'مفترق طرق', 'فضفضة ودعم', 'سؤال مجهول', 'تم الحل', 'مجرّب فعلياً', 'تحديث بعد مدة', 'بديل أذكى', 'تحذير أو تجربة سيئة', 'رأي خبير', 'الزبدة', 'خطوة بخطوة', 'توفير', 'جودة عالية', 'محلي',
  'مشكلة وتحتاج حل', 'تحذير / تجربة سيئة', 'خبير في المجال', 'الزبدة / مختصر', 'اقتصادي / توفير'];
function splitTag(content) {
  const nl = content.indexOf('\n');
  const first = (nl === -1 ? content : content.slice(0, nl)).replace(LEADING_SYMBOLS, '').trim();
  if (first && KNOWN_TAGS.includes(first)) return { tag: first, body: nl === -1 ? '' : content.slice(nl + 1).trim() };
  return { tag: '', body: content.replace(LEADING_SYMBOLS, m => (/\p{Extended_Pictographic}/u.test(m) ? '' : m)) };
}

/* ============ المنشورات ============ */
function postHtml(p) {
  const u = p.user || {};
  const { tag, body } = splitTag(p.content || '');
  let media = '';
  if (p.mediaUrl) {
    const url = esc(p.mediaUrl);
    media = p.mediaType === 'video' ? `<video class="post-media" controls preload="metadata" src="${url}"></video>`
      : p.mediaType === 'audio' ? `<audio class="post-media" controls preload="none" src="${url}"></audio>`
      : `<img class="post-media" loading="lazy" src="${url}" alt="">`;
  }
  const isLiked = liked().has(p.id), isRep = reposted().has(p.id);
  const comments = p.comments || [];
  const open = openThreads.has(p.id);
  return `<article class="post" data-id="${p.id}">
    ${avatar(u.name, u.handle)}
    <div class="post-main">
      <div class="post-top">
        <span class="post-name">${esc(u.name || 'عضو')}</span>
        <span class="handle">@${esc(u.handle || 'user')}</span>
        <span class="time">${ago(p.createdAt)}</span>
        ${tag ? `<span class="badge">${esc(tag)}</span>` : ''}
      </div>
      ${body ? `<div class="post-text">${formatText(body)}</div>` : ''}
      ${media}
      <div class="actions">
        <button class="act reply ${open ? 'on' : ''}" data-act="reply" aria-label="الردود">${icon('reply')}<span>${comments.length || ''}</span></button>
        <button class="act repeat ${isRep ? 'on' : ''}" data-act="repost" aria-label="إعادة مشاركة">${icon('repeat')}<span>${p.retweetsCount || ''}</span></button>
        <button class="act like ${isLiked ? 'on' : ''}" data-act="like" aria-label="إعجاب">${icon('heart')}<span>${p.likesCount || ''}</span></button>
      </div>
      <div class="thread ${open ? '' : 'hidden'}">
        ${comments.map(c => `<div class="comment"><b>${esc(c.name)}</b><span class="handle">@${esc(c.handle)}</span>${esc(c.content)}</div>`).join('')}
        <form class="comment-form"><input name="content" maxlength="2000" placeholder="اكتب ردك" autocomplete="off"><button class="btn primary small">رد</button></form>
      </div>
    </div>
  </article>`;
}

const KEYWORDS = {
  'الدين والقيم': ['دين', 'إيمان', 'صلاة', 'قرآن', 'دعاء', 'موعظ', 'أخلاق', 'قيم', 'توبة', 'صبر'],
  'الأسرة': ['أسرة', 'عائلة', 'أولاد', 'زوج', 'تربية', 'والد', 'أم ', 'أب '],
  'النصائح والتجارب': ['نصيحة', 'تجربة', 'تجارب', 'خطوة', 'الزبدة', 'مجرّب', 'مجرب'],
  'الكتب': ['كتاب', 'كتب', 'رواية', 'قراءة'], 'العمل': ['عمل', 'وظيفة', 'مدير', 'راتب', 'مهنة'],
  'التقنية': ['تقنية', 'برمج', 'تطبيق', 'جوال', 'حاسوب', 'نظام', 'تحديث'], 'الصحة': ['صحة', 'علاج', 'نوم', 'غذاء', 'رياضة'],
  'السفر': ['سفر', 'رحلة', 'فندق', 'سياحة', 'طيران'], 'المال': ['مال', 'ادخار', 'استثمار', 'سعر', 'توفير', 'ميزانية'],
  'الطبخ': ['طبخ', 'وصفة', 'قهوة', 'أكل', 'مطبخ'], 'السيارات': ['سيارة', 'سيارتي', 'محرك', 'فرامل', 'زيت'], 'الرياضة': ['رياضة', 'كرة', 'تمرين', 'لياقة']
};
function matchesPrefs(p) {
  const text = (p.content || '').toLowerCase();
  return prefs.some(k => text.includes(k.toLowerCase()) || (KEYWORDS[k] || []).some(w => text.includes(w)));
}
function updateForYouTab() {
  const el = $('#tabForYou');
  el.classList.toggle('hidden', !prefs.length);
  if (!prefs.length && tab === 'foryou') { tab = 'latest'; $$('#feedTabs button').forEach(b => b.classList.toggle('active', b.dataset.tab === 'latest')); }
  $$('#feedTabs button').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
}

function visiblePosts(list) {
  let items = [...list];
  if (tab === 'foryou' && view === 'home') items = items.filter(matchesPrefs);
  if (query && !query.startsWith('#')) {
    const q = query.toLowerCase();
    items = items.filter(p => (p.content + ' ' + (p.user?.name || '') + ' ' + (p.user?.handle || '')).toLowerCase().includes(q));
  }
  if (tab === 'top' && view === 'home') items.sort((a, b) => (b.likesCount + b.retweetsCount + (b.comments?.length || 0)) - (a.likesCount + a.retweetsCount + (a.comments?.length || 0)));
  return items;
}

function renderFeeds() {
  const fill = (el, items, emptyText) => { el.innerHTML = items.length ? items.map(postHtml).join('') : `<div class="empty">${emptyText}</div>`; };
  if (view === 'home') {
    fill($('#feed'), visiblePosts(posts), query ? 'لا توجد نتائج مطابقة.' : tab === 'foryou' ? 'لا توجد مشاركات تطابق اهتماماتك حتى الآن. جرّب تبويب «الأحدث».' : 'لا توجد مشاركات بعد. كن أول من يكتب.');
    const note = $('#filterNote');
    note.classList.toggle('hidden', !query);
    note.innerHTML = query ? `<span>نتائج: ${esc(query)}</span><button data-clear>إلغاء</button>` : '';
  }
  if (view === 'explore') fill($('#exploreFeed'), query ? visiblePosts(posts) : [], 'لا توجد نتائج مطابقة.');
  if (view === 'explore' && !query) $('#exploreFeed').innerHTML = '';
  if (view === 'profile') renderProfile();
  renderTrending();
}

async function loadFeed() {
  try {
    const tagQ = query.startsWith('#') ? `&hashtag=${encodeURIComponent(query.slice(1))}` : '';
    const data = await api(`/api/posts?limit=50${tagQ}`);
    posts = data.items || [];
    renderFeeds();
  } catch (e) {
    $('#feed').innerHTML = `<div class="empty">${esc(e.message)}</div>`;
  }
}

function renderTrending() {
  const counts = {};
  posts.forEach(p => (p.content.match(/#[\p{L}\d_]+/gu) || []).forEach(h => counts[h] = (counts[h] || 0) + 1));
  const list = Object.entries(counts).sort((a, b) => b[1] - a[1]).slice(0, 6);
  const html = list.length
    ? list.map(([h, n]) => `<button class="trend" data-hash="${esc(h.slice(1))}"><b>${esc(h)}</b><small>${n} ${n > 2 ? 'مشاركات' : 'مشاركة'}</small></button>`).join('')
    : '<div class="note">لا توجد وسوم بعد. أضف #وسم داخل منشورك.</div>';
  $('#trending').innerHTML = html;
  $('#trendingMobile').innerHTML = html;
}

/* ============ الملف الشخصي ============ */
function renderProfile() {
  if (!user) {
    $('#profileHead').innerHTML = `<div class="empty" style="width:100%">سجّل الدخول لعرض حسابك.<br><br><button class="btn primary" data-open-auth>دخول</button></div>`;
    $('#profileFeed').innerHTML = '';
    return;
  }
  const mine = posts.filter(p => p.userId === user.id);
  const likes = mine.reduce((a, p) => a + (p.likesCount || 0), 0);
  $('#profileHead').innerHTML = `${avatar(user.name, user.handle, 'lg')}
    <div><h2>${esc(user.name)}</h2><span class="handle">@${esc(user.handle)}</span>
    <div class="profile-stats"><span><b>${mine.length}</b> منشور</span><span><b>${likes}</b> إعجاب</span></div></div>`;
  $('#profileFeed').innerHTML = mine.length ? mine.map(postHtml).join('') : '<div class="empty">لم تنشر شيئاً بعد.</div>';
}

/* ============ الاهتمامات ============ */
function renderChips(el, selected) {
  el.innerHTML = INTERESTS.map(k => `<button type="button" class="chip ${selected.has(k) ? 'on' : ''}" data-chip="${esc(k)}" aria-pressed="${selected.has(k)}">${esc(k)}</button>`).join('');
}
function renderPrefChips() { renderChips($('#prefChips'), new Set(prefs)); }
function savePrefs(list) { prefs = list; store.set('shoura_prefs', prefs); updateForYouTab(); renderPrefChips(); renderFeeds(); }

/* ============ الجانب: المستخدم والأشخاص ============ */
function renderUser() {
  const box = $('#sideUser');
  if (user) {
    box.innerHTML = `<div class="me-chip">${avatar(user.name, user.handle)}<div class="who"><b>${esc(user.name)}${isGuest() ? '<span class="guest-tag">زائر</span>' : ''}</b><small>@${esc(user.handle)}</small></div></div>`;
  } else {
    box.innerHTML = `<button class="btn primary wide" data-open-auth style="margin:0">دخول</button>`;
  }
  $('#recordBtn').classList.toggle('locked', !user || isGuest());
  $('label.tool').classList.toggle('locked', !user || isGuest());
  $('#composerAvatar').outerHTML = (user ? avatar(user.name, user.handle) : '<div class="avatar">؟</div>').replace('class="avatar', 'id="composerAvatar" class="avatar');
}

async function loadPeople() {
  const box = $('#people');
  if (!user) { box.innerHTML = '<div class="note">سجّل الدخول لرؤية الأعضاء.</div>'; return; }
  try {
    contacts = await api('/api/users');
    box.innerHTML = contacts.slice(0, 5).map(c => `<div class="person">${avatar(c.name, c.handle)}<div class="who"><b>${esc(c.name)}</b><small>@${esc(c.handle)}</small></div><button class="btn ghost small" data-chat="${c.id}">رسالة</button></div>`).join('') || '<div class="note">لا يوجد أعضاء آخرون بعد.</div>';
  } catch { box.innerHTML = ''; }
}

/* ============ الرسائل ============ */
function renderContacts() {
  $('#contacts').innerHTML = user
    ? (contacts.map(c => `<button class="contact ${activeContact?.id === c.id ? 'on' : ''}" data-chat="${c.id}">${avatar(c.name, c.handle)}<span class="who"><b>${esc(c.name)}</b><small>@${esc(c.handle)}</small></span></button>`).join('') || '<div class="empty">لا يوجد أعضاء بعد.</div>')
    : '<div class="empty">سجّل الدخول لاستخدام الرسائل.<br><br><button class="btn primary" data-open-auth>دخول</button></div>';
}

async function openChat(id) {
  if (!user) return openAuth();
  const c = contacts.find(x => x.id === Number(id));
  if (!c) return;
  activeContact = c;
  go('messages');
  $('#messagesBox').classList.add('in-convo');
  $('#convo').innerHTML = `
    <div class="convo-head"><button class="convo-back" data-convo-back aria-label="رجوع">${icon('back')}</button>${esc(c.name)} <span class="handle">@${esc(c.handle)}</span></div>
    <div class="convo-log" id="convoLog"><div class="loading">جارٍ التحميل</div></div>
    <form class="convo-form" id="convoForm" autocomplete="off">
      <button type="button" class="tool js-voice-btn ${settings.voiceOff ? 'hidden' : ''}" id="dmRec" title="رسالة صوتية">${icon('mic')}</button>
      <span class="rec-timer js-voice-btn ${settings.voiceOff ? 'hidden' : ''}" id="dmTimer"></span>
      <input type="text" id="dmText" maxlength="5000" placeholder="اكتب رسالة">
      <button class="btn primary" aria-label="إرسال">${icon('send')}</button>
    </form>`;
  renderContacts();
  await refreshChat(true);
  clearInterval(convoTimer);
  convoTimer = setInterval(() => { if (view === 'messages' && activeContact) refreshChat(false); }, 8000);
}

async function refreshChat(scroll) {
  if (!activeContact) return;
  try {
    const msgs = await api(`/api/messages/${activeContact.id}`);
    const log = $('#convoLog');
    if (!log) return;
    const atBottom = scroll || log.scrollHeight - log.scrollTop - log.clientHeight < 60;
    log.innerHTML = msgs.length ? msgs.map(m => {
      const mine = m.senderId === user.id;
      const body = m.mediaUrl ? `<audio controls preload="none" src="${esc(m.mediaUrl)}"></audio>` : '';
      return `<div class="bubble ${mine ? 'mine' : ''}">${m.content ? esc(m.content) : ''}${body}<small>${ago(m.sentAt)}</small></div>`;
    }).join('') : '<div class="empty">لا توجد رسائل بعد.</div>';
    if (atBottom) log.scrollTop = log.scrollHeight;
  } catch (e) {
    const log = $('#convoLog'); if (log) log.innerHTML = `<div class="empty">${esc(e.message)}</div>`;
  }
}

async function sendDm({ content = '', file = null }) {
  let mediaUrl = null, mediaType = 'text';
  if (file) {
    const fd = new FormData(); fd.append('file', file);
    const up = await api('/api/media/upload', { method: 'POST', body: fd });
    mediaUrl = up.url; mediaType = 'audio';
  }
  await api('/api/messages', { method: 'POST', body: JSON.stringify({ receiverId: activeContact.id, content, mediaUrl, mediaType }) });
  await refreshChat(true);
}

/* ============ التنقل ============ */
function go(name) {
  if (name === 'settings') return openSettings();
  if (view === 'messages' && name !== 'messages') clearInterval(convoTimer);
  view = name;
  $$('.view').forEach(v => v.classList.toggle('hidden', v.id !== `view-${name}`));
  $$('[data-nav]').forEach(b => b.classList.toggle('active', b.dataset.nav === name));
  if (name === 'messages') { renderContacts(); if (user && !contacts.length) loadPeople().then(renderContacts); }
  if (name === 'explore' && !$('#topics').innerHTML) $('#topics').innerHTML = TOPICS.map(t => `<button class="topic" data-topic="${t}">${t}</button>`).join('');
  if (name === 'profile' || name === 'home' || name === 'explore') renderFeeds();
  window.scrollTo(0, 0);
}

function setQuery(q, goTo = 'home') {
  query = q.trim();
  $$('.js-search').forEach(i => i.value = query);
  if (goTo && view !== goTo) go(goTo);
  loadFeed();
}

/* ============ النوافذ ============ */
function openAuth(mode) {
  if (user && !isGuest()) return toast('أنت مسجل الدخول بالفعل.');
  closeModals();
  if (mode) setAuthMode(mode);
  $('#authModal').classList.remove('hidden');
  $('#authForm input:not([type=hidden])')?.focus();
}
function requireAccount(what) {
  toast(user ? `${what} يتطلب حساباً مسجلاً، وليس زائراً.` : `${what} يتطلب تسجيل حساب.`);
  openAuth('register');
}
function closeModals() { $$('.modal').forEach(m => m.classList.add('hidden')); }
function openSettings() {
  const g = isGuest();
  $('#accountBox').classList.toggle('hidden', !user);
  $('#guestInfo').classList.toggle('hidden', !g);
  $('#upgradeBtn').classList.toggle('hidden', !g);
  $('#guestDelete').classList.toggle('hidden', !g);
  $('#deleteLink').classList.toggle('hidden', g);
  renderPrefChips();
  $('#settingsModal').classList.remove('hidden');
}
function setAuthMode(m) {
  authMode = m;
  $$('#authSwitch button').forEach(b => b.classList.toggle('on', b.dataset.mode === m));
  $('#nameField').classList.toggle('hidden', m === 'login');
  $('#termsField').classList.toggle('hidden', m === 'login');
  $('#authForm [name=terms]').required = m === 'register';
  $('#authTitle').textContent = m === 'login' ? 'تسجيل الدخول' : 'إنشاء حساب';
  $('#authForm [name=password]').autocomplete = m === 'login' ? 'current-password' : 'new-password';
  $('#authError').textContent = '';
  if (m === 'register') renderChips($('#regChips'), regPicks);
}
function logout(silent) {
  if (isGuest()) store.del('shoura_guest');
  token = null; user = null; store.del('shoura_token'); store.del('shoura_user');
  contacts = []; activeContact = null; clearInterval(convoTimer);
  renderUser(); loadPeople(); renderFeeds();
  if (!silent) toast('تم تسجيل الخروج.');
}

/* ============ المرفقات والنشر ============ */
function clearAttachment() {
  attached = null;
  $('#mediaFile').value = '';
  const el = $('#attachPreview'); el.classList.add('hidden'); el.innerHTML = '';
}
function showAttachment(html) {
  const el = $('#attachPreview');
  el.innerHTML = `${html}<button type="button" data-remove-attach aria-label="إزالة">${icon('x')}</button>`;
  el.classList.remove('hidden');
}

async function publish(e) {
  e.preventDefault();
  let content = $('#content').value.trim();
  if (!user) { if (!content && !attached) return toast('اكتب شيئاً أولاً.'); pendingPublish = true; return openAuth(); }
  const tag = $('#postTag').value;
  if (!content && !attached) return toast('اكتب شيئاً أو أرفق ملفاً.');
  if (tag) content = `${tag}\n${content}`;
  const btn = $('#publish'); btn.disabled = true;
  try {
    let url = null, type = 'text', size = 0;
    if (attached) {
      const fd = new FormData(); fd.append('file', attached.file);
      const up = await api('/api/media/upload', { method: 'POST', body: fd });
      url = up.url; type = up.mediaType; size = attached.file.size;
    }
    await api('/api/posts', { method: 'POST', body: JSON.stringify({ content, mediaUrl: url, mediaType: type, mediaSizeBytes: size }) });
    $('#content').value = ''; $('#postTag').value = ''; clearAttachment();
    toast('تم النشر.');
    await loadFeed();
  } catch (err) { toast(err.message); }
  finally { btn.disabled = false; }
}

/* ============ الأحداث ============ */
document.addEventListener('click', async e => {
  const t = e.target;

  if (t.closest('[data-close]') || t.classList.contains('modal')) return closeModals();
  if (t.closest('[data-open-auth]')) return openAuth();

  const nav = t.closest('[data-nav]');
  if (nav) { e.preventDefault(); if (nav.dataset.nav === 'home') { query = ''; $$('.js-search').forEach(i => i.value = ''); } return go(nav.dataset.nav); }

  const hash = t.closest('[data-hash]');
  if (hash) { e.preventDefault(); return setQuery('#' + hash.dataset.hash); }
  const topic = t.closest('[data-topic]');
  if (topic) return setQuery(topic.dataset.topic);
  if (t.closest('[data-clear]')) return setQuery('');

  const chat = t.closest('[data-chat]');
  if (chat) return openChat(chat.dataset.chat);
  if (t.closest('[data-convo-back]')) { $('#messagesBox').classList.remove('in-convo'); activeContact = null; clearInterval(convoTimer); return renderContacts(); }

  const theme = t.closest('[data-theme-id]');
  if (theme) return saveSettings({ theme: theme.dataset.themeId });
  const size = t.closest('#sizeSeg [data-size]');
  if (size) return saveSettings({ size: size.dataset.size });
  if (t.closest('#logoutBtn')) { closeModals(); return logout(); }

  const mode = t.closest('#authSwitch [data-mode]');
  if (mode) return setAuthMode(mode.dataset.mode);
  const tabBtn = t.closest('#feedTabs [data-tab]');
  if (tabBtn) { tab = tabBtn.dataset.tab; $$('#feedTabs button').forEach(b => b.classList.toggle('active', b === tabBtn)); return renderFeeds(); }

  /* اهتمامات */
  const chip = t.closest('[data-chip]');
  if (chip) {
    const k = chip.dataset.chip;
    if (chip.parentElement.id === 'regChips') { regPicks.has(k) ? regPicks.delete(k) : regPicks.add(k); renderChips($('#regChips'), regPicks); }
    else savePrefs(prefs.includes(k) ? prefs.filter(x => x !== k) : [...prefs, k]);
    return;
  }

  /* الزائر */
  if (t.closest('#guestBtn')) {
    const btn = $('#guestBtn'); btn.disabled = true;
    try {
      await createGuest();
      closeModals(); renderUser(); loadPeople(); renderFeeds();
      toast('أنت الآن زائر. يمكنك الكتابة والتعليق.');
      if (pendingPublish) { pendingPublish = false; $('#composer').requestSubmit(); }
    } catch (err) { $('#authError').textContent = err.message; }
    finally { btn.disabled = false; }
    return;
  }
  if (t.closest('#upgradeBtn')) { closeModals(); return openAuth('register'); }
  if (t.closest('#guestDelete')) {
    if (!confirm('سيُحذف حساب الزائر وكل ما كتبته به نهائياً. متابعة؟')) return;
    try { await api('/api/account/me', { method: 'DELETE' }); closeModals(); logout(true); toast('تم حذف بيانات الزائر.'); }
    catch (err) { toast(err.message); }
    return;
  }
  const attachLabel = t.closest('label.tool');
  if (attachLabel && (!user || isGuest())) { e.preventDefault(); return requireAccount('رفع الصور والمقاطع'); }

  if (t.closest('#sidePost')) { go('home'); $('#content').focus(); return; }
  if (t.closest('[data-remove-attach]')) return clearAttachment();

  /* أزرار المنشور */
  const act = t.closest('[data-act]');
  if (act) {
    const card = act.closest('.post'); const id = Number(card.dataset.id);
    if (act.dataset.act === 'reply') {
      openThreads.has(id) ? openThreads.delete(id) : openThreads.add(id);
      const th = $('.thread', card); th.classList.toggle('hidden'); act.classList.toggle('on', !th.classList.contains('hidden'));
      if (!th.classList.contains('hidden')) $('input', th).focus();
      return;
    }
    if (!user) return openAuth();
    const kind = act.dataset.act;
    const set = kind === 'like' ? liked() : reposted();
    if (set.has(id)) return toast(kind === 'like' ? 'سبق أن أعجبك هذا المنشور.' : 'سبقت لك إعادة مشاركته.');
    try {
      const d = await api(`/api/posts/${id}/${kind}`, { method: 'POST' });
      set.add(id); store.set(kind === 'like' ? likedKey() : repostedKey(), [...set]);
      const p = posts.find(x => x.id === id);
      if (p) { if (kind === 'like') p.likesCount = d.likesCount; else p.retweetsCount = d.retweetsCount; }
      act.classList.add('on'); $('span', act).textContent = kind === 'like' ? d.likesCount : d.retweetsCount;
    } catch (err) { toast(err.message); }
    return;
  }

  /* تسجيل صوتي: المنشور */
  if (t.closest('#recordBtn')) {
    if (!user || isGuest()) return requireAccount('التسجيل الصوتي');
    const btn = $('#recordBtn');
    if (recording()) return stopRecording();
    clearAttachment();
    await startRecording(
      s => {
        const timer = $('#recordTimer');
        if (s === null) { btn.classList.remove('recording'); timer.textContent = ''; return; }
        btn.classList.add('recording'); timer.textContent = clock(s);
      },
      (file, secs) => {
        btn.classList.remove('recording'); $('#recordTimer').textContent = '';
        attached = { file, type: 'audio' };
        showAttachment(`${icon('mic')}<span>تسجيل صوتي (${clock(secs)})</span><audio controls src="${URL.createObjectURL(file)}"></audio>`);
      }
    );
    return;
  }

  /* تسجيل صوتي: الرسائل */
  if (t.closest('#dmRec')) {
    if (isGuest()) return requireAccount('الرسائل الصوتية');
    const btn = $('#dmRec');
    if (recording()) return stopRecording();
    await startRecording(
      s => {
        const timer = $('#dmTimer'); if (!timer) return;
        if (s === null) { btn.classList.remove('recording'); timer.textContent = ''; return; }
        btn.classList.add('recording'); timer.textContent = clock(s);
      },
      async file => {
        btn.classList.remove('recording'); const timer = $('#dmTimer'); if (timer) timer.textContent = '';
        try { await sendDm({ file }); } catch (err) { toast(err.message); }
      }
    );
  }
});

document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModals(); });

document.addEventListener('submit', async e => {
  const f = e.target;
  if (f.id === 'composer') return publish(e);

  if (f.classList.contains('js-search-form')) {
    e.preventDefault();
    return setQuery($('.js-search', f).value, view === 'explore' ? 'explore' : 'home');
  }

  if (f.classList.contains('comment-form')) {
    e.preventDefault();
    if (!user) return openAuth();
    const input = f.elements.content, text = input.value.trim();
    if (!text) return;
    const id = Number(f.closest('.post').dataset.id);
    try {
      await api(`/api/posts/${id}/comments`, { method: 'POST', body: JSON.stringify({ content: text }) });
      openThreads.add(id);
      await loadFeed();
    } catch (err) { toast(err.message); }
    return;
  }

  if (f.id === 'convoForm') {
    e.preventDefault();
    const input = $('#dmText'), text = input.value.trim();
    if (!text) return;
    input.value = '';
    try { await sendDm({ content: text }); } catch (err) { input.value = text; toast(err.message); }
    return;
  }

  if (f.id === 'authForm') {
    e.preventDefault();
    const d = new FormData(f);
    const payload = { email: d.get('email'), password: d.get('password'), acceptTerms: !!d.get('terms') };
    if (authMode === 'register') Object.assign(payload, { name: d.get('name'), handle: d.get('handle') });
    try {
      const r = await api('/api/auth/' + (authMode === 'login' ? 'login' : 'register'), { method: 'POST', body: JSON.stringify(payload) });
      if (isGuest()) store.del('shoura_guest');
      saveSession(r);
      if (authMode === 'register' && regPicks.size) savePrefs([...regPicks]);
      regPicks.clear();
      f.reset(); closeModals(); renderUser(); loadPeople(); renderFeeds();
      toast(authMode === 'login' ? 'تم تسجيل الدخول.' : 'تم إنشاء حسابك.');
      if (pendingPublish) { pendingPublish = false; $('#composer').requestSubmit(); }
    } catch (err) { $('#authError').textContent = err.message; }
  }
});

document.addEventListener('change', e => {
  if (e.target.id === 'voiceOff') {
    saveSettings({ voiceOff: e.target.checked });
    toast(e.target.checked ? 'تم تعطيل التسجيل الصوتي.' : 'تم تفعيل التسجيل الصوتي.');
  }
  if (e.target.id === 'mediaFile') {
    const file = e.target.files[0]; if (!file) return;
    const isVideo = file.type.startsWith('video/'), isImage = file.type.startsWith('image/');
    if (!isVideo && !isImage) { toast('اختر صورة أو فيديو.'); e.target.value = ''; return; }
    if (isVideo && file.size > 20 * 1024 * 1024) { toast('الحد الأقصى للفيديو 20 ميجابايت.'); e.target.value = ''; return; }
    attached = { file, type: isVideo ? 'video' : 'image' };
    showAttachment(`${icon('image')}<span>${esc(file.name)} · ${(file.size / 1048576).toFixed(1)} م.ب</span>`);
  }
});

document.addEventListener('input', e => {
  if (e.target.id === 'content') { e.target.style.height = 'auto'; e.target.style.height = Math.min(e.target.scrollHeight, 260) + 'px'; }
});

/* ============ البدء ============ */
setAuthMode('login');
applySettings();
updateForYouTab();
renderUser();
loadFeed();
loadPeople();
