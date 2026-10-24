// Icons and original illustrations, drawn as inline SVG so they follow the theme and animate.
const svgI = (d, extra = '') => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" ${extra}>${d}</svg>`;

const IC = {
  home: svgI('<path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V20a1 1 0 0 0 1 1h4v-6h4v6h4a1 1 0 0 0 1-1V9.5"/>'),
  book: svgI('<path d="M4 19.5V5a2 2 0 0 1 2-2h13v16H6a2 2 0 0 0-2 2z"/><path d="M8 7h7M8 11h5"/>'),
  video: svgI('<rect x="2.5" y="6" width="13.5" height="12" rx="2.5"/><path d="m16 10.5 5.5-3v9l-5.5-3"/>'),
  globe: svgI('<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.7 3.8 5.7 3.8 9s-1.3 6.3-3.8 9c-2.5-2.7-3.8-5.7-3.8-9S9.5 5.7 12 3z"/>'),
  settings: svgI('<circle cx="12" cy="12" r="3.2"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>'),
  download: svgI('<path d="M12 3v12"/><path d="m7 10 5 5 5-5"/><path d="M5 21h14"/>'),
  sparkles: svgI('<path d="M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z"/><path d="M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z"/>'),
  folder: svgI('<path d="M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2.5h7.5A2.5 2.5 0 0 1 21 10v7.5a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 17.5z"/>'),
  chat: svgI('<path d="M21 12a8 8 0 0 1-11.6 7.1L4 20.5l1.4-4.6A8 8 0 1 1 21 12z"/>'),
  check: svgI('<path d="M5 12.5l4.5 4.5L19 7.5"/>'),
  x: svgI('<path d="M6 6l12 12M18 6 6 18"/>'),
  alert: svgI('<path d="M12 8v5M12 16.5v.3"/><circle cx="12" cy="12" r="9"/>'),
  refresh: svgI('<path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/>'),
  back: svgI('<path d="M9 6l6 6-6 6"/>'),
  forward: svgI('<path d="M15 6l-6 6 6 6"/>'),
  external: svgI('<path d="M14 3h7v7"/><path d="M10 14 21 3"/><path d="M21 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5"/>'),
  plus: svgI('<path d="M12 5v14M5 12h14"/>'),
  file: svgI('<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/>'),
  clipboard: svgI('<rect x="6" y="4" width="12" height="17" rx="2"/><path d="M9 4.5V3h6v1.5"/><path d="M9 10h6M9 14h4"/>'),
  calendar: svgI('<rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M3.5 10h17M8 3v4M16 3v4"/>'),
  brain: svgI('<path d="M9.5 4A3 3 0 0 0 6.6 7.8 3.3 3.3 0 0 0 4.8 13a3.2 3.2 0 0 0 2.4 5.2A2.7 2.7 0 0 0 12 19.6V5.6A2.5 2.5 0 0 0 9.5 4z"/><path d="M14.5 4a3 3 0 0 1 2.9 3.8 3.3 3.3 0 0 1 1.8 5.2 3.2 3.2 0 0 1-2.4 5.2 2.7 2.7 0 0 1-4.8 1.4"/>'),
  exam: svgI('<path d="M7 3h7l5 5v11a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="m9 14 2 2 4-4"/>'),
  info: svgI('<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 7.6v.3"/>'),
  note: svgI('<path d="M4 20l1-4L16.5 4.5a2.1 2.1 0 0 1 3 3L8 19z"/><path d="M14 7l3 3"/>'),
  play: svgI('<path d="M7 5v14l12-7z"/>'),
  user: svgI('<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>'),
  send: svgI('<path d="M20 12H5"/><path d="m11 6-6 6 6 6"/>'),
  clock: svgI('<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>'),
  spin: svgI('<path d="M21 12a9 9 0 1 1-6.2-8.56"/>', 'class="spin-ic"'),
  link: svgI('<path d="M10 14a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1 1"/><path d="M14 10a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1-1"/>'),
  trash: svgI('<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>'),
  sun: svgI('<circle cx="12" cy="12" r="4.2"/><path d="M12 2.5v2M12 19.5v2M4.6 4.6l1.4 1.4M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4"/>'),
  moon: svgI('<path d="M20.5 14.5A8.5 8.5 0 0 1 9.5 3.5a8.5 8.5 0 1 0 11 11z"/>'),
  key: svgI('<circle cx="8" cy="15" r="4"/><path d="m11 12 9-9M16 7l2 2M14 9l2 2"/>'),
  grad: '<svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 9.5 12 4.5 2 9.5l10 5 10-5z"/><path d="M6 11.6v4.6c3.3 2.4 8.7 2.4 12 0v-4.6"/><path d="M22 9.5v5"/></svg>',
  // Generic marks for the two AI options (not the vendors' logos).
  claude: svgI('<circle cx="12" cy="12" r="8.5"/><path d="M8.5 12.5c1 1.6 2.2 2.4 3.5 2.4s2.5-.8 3.5-2.4M9 9.5h.01M15 9.5h.01"/>'),
  gemini: svgI('<rect x="4" y="4" width="16" height="16" rx="5"/><path d="M9 12h6M12 9v6"/>')
};

const ART = {
  // SumIT's own mascot: a student with dark hair, glasses, a hoodie and a graduation cap, giving a thumbs up.
  // The arm swings up (.mascot-arm) and a small gleam flashes on the smile (.mascot-gleam).
  // Its gradient / clip ids get a unique suffix per copy: with duplicate ids, a hidden copy "wins" and the others go blank.
  mascot: (cls = '') => ART._mascotSvg(cls).replace(/ms(Bg|Hood|Clip)/g, '$&' + (ART._mi = (ART._mi || 0) + 1)),
  _mascotSvg: (cls) => `<svg class="mascot ${cls}" viewBox="0 0 200 200" fill="none">
    <defs>
      <linearGradient id="msBg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#6366f1"/><stop offset=".55" stop-color="#8b5cf6"/><stop offset="1" stop-color="#ec4899"/></linearGradient>
      <linearGradient id="msHood" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#4f46e5"/><stop offset="1" stop-color="#3730a3"/></linearGradient>
      <clipPath id="msClip"><rect x="6" y="6" width="188" height="188" rx="46"/></clipPath>
    </defs>
    <rect x="6" y="6" width="188" height="188" rx="46" fill="url(#msBg)"/>
    <g clip-path="url(#msClip)">
    <circle cx="100" cy="112" r="70" fill="rgba(255,255,255,.12)"/>
    <path d="M44 200c2-40 26-62 56-62s54 22 56 62z" fill="url(#msHood)"/>
    <path d="M86 140l14 16 14-16" stroke="#c7d2fe" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>
    <text x="100" y="186" text-anchor="middle" font-family="Segoe UI, sans-serif" font-weight="800" font-size="17" fill="#e0e7ff">IT</text>
    <circle cx="100" cy="98" r="36" fill="#f6c9a8"/>
    <path d="M64 92c0-24 16-38 36-38s36 14 36 38c-6-10-16-16-36-16s-30 6-36 16z" fill="#2b2140"/>
    <circle cx="64" cy="100" r="6" fill="#efb893"/><circle cx="136" cy="100" r="6" fill="#efb893"/>
    <g stroke="#1e1b4b" stroke-width="3.5"><rect x="74" y="90" width="22" height="16" rx="7" fill="rgba(255,255,255,.35)"/><rect x="104" y="90" width="22" height="16" rx="7" fill="rgba(255,255,255,.35)"/><path d="M96 97h8"/></g>
    <circle cx="85" cy="98" r="3" fill="#1e1b4b"/><path d="M110 99c3-3 8-3 11 0" stroke="#1e1b4b" stroke-width="3" stroke-linecap="round"/>
    <path d="M84 114c8 10 24 10 32 0" fill="#fff" stroke="#1e1b4b" stroke-width="3" stroke-linejoin="round"/>
    <g class="mascot-gleam"><path d="M118 108l2 5 5 2-5 2-2 5-2-5-5-2 5-2z" fill="#fff"/></g>
    <path d="M100 46l46 18-46 18-46-18z" fill="#1e1b4b"/><path d="M76 72v10c14 8 34 8 48 0V72l-24 10z" fill="#312e81"/>
    <path d="M146 64v22" stroke="#fbbf24" stroke-width="3"/><circle cx="146" cy="89" r="4.5" fill="#fbbf24"/>
    <g class="mascot-arm">
      <path d="M52 168c-6-16-6-30 0-44" stroke="#4338ca" stroke-width="18" stroke-linecap="round"/>
      <g transform="translate(52 118)"><rect x="-13" y="-6" width="26" height="22" rx="8" fill="#f6c9a8"/><rect x="-5" y="-24" width="10" height="22" rx="5" fill="#f6c9a8"/><path d="M-13 3h26M-13 9h26" stroke="#e3a986" stroke-width="2"/></g>
    </g>
    </g>
  </svg>`,

  // Desk with a laptop playing a lecture, books, a mug and a plant.
  hero: () => `<svg viewBox="0 0 300 200" fill="none">
    <ellipse cx="150" cy="186" rx="128" ry="9" fill="rgba(0,0,0,.14)"/>
    <rect x="22" y="150" width="256" height="10" rx="5" fill="rgba(255,255,255,.9)"/>
    <g class="float"><rect x="78" y="62" width="128" height="82" rx="9" fill="#1e1b4b"/><rect x="85" y="69" width="114" height="68" rx="5" fill="#eef2ff"/>
      <rect x="93" y="78" width="54" height="7" rx="3.5" fill="#c7d2fe"/><rect x="93" y="91" width="80" height="5" rx="2.5" fill="#e0e7ff"/><rect x="93" y="101" width="66" height="5" rx="2.5" fill="#e0e7ff"/>
      <circle cx="176" cy="113" r="13" fill="#6366f1"/><path d="M172 107v12l10-6z" fill="#fff"/>
      <path d="M66 144h152l-8 6H74z" fill="#c7d2fe"/></g>
    <g><rect x="222" y="118" width="40" height="9" rx="2" fill="#f472b6"/><rect x="226" y="127" width="36" height="9" rx="2" fill="#fbbf24"/><rect x="220" y="136" width="44" height="14" rx="2" fill="#34d399"/>
      <rect x="226" y="140" width="20" height="2.5" rx="1" fill="rgba(255,255,255,.7)"/></g>
    <g><path d="M34 128h22v18a6 6 0 0 1-6 6H40a6 6 0 0 1-6-6z" fill="#fff"/><path d="M56 133h4a5 5 0 0 1 0 10h-4" stroke="#fff" stroke-width="3"/>
      <g class="steam" stroke="rgba(255,255,255,.85)" stroke-width="2.4" stroke-linecap="round"><path d="M41 121c-3-4 3-6 0-10"/><path d="M49 121c-3-4 3-6 0-10"/></g></g>
    <g class="float d2"><path d="M262 60c-12 0-20 12-14 26 10-2 16-12 14-26z" fill="#a7f3d0"/><path d="M270 74c10-2 18 8 14 20-8-2-14-10-14-20z" fill="#6ee7b7"/></g>
    <path d="M266 118V84" stroke="#065f46" stroke-width="2.5"/><path d="M256 104h20l-3 14h-14z" fill="#fff"/>
    <g fill="#fff"><path class="twinkle" d="M40 40l3 8 8 3-8 3-3 8-3-8-8-3 8-3z"/><path class="twinkle d2" d="M230 26l2 5 5 2-5 2-2 5-2-5-5-2 5-2z"/><circle class="twinkle" cx="120" cy="30" r="3"/></g>
  </svg>`,

  welcome: () => `<svg viewBox="0 0 330 200" fill="none">
    <ellipse cx="165" cy="182" rx="110" ry="8" fill="rgba(0,0,0,.15)"/>
    <g class="float"><rect x="104" y="104" width="122" height="20" rx="4" fill="#fbbf24"/><rect x="96" y="124" width="138" height="22" rx="4" fill="#f472b6"/><rect x="108" y="146" width="116" height="24" rx="4" fill="#34d399"/>
      <rect x="116" y="111" width="50" height="4" rx="2" fill="rgba(255,255,255,.7)"/><rect x="108" y="132" width="70" height="4" rx="2" fill="rgba(255,255,255,.7)"/><rect x="120" y="155" width="56" height="4" rx="2" fill="rgba(255,255,255,.7)"/>
      <path d="M165 40 224 68 165 96 106 68z" fill="#1e1b4b"/><path d="M130 79v17c19 13 51 13 70 0V79l-35 17z" fill="#312e81"/><path d="M224 68v30" stroke="#fbbf24" stroke-width="3"/><circle cx="224" cy="101" r="5" fill="#fbbf24"/></g>
    <g fill="#fff"><path class="twinkle" d="M70 50l4 10 10 4-10 4-4 10-4-10-10-4 10-4z"/><path class="twinkle d2" d="M262 36l3 7 7 3-7 3-3 7-3-7-7-3 7-3z"/><path class="twinkle" d="M272 128l2 5 5 2-5 2-2 5-2-5-5-2 5-2z"/><circle class="twinkle d2" cx="60" cy="130" r="4"/></g>
  </svg>`,

  ai: () => `<svg viewBox="0 0 330 200" fill="none">
    <g class="float"><rect x="64" y="46" width="150" height="70" rx="20" fill="#fff"/><path d="M96 116l-12 18 30-18z" fill="#fff"/>
      <rect x="86" y="66" width="86" height="8" rx="4" fill="#c7d2fe"/><rect x="86" y="82" width="106" height="8" rx="4" fill="#e0e7ff"/><rect x="86" y="98" width="62" height="8" rx="4" fill="#e0e7ff"/></g>
    <g class="float d2"><rect x="150" y="104" width="124" height="58" rx="18" fill="#1e1b4b"/><path d="M246 162l12 16-28-16z" fill="#1e1b4b"/>
      <circle cx="184" cy="133" r="6" fill="#a5b4fc"/><circle cx="208" cy="133" r="6" fill="#c4b5fd"/><circle cx="232" cy="133" r="6" fill="#f9a8d4"/></g>
    <g fill="#fff"><path class="twinkle" d="M262 50l5 12 12 5-12 5-5 12-5-12-12-5 12-5z"/><path class="twinkle d2" d="M44 140l3 7 7 3-7 3-3 7-3-7-7-3 7-3z"/></g>
  </svg>`,

  campus: () => `<svg viewBox="0 0 330 200" fill="none">
    <ellipse cx="165" cy="182" rx="120" ry="8" fill="rgba(0,0,0,.15)"/>
    <g class="float"><path d="M165 34 248 74H82z" fill="#1e1b4b"/><rect x="88" y="74" width="154" height="10" fill="#312e81"/>
      <g fill="#fff">${[100, 128, 156, 184, 212].map(x => `<rect x="${x}" y="88" width="16" height="62" rx="3"/>`).join('')}</g>
      <rect x="80" y="150" width="170" height="16" rx="4" fill="#c7d2fe"/><circle cx="165" cy="58" r="8" fill="#fbbf24"/></g>
    <g class="float d2"><circle cx="270" cy="128" r="14" stroke="#fbbf24" stroke-width="6"/><path d="M262 138l-26 26M244 156l6 6M238 162l5 5" stroke="#fbbf24" stroke-width="6" stroke-linecap="round"/></g>
    <g fill="#fff"><path class="twinkle" d="M56 60l4 9 9 4-9 4-4 9-4-9-9-4 9-4z"/><circle class="twinkle d2" cx="290" cy="50" r="4"/></g>
  </svg>`,

  folders: () => `<svg viewBox="0 0 330 200" fill="none">
    <ellipse cx="165" cy="182" rx="110" ry="8" fill="rgba(0,0,0,.15)"/>
    <g class="float"><path d="M84 66a8 8 0 0 1 8-8h40l12 14h86a8 8 0 0 1 8 8v82a8 8 0 0 1-8 8H92a8 8 0 0 1-8-8z" fill="#fbbf24"/>
      <rect x="104" y="48" width="76" height="96" rx="6" fill="#fff" transform="rotate(-8 142 96)"/><rect x="150" y="46" width="76" height="96" rx="6" fill="#eef2ff" transform="rotate(7 188 94)"/>
      <path d="M84 92a8 8 0 0 1 8-8h146a8 8 0 0 1 8 8v70a8 8 0 0 1-8 8H92a8 8 0 0 1-8-8z" fill="#f59e0b"/><rect x="146" y="118" width="38" height="6" rx="3" fill="rgba(255,255,255,.7)"/></g>
    <g fill="#fff"><path class="twinkle" d="M270 56l4 9 9 4-9 4-4 9-4-9-9-4 9-4z"/><circle class="twinkle d2" cx="58" cy="80" r="4"/></g>
  </svg>`,

  done: () => `<svg viewBox="0 0 330 200" fill="none">
    <g class="float"><circle cx="165" cy="96" r="58" fill="#fff"/><circle cx="165" cy="96" r="44" fill="#34d399"/><path d="M143 97l15 15 29-31" stroke="#fff" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/></g>
    <g>${[[60, 50, '#fbbf24'], [272, 60, '#f472b6'], [80, 150, '#a5b4fc'], [258, 150, '#fbbf24'], [110, 30, '#fff'], [226, 24, '#a7f3d0']].map(([x, y, c], i) =>
      `<rect class="twinkle${i % 2 ? ' d2' : ''}" x="${x}" y="${y}" width="10" height="16" rx="3" fill="${c}" transform="rotate(${i * 37} ${x + 5} ${y + 8})"/>`).join('')}</g>
  </svg>`,

  empty: () => `<svg viewBox="0 0 150 110" fill="none">
    <rect x="30" y="18" width="64" height="80" rx="8" fill="var(--surface-3)"/><rect x="42" y="32" width="40" height="6" rx="3" fill="var(--border)"/><rect x="42" y="46" width="30" height="6" rx="3" fill="var(--border)"/>
    <g class="float"><circle cx="96" cy="62" r="20" fill="var(--surface)" stroke="var(--accent)" stroke-width="6"/><path d="M110 77l16 16" stroke="var(--accent)" stroke-width="8" stroke-linecap="round"/></g>
  </svg>`,

  calendar: () => `<svg viewBox="0 0 150 110" fill="none">
    <g class="float"><rect x="35" y="20" width="80" height="74" rx="12" fill="var(--surface)" stroke="var(--border)" stroke-width="3"/><path d="M35 40h80" stroke="var(--accent)" stroke-width="12"/>
      <path d="M58 64l10 10 20-20" stroke="var(--ok)" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/></g>
    <path class="twinkle" d="M124 20l3 7 7 3-7 3-3 7-3-7-7-3 7-3z" fill="var(--accent-2)"/>
  </svg>`,

  chat: () => `<svg viewBox="0 0 150 104" fill="none">
    <g class="float"><rect x="18" y="16" width="80" height="44" rx="14" fill="var(--accent-soft)"/><path d="M36 60l-6 12 18-12z" fill="var(--accent-soft)"/>
      <rect x="30" y="30" width="44" height="5" rx="2.5" fill="var(--accent)"/><rect x="30" y="41" width="56" height="5" rx="2.5" fill="var(--accent-2)" opacity=".6"/></g>
    <g class="float d2"><rect x="62" y="52" width="72" height="36" rx="12" fill="var(--surface-3)"/><circle cx="84" cy="70" r="4" fill="var(--muted)"/><circle cx="98" cy="70" r="4" fill="var(--muted)"/><circle cx="112" cy="70" r="4" fill="var(--muted)"/></g>
  </svg>`,

  // Decorative band for a course card, coloured from the course id.
  band: (seed) => {
    const hues = [244, 262, 199, 160, 330, 24, 280, 214];
    const h = hues[Math.abs(seed) % hues.length];
    const a = `hsl(${h} 78% 60%)`, b = `hsl(${(h + 38) % 360} 72% 52%)`;
    const r = (n) => ((seed * 9301 + n * 49297) % 233280) / 233280;
    // Gradient ids must be unique on the page, or a hidden copy "wins" and the band renders grey.
    const gid = 'g' + seed + '_' + (ART._n = (ART._n || 0) + 1);
    return `<svg viewBox="0 0 300 84" preserveAspectRatio="none" width="100%" height="100%"><defs><linearGradient id="${gid}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${a}"/><stop offset="1" stop-color="${b}"/></linearGradient></defs>
      <rect width="300" height="84" fill="url(#${gid})"/>
      <circle cx="${40 + r(1) * 220}" cy="${r(2) * 84}" r="${30 + r(3) * 30}" fill="rgba(255,255,255,.12)"/>
      <circle cx="${r(4) * 300}" cy="${60 + r(5) * 30}" r="${20 + r(6) * 26}" fill="rgba(255,255,255,.09)"/>
      <path d="M0 ${64 + r(7) * 10} Q 75 ${50 + r(8) * 20} 150 ${64} T 300 ${58 + r(9) * 16} V84 H0z" fill="rgba(255,255,255,.1)"/></svg>`;
  }
};
